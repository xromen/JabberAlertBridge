using System.ComponentModel.DataAnnotations;

namespace JabberAlertBridge.Configuration;

public sealed class XmppOptions
{
    public const string SectionName = "Xmpp";

    [Required]
    public string Host { get; init; } = "";

    [Range(1, 65535)]
    public int Port { get; init; } = 5222;

    [Required]
    public string Jid { get; init; } = "";

    [Required]
    public string Password { get; init; } = "";

    public string[] RecipientJids { get; set; } = [];

    public bool RequireTls { get; init; } = true;

    public bool ValidateCertificate { get; init; }

    public bool RequireServerAcknowledgement { get; init; } = true;

    [Range(1, 120)]
    public int ConnectTimeoutSeconds { get; init; } = 15;

    [Range(10, 300)]
    public int PingIntervalSeconds { get; init; } = 30;
}

public sealed class WebhookAuthOptions
{
    public const string SectionName = "WebhookAuth";

    [Required]
    public string Username { get; init; } = "monitoring";

    [Required]
    public string Password { get; init; } = "";
}

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    [Range(1, 60)]
    public int PollIntervalSeconds { get; init; } = 2;

    [Range(1, 120)]
    public int SendTimeoutSeconds { get; init; } = 20;

    [Range(5, 600)]
    public int LeaseDurationSeconds { get; init; } = 60;

    [Range(1, 3600)]
    public int RetryInitialSeconds { get; init; } = 5;

    [Range(1, 3600)]
    public int RetryMaxSeconds { get; init; } = 300;

    [Range(1, 1_000_000)]
    public int MaxPendingMessages { get; init; } = 5000;

    [Range(0, int.MaxValue)]
    public int MaxAttempts { get; init; }

    [Range(1, 3650)]
    public int MessageRetentionDays { get; init; } = 14;

    [Range(1, 3650)]
    public int DeadRetentionDays { get; init; } = 90;
}

public sealed class AlertFormattingOptions
{
    public const string SectionName = "AlertFormatting";

    public string TimeZoneId { get; init; } = "Asia/Vladivostok";

    [Range(128, 16000)]
    public int MaxMessageLength { get; init; } = 4000;

    [Range(1, 1000)]
    public int MaxAlertsPerRequest { get; init; } = 100;

    [Range(1024, 10_485_760)]
    public int MaxRequestBodyBytes { get; init; } = 262144;

    public bool IncludeLinks { get; init; } = true;

    public bool SendResolved { get; init; } = true;
}
