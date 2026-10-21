using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Xml.Linq;
using JabberAlertBridge.Services.Interfaces;
using XmppDotNet;
using XmppDotNet.Xml;
using XmppDotNet.Xmpp.Client;
using XmppDotNet.Xmpp.StreamManagement;
using XmppDotNet.Xmpp.StreamManagement.Ack;

namespace JabberAlertBridge.Infrastructure.Xmpp;

internal sealed class XmppServerAcknowledgements : IDisposable
{
    private const string Namespace = "urn:xmpp:sm:3";

    private readonly XmppClient _client;
    private readonly CancellationToken _sessionToken;
    private readonly ILogger _logger;
    private readonly CompositeDisposable _subscriptions = new();

    private long _sentStanzas;
    private long _receivedStanzas;
    private int _enabled;

    public XmppServerAcknowledgements(XmppClient client, CancellationToken sessionToken, ILogger logger)
    {
        _client = client;
        _sessionToken = sessionToken;
        _logger = logger;

        _subscriptions.Add(client.Transport.BeforeXmlSent.Subscribe(CountOutgoingStanza));
        _subscriptions.Add(client.XmppXElementReceived.Subscribe(HandleIncoming, _ => { }));
    }

    public bool IsSupported { get; private set; }

    public bool IsEnabled => Volatile.Read(ref _enabled) == 1;

    public async Task EnableAsync(bool required, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            if (required)
            {
                throw new XmppSendException("stream-management-required");
            }

            _logger.LogWarning("XMPP-сервер не поддерживает подтверждение отправки; используется запись в сокет");

            return;
        }

        XmppXElement response = await XmppExchange.SendAsync(
            _client,
            new Enable { Resume = false },
            element => element.Name.NamespaceName == Namespace && element.Name.LocalName is "enabled" or "failed",
            cancellationToken);

        if (response.Name.LocalName == "failed")
        {
            throw new XmppSendException("stream-management-failed");
        }
    }

    public async Task SendAsync(Message message, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            await _client.Transport.SendAsync(message, cancellationToken);

            return;
        }

        long target = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable outgoing = _client.Transport.BeforeXmlSent.Subscribe(element =>
        {
            if (ReferenceEquals(element, message))
            {
                Volatile.Write(ref target, Volatile.Read(ref _sentStanzas));
            }
        });

        using IDisposable incoming = _client.XmppXElementReceived.Subscribe(element =>
        {
            if (IsMessageError(element, message))
            {
                string error = XmppExchange.ErrorCode(element);
                bool permanent = error is "bad-request" or "jid-malformed" or "not-acceptable" or "policy-violation";

                completion.TrySetException(new XmppSendException(error, permanent));
            }
            else if (Acknowledges(element, Volatile.Read(ref target)))
            {
                completion.TrySetResult();
            }
        }, _ => completion.TrySetCanceled(cancellationToken));

        await _client.Transport.SendAsync(message, cancellationToken);
        await _client.Transport.SendAsync(new Request(), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    private void CountOutgoingStanza(XmppXElement element)
    {
        if (IsEnabled && IsStanza(element))
        {
            Interlocked.Increment(ref _sentStanzas);
        }
    }

    private void HandleIncoming(XmppXElement element)
    {
        if (element.Name == XName.Get("features", "http://etherx.jabber.org/streams"))
        {
            IsSupported = element.Descendants().Any(child => child.Name == XName.Get("sm", Namespace));
        }
        else if (element.Name == XName.Get("enabled", Namespace))
        {
            Interlocked.Exchange(ref _sentStanzas, 0);
            Interlocked.Exchange(ref _receivedStanzas, 0);
            Volatile.Write(ref _enabled, 1);
        }
        else if (IsEnabled && IsStanza(element))
        {
            Interlocked.Increment(ref _receivedStanzas);
        }
        else if (IsEnabled && element.Name == XName.Get("r", Namespace))
        {
            _ = ReplyAsync();
        }
    }

    private bool Acknowledges(XmppXElement element, long target)
    {
        if (target == 0 || element.Name != XName.Get("a", Namespace))
        {
            return false;
        }

        if (!uint.TryParse(
            (string?)element.Attribute("h"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out uint handled))
        {
            return false;
        }

        uint sent = unchecked((uint)Volatile.Read(ref _sentStanzas));

        // XEP-0198 counters wrap at 2^32; compare within the outstanding half-range.
        return unchecked(handled - (uint)target) < 0x80000000u
            && unchecked(sent - handled) < 0x80000000u;
    }

    private bool IsMessageError(XmppXElement element, Message message)
    {
        string? sender = XmppExchange.BareJid(XmppExchange.From(element));

        return element.Name.LocalName == "message"
            && XmppExchange.Type(element) == "error"
            && (string?)element.Attribute("id") == message.Id
            && (sender == message.To.ToString() || sender == _client.Jid.Domain || sender is null);
    }

    private async Task ReplyAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_sessionToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var answer = new Answer
            {
                LastHandledStanza = unchecked((uint)Volatile.Read(ref _receivedStanzas))
            };

            await _client.Transport.SendAsync(answer, timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogDebug("Не удалось подтвердить входящие XMPP stanzas: {ErrorType}", exception.GetType().Name);
        }
    }

    private static bool IsStanza(XmppXElement element)
    {
        return element.Name.NamespaceName == "jabber:client"
            && element.Name.LocalName is "message" or "iq" or "presence";
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
