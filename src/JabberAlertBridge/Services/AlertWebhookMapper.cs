using System.Globalization;
using System.Text.Json;
using JabberAlertBridge.Configuration;
using JabberAlertBridge.Models;
using JabberAlertBridge.Models.Webhooks;
using Microsoft.Extensions.Options;

namespace JabberAlertBridge.Services;

public sealed class AlertWebhookMapper(IOptions<AlertFormattingOptions> options)
{
    private readonly AlertFormattingOptions _options = options.Value;

    public AlertNotification Map(AlertWebhookRequest request)
    {
        IReadOnlyList<WebhookAlert?> alerts = ValidateRequest(request);

        var items = new List<AlertItem>();
        bool grafana = request.OrgId.ValueKind != JsonValueKind.Undefined || request.Title is not null;

        foreach (WebhookAlert? alert in alerts)
        {
            if (alert is null)
            {
                throw new WebhookValidationException("Каждый алерт должен быть JSON-объектом.");
            }

            AlertItem item = MapAlert(alert);
            grafana |= alert.DashboardURL is not null || alert.PanelURL is not null;

            if (_options.SendResolved || item.Status != "resolved")
            {
                items.Add(item);
            }
        }

        string source = grafana ? "grafana" : request.Version is not null ? "alertmanager" : "webhook";
        int truncated = ReadTruncatedCount(request.TruncatedAlerts);

        return new AlertNotification(source, request.GroupKey, items, truncated);
    }

    private IReadOnlyList<WebhookAlert?> ValidateRequest(AlertWebhookRequest request)
    {
        if (request.Alerts is not { Count: > 0 } || request.Alerts.Count > _options.MaxAlertsPerRequest)
        {
            throw new WebhookValidationException($"Требуется от 1 до {_options.MaxAlertsPerRequest} алертов.");
        }

        if (request.Status is not null && request.Status is not ("firing" or "resolved"))
        {
            throw new WebhookValidationException("Неизвестный общий статус алертов.");
        }

        return request.Alerts;
    }

    private static AlertItem MapAlert(WebhookAlert alert)
    {
        if (alert.Status is not ("firing" or "resolved"))
        {
            throw new WebhookValidationException("Каждый алерт должен иметь статус firing или resolved.");
        }

        string? name = Get(alert.Labels, "alertname");

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new WebhookValidationException("У каждого алерта требуется labels.alertname.");
        }

        return new AlertItem(
            Status: alert.Status,
            Name: name,
            Severity: Get(alert.Labels, "severity"),
            Instance: Get(alert.Labels, "instance"),
            Service: Get(alert.Labels, "service") ?? Get(alert.Labels, "job"),
            Environment: Get(alert.Labels, "environment") ?? Get(alert.Labels, "env"),
            Summary: Get(alert.Annotations, "summary"),
            Description: Get(alert.Annotations, "description"),
            StartsAt: ValidTime(alert.StartsAt),
            EndsAt: ValidTime(alert.EndsAt),
            Fingerprint: alert.Fingerprint,
            Link: SelectLink(alert));
    }

    private static string? SelectLink(WebhookAlert alert)
    {
        string?[] links = [alert.PanelURL, alert.DashboardURL, alert.GeneratorURL];

        return links.FirstOrDefault(value =>
            Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "https" or "http");
    }

    private static int ReadTruncatedCount(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int count) && count >= 0)
        {
            return count;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), CultureInfo.InvariantCulture, out count)
            && count >= 0)
        {
            return count;
        }

        throw new WebhookValidationException("Некорректное значение truncatedAlerts.");
    }

    private static string? Get(Dictionary<string, string?>? values, string key)
    {
        return values?.GetValueOrDefault(key);
    }

    private static DateTimeOffset? ValidTime(DateTimeOffset? value)
    {
        return value is null || value.Value.Year <= 1 ? null : value;
    }
}

public sealed class WebhookValidationException(string message) : Exception(message);
