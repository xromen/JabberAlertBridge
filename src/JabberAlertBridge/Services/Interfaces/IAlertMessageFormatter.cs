using JabberAlertBridge.Models;

namespace JabberAlertBridge.Services.Interfaces;

public interface IAlertMessageFormatter
{
    IReadOnlyList<string> Format(AlertNotification notification);
}
