namespace JabberAlertBridge.Models;

public sealed class OutgoingMessage
{
    public Guid Id { get; set; }

    public long Sequence { get; set; }

    public string Source { get; set; } = "";

    public string RecipientJid { get; set; } = "";

    public string Content { get; set; } = "";

    public string Status { get; set; } = MessageStatus.Pending;

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? DeadAt { get; set; }

    public string? LastError { get; set; }
}

public static class MessageStatus
{
    public const string Pending = "pending";

    public const string Processing = "processing";

    public const string Sent = "sent";

    public const string Dead = "dead";
}
