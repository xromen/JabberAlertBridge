namespace JabberAlertBridge.Services.Interfaces;

public interface IXmppMessageSender
{
    bool IsReady { get; }
    Task SendToRoomAsync(string roomJid, string text, CancellationToken cancellationToken);
}
