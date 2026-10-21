using JabberAlertBridge.Configuration;
using JabberAlertBridge.Models;
using JabberAlertBridge.Models.Webhooks;
using JabberAlertBridge.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace JabberAlertBridge.Services;

public sealed class AlertProcessor(
    AlertWebhookMapper mapper,
    IAlertMessageFormatter formatter,
    IOutgoingMessageQueue queue,
    IOptions<XmppOptions> xmpp)
{
    public async Task<EnqueueResult> ProcessAsync(
        AlertWebhookRequest request,
        CancellationToken cancellationToken)
    {
        AlertNotification notification = mapper.Map(request);
        IReadOnlyList<string> texts = formatter.Format(notification);

        IReadOnlyList<Guid> ids = texts.Count == 0
            ? []
            : await queue.EnqueueAsync(
                texts,
                notification.Source,
                xmpp.Value.RecipientJids,
                cancellationToken);

        return new EnqueueResult(notification.Source, notification.Alerts.Count, ids);
    }
}

public sealed record EnqueueResult(
    string Source,
    int AlertCount,
    IReadOnlyList<Guid> MessageIds);
