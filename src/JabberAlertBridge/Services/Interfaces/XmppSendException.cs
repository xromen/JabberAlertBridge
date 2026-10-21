namespace JabberAlertBridge.Services.Interfaces;

public sealed class XmppSendException(string errorCode, bool permanent = false) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
    public bool Permanent { get; } = permanent;
}
