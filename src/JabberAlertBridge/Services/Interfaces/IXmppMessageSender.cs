namespace JabberAlertBridge.Services.Interfaces;

public interface IXmppMessageSender
{
    bool IsReady { get; }

    Task SendAsync(Guid messageId, string recipientJid, string text, CancellationToken cancellationToken);
}
