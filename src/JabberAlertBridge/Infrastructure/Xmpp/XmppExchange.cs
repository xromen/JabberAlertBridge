using System.Reactive.Linq;
using XmppDotNet;
using XmppDotNet.Xml;

namespace JabberAlertBridge.Infrastructure.Xmpp;

internal static class XmppExchange
{
    public static async Task<XmppXElement> SendAsync(
        XmppClient client,
        XmppXElement stanza,
        Func<XmppXElement, bool> predicate,
        CancellationToken cancellationToken)
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

    public static string? From(XmppXElement element) => (string?)element.Attribute("from");

    public static string? Type(XmppXElement element) => (string?)element.Attribute("type");

    public static string? BareJid(string? jid) => jid?.Split('/')[0];

    public static string ErrorCode(XmppXElement element)
    {
        return element.Descendants()
            .FirstOrDefault(child =>
                child.Name.NamespaceName == "urn:ietf:params:xml:ns:xmpp-stanzas"
                && child.Name.LocalName != "text")
            ?.Name.LocalName ?? "xmpp-error";
    }
}
