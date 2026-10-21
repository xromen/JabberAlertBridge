using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Xml.Linq;
using JabberAlertBridge.Configuration;
using JabberAlertBridge.Services.Interfaces;
using XmppDotNet;
using XmppDotNet.Sasl;
using XmppDotNet.Transport;
using XmppDotNet.Transport.Socket;
using XmppDotNet.Xml;
using XmppDotNet.Xmpp;
using XmppDotNet.Xmpp.Client;
using XmppDotNet.Xmpp.Sasl;

namespace JabberAlertBridge.Infrastructure.Xmpp;

internal sealed class XmppSession : IDisposable
{
    private readonly XmppOptions _options;
    private readonly ILogger _logger;
    private readonly Action _onDisconnected;
    private readonly XmppClient _client;
    private readonly CancellationTokenSource _lifetime;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly XmppServerAcknowledgements _acknowledgements;

    private bool _connected;
    private bool _secure;
    private bool _authenticated;

    public XmppSession(
        XmppOptions options,
        ILogger logger,
        Action onDisconnected,
        CancellationToken stoppingToken)
    {
        _options = options;
        _logger = logger;
        _onDisconnected = onDisconnected;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _client = CreateClient();
        _acknowledgements = new XmppServerAcknowledgements(_client, _lifetime.Token, logger);

        _client.SaslHandler = new TlsAuthentication(() => _secure, options.RequireTls);

        _subscriptions.Add(_client.StateChanged.Subscribe(HandleSessionState));
        _subscriptions.Add(_client.Transport.StateChanged.Subscribe(HandleTransportState));
        _subscriptions.Add(_client.XmppXElementReceived.Subscribe(HandleIncoming, _ => Abort()));
    }

    public CancellationToken Token => _lifetime.Token;

    public bool IsReady => _authenticated && !_lifetime.IsCancellationRequested;

    public async Task ConnectAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

        await _client.ConnectAsync(timeout.Token).WaitAsync(timeout.Token);
        await _acknowledgements.EnableAsync(_options.RequireServerAcknowledgement, timeout.Token);
        await _client.Transport.SendAsync(new Presence(), timeout.Token);

        timeout.Token.ThrowIfCancellationRequested();
        _authenticated = true;
    }

    public async Task SendAsync(
        Guid messageId,
        string recipientJid,
        string text,
        CancellationToken cancellationToken)
    {
        var message = new Message
        {
            Id = messageId.ToString("N"),
            To = new Jid(recipientJid),
            Type = MessageType.Chat,
            Body = text
        };

        await _acknowledgements.SendAsync(message, cancellationToken);
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N");

        var ping = new Iq
        {
            Id = id,
            To = new Jid(_client.Jid.Domain),
            Type = IqType.Get
        };

        ping.Add(new XElement(XName.Get("ping", "urn:xmpp:ping")));

        XmppXElement response = await XmppExchange.SendAsync(
            _client,
            ping,
            element => element.Name.LocalName == "iq"
                && (string?)element.Attribute("id") == id
                && XmppExchange.Type(element) is "result" or "error",
            cancellationToken);

        if (XmppExchange.Type(response) == "error")
        {
            string error = XmppExchange.ErrorCode(response);

            if (error is not ("service-unavailable" or "feature-not-implemented"))
            {
                throw new XmppSendException(error);
            }
        }
    }

    public async Task CloseAsync(bool graceful)
    {
        await _lifetime.CancelAsync();

        if (graceful && _authenticated)
        {
            await CloseStreamAsync();
        }

        try
        {
            await _client.Transport.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception exception)
        {
            _logger.LogDebug("Закрытие XMPP-соединения: {ErrorType}", exception.GetType().Name);
        }
    }

    private XmppClient CreateClient()
    {
        var resolver = new StaticNameResolver(new UriBuilder("tcp", _options.Host, _options.Port).Uri);

        ICertificateValidator validator = _options.ValidateCertificate
            ? new DefaultCertificateValidator()
            : new AlwaysAcceptCertificateValidator();

        return new XmppClient(configuration => configuration.UseSocketTransport(resolver, validator))
        {
            Jid = new Jid(_options.Jid),
            Password = _options.Password,
            Tls = _options.RequireTls,
            Timeout = _options.ConnectTimeoutSeconds * 1000
        };
    }

    private void HandleSessionState(SessionState state)
    {
        if (state == SessionState.Secure)
        {
            _secure = true;
        }
    }

    private void HandleTransportState(State state)
    {
        if (state == State.Connected)
        {
            _connected = true;
        }
        else if (_connected && state == State.Disconnected)
        {
            Abort();
        }
    }

    private void HandleIncoming(XmppXElement element)
    {
        if (element.Name.LocalName == "message" && XmppExchange.Type(element) == "error")
        {
            _logger.LogWarning(
                "XMPP вернул ошибку сообщения {MessageId}: {ErrorCode}",
                (string?)element.Attribute("id"),
                XmppExchange.ErrorCode(element));
        }

        if (element.Name.LocalName == "iq"
            && XmppExchange.Type(element) == "get"
            && element.Elements().Any(child => child.Name == XName.Get("ping", "urn:xmpp:ping")))
        {
            _ = RespondToPingAsync(element);
        }
    }

    private async Task RespondToPingAsync(XmppXElement request)
    {
        string? id = (string?)request.Attribute("id");

        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var response = new Iq { Id = id, Type = IqType.Result };
            string? sender = XmppExchange.From(request);

            if (!string.IsNullOrWhiteSpace(sender))
            {
                response.To = new Jid(sender);
            }

            // Replies must not wait for the lock held by an outgoing request.
            await _client.Transport.SendAsync(response, timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogDebug("Не удалось ответить на XMPP ping: {ErrorType}", exception.GetType().Name);
        }
    }

    private async Task CloseStreamAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        try
        {
            await _client.Transport.SendAsync(new Presence { Type = PresenceType.Unavailable }, timeout.Token);
            await _client.Transport.SendAsync(_client.Transport.GetStreamFooter(), timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogDebug("Закрытие XML-потока: {ErrorType}", exception.GetType().Name);
        }
    }

    public void Abort()
    {
        _onDisconnected();
        _lifetime.Cancel();
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _acknowledgements.Dispose();
        _lifetime.Dispose();
    }

    private sealed class TlsAuthentication(Func<bool> isSecure, bool requireTls) : IAuthenticate
    {
        public Task<XmppXElement> AuthenticateAsync(
            Mechanisms mechanisms,
            IXmppClient xmppClient,
            CancellationToken cancellationToken)
        {
            if (requireTls && !isSecure())
            {
                throw new XmppSendException("tls-required");
            }

            return new DefaultSaslHandler().AuthenticateAsync(mechanisms, xmppClient, cancellationToken);
        }
    }
}
