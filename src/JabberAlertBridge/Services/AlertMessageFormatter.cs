using System.Globalization;
using System.Text;
using System.Xml;
using JabberAlertBridge.Configuration;
using JabberAlertBridge.Models;
using JabberAlertBridge.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace JabberAlertBridge.Services;

public sealed class AlertMessageFormatter(IOptions<AlertFormattingOptions> options) : IAlertMessageFormatter
{
    private readonly AlertFormattingOptions _options = options.Value;
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

    public IReadOnlyList<string> Format(AlertNotification notification)
    {
        if (notification.Alerts.Count == 0)
        {
            return [];
        }

        var blocks = new List<string>();
        if (notification.Alerts.Count > 1)
        {
            blocks.Add($"Уведомление {notification.Source}: {notification.Alerts.Count} алертов");
        }

        if (notification.TruncatedAlerts > 0)
        {
            blocks.Add($"⚠ Источник не включил в уведомление ещё {notification.TruncatedAlerts} алертов.");
        }

        foreach (AlertItem alert in notification.Alerts.OrderBy(item => item.Status == "resolved"))
        {
            blocks.Add(FormatAlert(alert));
        }

        string text = string.Join("\n\n", blocks);
        if (text.Length <= _options.MaxMessageLength)
        {
            return [text];
        }

        // Reserve room for part numbers before splitting, including very large requests.
        int contentLimit = _options.MaxMessageLength - 32;
        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (string block in blocks)
        {
            if (current.Length > 0 && current.Length + 2 + block.Length > contentLimit)
            {
                parts.Add(current.ToString());
                current.Clear();
            }

            if (block.Length <= contentLimit)
            {
                if (current.Length > 0)
                {
                    current.Append("\n\n");
                }

                current.Append(block);
                continue;
            }

            string remainder = block;
            while (remainder.Length > contentLimit)
            {
                int length = remainder.LastIndexOf('\n', contentLimit - 1, contentLimit);
                if (length < contentLimit / 2)
                {
                    length = contentLimit;
                }

                if (char.IsHighSurrogate(remainder[length - 1]))
                {
                    length--;
                }

                parts.Add(remainder[..length]);
                remainder = remainder[length..].TrimStart('\n');
            }

            current.Append(remainder);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts.Select((part, index) => $"({index + 1}/{parts.Count})\n{part}").ToArray();
    }

    private string FormatAlert(AlertItem alert)
    {
        string status = alert.Status == "firing" ? "🔴 ПРОБЛЕМА" : "🟢 ВОССТАНОВЛЕНО";
        string severity = alert.Severity?.ToLowerInvariant() switch
        {
            "critical" => "КРИТИЧЕСКИЙ",
            "warning" => "ПРЕДУПРЕЖДЕНИЕ",
            "info" or "information" => "ИНФОРМАЦИЯ",
            null or "" => "НЕ УКАЗАН",
            _ => alert.Severity
        };

        var text = new StringBuilder();
        text.AppendLine($"{status} — {alert.Name}");
        text.AppendLine($"Приоритет: {severity}");
        AddLine(text, "Сервер", alert.Instance ?? "не указан");
        AddLine(text, "Сервис", alert.Service);
        AddLine(text, "Окружение", alert.Environment);
        AddLine(text, "Проблема", alert.Summary);
        if (!string.IsNullOrWhiteSpace(alert.Description) && alert.Description != alert.Summary)
        {
            text.AppendLine(alert.Description);
        }

        text.AppendLine($"Начало: {FormatTime(alert.StartsAt)}");
        if (alert.Status == "resolved")
        {
            text.AppendLine($"Окончание: {FormatTime(alert.EndsAt)}");
            if (alert.StartsAt is { } start && alert.EndsAt is { } end && end >= start)
            {
                text.AppendLine($"Длительность: {(end - start).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture)}");
            }
        }

        if (_options.IncludeLinks && alert.Link is not null)
        {
            text.AppendLine(alert.Link.Length <= 1024 ? alert.Link : "Ссылка пропущена: длина превышает 1024 символа.");
        }

        return CleanXmlText(text.ToString().TrimEnd());
    }

    private string FormatTime(DateTimeOffset? value)
    {
        return value is null ? "не указано" :
            $"{TimeZoneInfo.ConvertTime(value.Value, _timeZone).ToString("dd.MM.yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture)} ({_timeZone.Id})";
    }

    private static void AddLine(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.AppendLine($"{label}: {value}");
        }
    }

    private static string CleanXmlText(string text)
    {
        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char value = text[i];
            if (XmlConvert.IsXmlChar(value))
            {
                result.Append(value);
            }
            else if (char.IsHighSurrogate(value) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                result.Append(value).Append(text[++i]);
            }
        }

        return result.ToString();
    }
}
