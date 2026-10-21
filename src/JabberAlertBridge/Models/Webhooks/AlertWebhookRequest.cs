using System.Text.Json;

namespace JabberAlertBridge.Models.Webhooks;

public sealed class AlertWebhookRequest
{
    public string? Receiver { get; init; }
    public string? Status { get; init; }

    public string? GroupKey { get; init; }
    public string? Version { get; init; }

    public JsonElement OrgId { get; init; }
    public string? Title { get; init; }

    public JsonElement TruncatedAlerts { get; init; }

    public List<WebhookAlert?>? Alerts { get; init; }
}

public sealed class WebhookAlert
{
    public string? Status { get; init; }

    public Dictionary<string, string?>? Labels { get; init; }
    public Dictionary<string, string?>? Annotations { get; init; }

    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? EndsAt { get; init; }

    public string? Fingerprint { get; init; }

    public string? GeneratorURL { get; init; }
    public string? DashboardURL { get; init; }
    public string? PanelURL { get; init; }
}
