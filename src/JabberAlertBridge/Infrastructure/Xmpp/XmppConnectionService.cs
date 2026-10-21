using System.Reactive.Linq;
using System.Xml.Linq;
using JabberAlertBridge.Configuration;
using JabberAlertBridge.Observability;
using JabberAlertBridge.Services.Interfaces;
using Microsoft.Extensions.Options;
using XmppDotNet;
using XmppDotNet.Sasl;
using XmppDotNet.Transport;
using XmppDotNet.Transport.Socket;
using XmppDotNet.Xml;
using XmppDotNet.Xmpp;
using XmppDotNet.Xmpp.Client;
using XmppDotNet.Xmpp.Sasl;

namespace JabberAlertBridge.Infrastructure.Xmpp;

public sealed class XmppConnectionService(
    IOptions<XmppOptions> options,
    BridgeMetrics metrics,
    ILogger<XmppConnectionService> logger) : BackgroundService, IXmppMessageSender
{
    private readonly XmppOptions _options = options.Value;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Session? _session;

    public bool IsReady => Volatile.Read(ref _session) is not null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            XmppClient client = CreateClient();
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool connected = false;
            bool secure = false;
            client.SaslHandler = new TlsAuthentication(() => secure, _options.RequireTls);
            using IDisposable stateSubscription = client.StateChanged.Subscribe(state =>
            {
                if (state == SessionState.Secure)
                {
                    secure = true;
                }
            });
            using IDisposable connectionSubscription = client.Transport.StateChanged.Subscribe(state =>
            {
                if (state == State.Connected)
                {
                    connected = true;
                }
                else if (connected && state == State.Disconnected)
                {
                    SetOffline(client);
                    disconnected.TrySetResult();
                    sessionLifetime.Cancel();
                }
            });
            using IDisposable incomingSubscription = client.XmppXElementReceived.Subscribe(element =>
            {
                if (element.Name.LocalName == "iq" && Type(element) == "get"
                    && element.Elements().Any(child => child.Name == XName.Get("ping", "urn:xmpp:ping")))
                {
                    _ = RespondToPingAsync(client, element, sessionLifetime.Token);
                }

                Session? session = Volatile.Read(ref _session);
                if (session?.Client == client && element.Name.LocalName == "presence"
                    && From(element) == session.Occupant && Type(element) is "unavailable" or "error")
                {
                    SetOffline(client);
                    disconnected.TrySetResult();
                    sessionLifetime.Cancel();
                }
            }, _ =>
            {
                SetOffline(client);
                disconnected.TrySetResult();
                sessionLifetime.Cancel();
            });

            try
            {
                logger.LogInformation("Подключение к XMPP {Host}:{Port}", _options.Host, _options.Port);
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(sessionLifetime.Token);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));
                await client.ConnectAsync(connectTimeout.Token).WaitAsync(connectTimeout.Token);
                await client.Transport.SendAsync(new Presence(), connectTimeout.Token);
                string occupant = $"{_options.RoomJid}/{_options.Nickname}";
                var presence = new Presence { To = new Jid(occupant) };
                XNamespace muc = "http://jabber.org/protocol/muc";
                var join = new XElement(muc + "x", new XElement(muc + "history", new XAttribute("maxstanzas", "0")));
                if (!string.IsNullOrEmpty(_options.RoomPassword))
                {
                    join.Add(new XElement(muc + "password", _options.RoomPassword));
                }

                presence.Add(join);
                XmppXElement response = await ExchangeAsync(client, presence, element =>
                    element.Name.LocalName == "presence" && From(element) == occupant
                    && (Type(element) == "error" || element.Descendants()
                        .Any(child => child.Name == XName.Get("status", "http://jabber.org/protocol/muc#user")
                            && (string?)child.Attribute("code") == "110")), connectTimeout.Token);
                if (Type(response) == "error")
                {
                    throw new XmppSendException(ErrorCode(response));
                }

                sessionLifetime.Token.ThrowIfCancellationRequested();
                var current = new Session(client, occupant, sessionLifetime.Token);
                Volatile.Write(ref _session, current);
                metrics.XmppReady.Set(1);
                logger.LogInformation("Бот вошёл в XMPP-конференцию");
                failures = 0;
                while (!sessionLifetime.IsCancellationRequested)
                {
                    Task delay = Task.Delay(TimeSpan.FromSeconds(_options.PingIntervalSeconds), sessionLifetime.Token);
                    await Task.WhenAny(delay, disconnected.Task);
                    sessionLifetime.Token.ThrowIfCancellationRequested();
                    await PingAsync(current, sessionLifetime.Token);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                string error = exception is XmppSendException sendError ? sendError.ErrorCode : exception.GetType().Name;
                logger.LogWarning("XMPP недоступен: {ErrorCode}", error);
            }
            finally
            {
                Session? endingSession = Volatile.Read(ref _session);
                SetOffline(client);
                await sessionLifetime.CancelAsync();
                if (stoppingToken.IsCancellationRequested && endingSession?.Client == client)
                {
                    using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        await client.Transport.SendAsync(new Presence
                        {
                            To = new Jid(endingSession.Occupant),
                            Type = PresenceType.Unavailable
                        }, closeTimeout.Token);
                        await client.Transport.SendAsync(client.Transport.GetStreamFooter(), closeTimeout.Token);
                    }
                    catch (Exception exception)
                    {
                        logger.LogDebug("Выход из конференции: {ErrorType}", exception.GetType().Name);
                    }
                }

                try
                {
                    await client.Transport.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (Exception exception)
                {
                    logger.LogDebug("Закрытие XMPP-соединения: {ErrorType}", exception.GetType().Name);
                }
            }

            double backoff = Math.Min(300, 5 * Math.Pow(2, Math.Min(failures++, 6)));
            await Task.Delay(TimeSpan.FromSeconds(backoff * (0.9 + (Random.Shared.NextDouble() * 0.1))), stoppingToken);
        }
    }

    public async Task SendToRoomAsync(string roomJid, string text, CancellationToken cancellationToken)
    {
        Session session = Volatile.Read(ref _session) ?? throw new XmppSendException("not-connected");
        if (!string.Equals(roomJid, _options.RoomJid, StringComparison.Ordinal))
        {
            throw new XmppSendException("queued-room-differs-from-configuration");
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token);
        await _sendLock.WaitAsync(lifetime.Token);
        try
        {
            string id = Guid.NewGuid().ToString("N");
            var message = new Message { Id = id, To = new Jid(roomJid), Type = MessageType.GroupChat, Body = text };
            message.Add(new XElement(XName.Get("origin-id", "urn:xmpp:sid:0"), new XAttribute("id", id)));
            XmppXElement echo = await ExchangeAsync(session.Client, message, element =>
                element.Name.LocalName == "message" &&
                (Type(element) == "error" && (string?)element.Attribute("id") == id && Bare(From(element)) == roomJid
                    || Type(element) == "groupchat" && From(element) == session.Occupant
                    && !element.Elements().Any(child => child.Name.NamespaceName is "urn:xmpp:delay" or "jabber:x:delay")
                    && ((string?)element.Attribute("id") == id || element.Elements()
                        .Any(child => child.Name == XName.Get("origin-id", "urn:xmpp:sid:0")
                            && (string?)child.Attribute("id") == id))), lifetime.Token);
            if (Type(echo) == "error")
            {
                string error = ErrorCode(echo);
                throw new XmppSendException(error, error is "bad-request" or "not-acceptable" or "policy-violation");
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private XmppClient CreateClient()
    {
        var resolver = new StaticNameResolver(new UriBuilder("tcp", _options.Host, _options.Port).Uri);
        ICertificateValidator validator = _options.ValidateCertificate
            ? new DefaultCertificateValidator() : new AlwaysAcceptCertificateValidator();
        return new XmppClient(configuration => configuration.UseSocketTransport(resolver, validator))
        {
            Jid = new Jid(_options.Jid),
            Password = _options.Password,
            Tls = _options.RequireTls,
            Timeout = _options.ConnectTimeoutSeconds * 1000
        };
    }

    private async Task PingAsync(Session session, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));
        await _sendLock.WaitAsync(timeout.Token);
        try
        {
            string id = Guid.NewGuid().ToString("N");
            var ping = new Iq { Id = id, To = new Jid(session.Occupant), Type = IqType.Get };
            ping.Add(new XElement(XName.Get("ping", "urn:xmpp:ping")));
            XmppXElement response = await ExchangeAsync(session.Client, ping, element =>
                element.Name.LocalName == "iq" && (string?)element.Attribute("id") == id
                && Type(element) is "result" or "error", timeout.Token);
            if (Type(response) == "error" && ErrorCode(response) is not ("service-unavailable" or "feature-not-implemented"))
            {
                throw new XmppSendException(ErrorCode(response));
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<XmppXElement> ExchangeAsync(XmppClient client, XmppXElement stanza,
        Func<XmppXElement, bool> predicate, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<XmppXElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = client.XmppXElementReceived.Subscribe(element =>
        {
            if (predicate(element))
            {
                completion.TrySetResult(element);
            }
        }, _ => completion.TrySetCanceled(cancellationToken));
        await client.Transport.SendAsync(stanza, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    private async Task RespondToPingAsync(XmppClient client, XmppXElement request, CancellationToken cancellationToken)
    {
        string? sender = From(request);
        string? id = (string?)request.Attribute("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            // Do not take the request lock: the server may route our own self-ping back to the bot.
            var response = new Iq { Id = id, Type = IqType.Result };
            if (!string.IsNullOrWhiteSpace(sender))
            {
                response.To = new Jid(sender);
            }

            await client.Transport.SendAsync(response, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogDebug("Не удалось ответить на XMPP ping: {ErrorType}", exception.GetType().Name);
        }
    }

    private void SetOffline(XmppClient client)
    {
        Session? current = Volatile.Read(ref _session);
        if (current?.Client == client)
        {
            Interlocked.CompareExchange(ref _session, null, current);
        }

        metrics.XmppReady.Set(0);
    }

    private static string? From(XmppXElement element) => (string?)element.Attribute("from");
    private static string? Type(XmppXElement element) => (string?)element.Attribute("type");
    private static string? Bare(string? jid) => jid?.Split('/')[0];

    private static string ErrorCode(XmppXElement element)
    {
        return element.Descendants().FirstOrDefault(child =>
            child.Name.NamespaceName == "urn:ietf:params:xml:ns:xmpp-stanzas" && child.Name.LocalName != "text")
            ?.Name.LocalName ?? "xmpp-error";
    }

    public override void Dispose()
    {
        base.Dispose();
        _sendLock.Dispose();
    }

    private sealed record Session(XmppClient Client, string Occupant, CancellationToken Token);

    private sealed class TlsAuthentication(Func<bool> isSecure, bool requireTls) : IAuthenticate
    {
        public Task<XmppXElement> AuthenticateAsync(Mechanisms mechanisms, IXmppClient xmppClient, CancellationToken cancellationToken)
        {
            if (requireTls && !isSecure())
            {
                throw new XmppSendException("tls-required");
            }

            return new DefaultSaslHandler().AuthenticateAsync(mechanisms, xmppClient, cancellationToken);
        }
    }
}
