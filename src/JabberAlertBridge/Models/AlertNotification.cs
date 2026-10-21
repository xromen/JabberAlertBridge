namespace JabberAlertBridge.Models;

public sealed record AlertNotification(
    string Source,
    string? GroupKey,
    IReadOnlyList<AlertItem> Alerts,
    int TruncatedAlerts);

public sealed record AlertItem(
    string Status,
    string Name,
    string? Severity,
    string? Instance,
    string? Service,
    string? Environment,
    string? Summary,
    string? Description,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Fingerprint,
    string? Link);
