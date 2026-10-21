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
        if (request.Alerts is not { Count: > 0 } || request.Alerts.Count > _options.MaxAlertsPerRequest)
        {
            throw new WebhookValidationException($"Требуется от 1 до {_options.MaxAlertsPerRequest} алертов.");
        }

        if (request.Status is not null && request.Status is not ("firing" or "resolved"))
        {
            throw new WebhookValidationException("Неизвестный общий статус алертов.");
        }

        var items = new List<AlertItem>();
        bool grafana = request.OrgId.ValueKind != JsonValueKind.Undefined || request.Title is not null;
        foreach (WebhookAlert? alert in request.Alerts)
        {
            if (alert is null || alert.Status is not ("firing" or "resolved"))
            {
                throw new WebhookValidationException("Каждый алерт должен иметь статус firing или resolved.");
            }

            string? name = Get(alert.Labels, "alertname");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new WebhookValidationException("У каждого алерта требуется labels.alertname.");
            }

            grafana |= alert.DashboardURL is not null || alert.PanelURL is not null;
            if (!_options.SendResolved && alert.Status == "resolved")
            {
                continue;
            }

            string? link = new[] { alert.PanelURL, alert.DashboardURL, alert.GeneratorURL }
                .FirstOrDefault(value => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                    && uri.Scheme is "https" or "http");

            items.Add(new AlertItem(
                alert.Status, name, Get(alert.Labels, "severity"), Get(alert.Labels, "instance"),
                Get(alert.Labels, "service") ?? Get(alert.Labels, "job"),
                Get(alert.Labels, "environment") ?? Get(alert.Labels, "env"),
                Get(alert.Annotations, "summary"), Get(alert.Annotations, "description"),
                ValidTime(alert.StartsAt), ValidTime(alert.EndsAt), alert.Fingerprint, link));
        }

        int truncated = 0;
        if (request.TruncatedAlerts.ValueKind == JsonValueKind.Number)
        {
            if (!request.TruncatedAlerts.TryGetInt32(out truncated) || truncated < 0)
            {
                throw new WebhookValidationException("Некорректное значение truncatedAlerts.");
            }
        }
        else if (request.TruncatedAlerts.ValueKind == JsonValueKind.String)
        {
            if (!int.TryParse(request.TruncatedAlerts.GetString(), CultureInfo.InvariantCulture, out truncated) || truncated < 0)
            {
                throw new WebhookValidationException("Некорректное значение truncatedAlerts.");
            }
        }
        else if (request.TruncatedAlerts.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            throw new WebhookValidationException("Некорректное значение truncatedAlerts.");
        }

        string source = grafana ? "grafana" : request.Version is not null ? "alertmanager" : "webhook";
        return new AlertNotification(source, request.GroupKey, items, truncated);
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
