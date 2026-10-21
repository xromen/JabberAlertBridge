using System.Xml;
using JabberAlertBridge.Api.Authentication;
using JabberAlertBridge.BackgroundServices;
using JabberAlertBridge.HealthChecks;
using JabberAlertBridge.Infrastructure.Persistence;
using JabberAlertBridge.Infrastructure.Xmpp;
using JabberAlertBridge.Observability;
using JabberAlertBridge.Services;
using JabberAlertBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace JabberAlertBridge.Configuration;

public static class ServiceRegistration
{
    public static void AddBridgeOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<XmppOptions>()
            .BindConfiguration(XmppOptions.SectionName)
            .Configure(options => ConfigureRecipients(options, configuration))
            .ValidateDataAnnotations()
            .Validate(options => IsBareJid(options.Jid), "Требуется JID аккаунта вида user@domain без ресурса.")
            .Validate(
                options => options.RecipientJids.Length > 0 && options.RecipientJids.All(IsBareJid),
                "Задайте непустой список Xmpp:RecipientJids с JID пользователей вида user@domain.")
            .Validate(
                options => Uri.CheckHostName(options.Host) != UriHostNameType.Unknown,
                "Требуется корректный XMPP Host.")
            .Validate(options => options.Password != "CHANGE_ME", "Задайте Xmpp:Password.")
            .ValidateOnStart();

        services.AddOptions<WebhookAuthOptions>()
            .BindConfiguration(WebhookAuthOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => !options.Username.Contains(':') && options.Password != "CHANGE_ME",
                "Задайте HTTP-учётные данные.")
            .ValidateOnStart();

        services.AddOptions<DeliveryOptions>()
            .BindConfiguration(DeliveryOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.LeaseDurationSeconds > options.SendTimeoutSeconds + 5,
                "Lease должен превышать timeout отправки минимум на 5 секунд.")
            .Validate(
                options => options.RetryMaxSeconds >= options.RetryInitialSeconds,
                "Максимальный retry меньше начального.")
            .ValidateOnStart();

        services.AddOptions<AlertFormattingOptions>()
            .BindConfiguration(AlertFormattingOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZoneId, out _),
                "Неизвестный часовой пояс.")
            .ValidateOnStart();
    }

    public static void AddBridgeServices(this IServiceCollection services, string connectionString)
    {
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        services.AddDbContextFactory<AlertDbContext>(options => options.UseNpgsql(connectionString));

        services.AddSingleton<IOutgoingMessageQueue, PostgresMessageQueue>();
        services.AddSingleton<IAlertMessageFormatter, AlertMessageFormatter>();
        services.AddScoped<AlertWebhookMapper>();
        services.AddScoped<AlertProcessor>();

        services.AddSingleton<BridgeMetrics>();
        services.AddSingleton<XmppConnectionService>();
        services.AddSingleton<IXmppMessageSender>(provider => provider.GetRequiredService<XmppConnectionService>());

        services.AddHostedService(provider => provider.GetRequiredService<XmppConnectionService>());
        services.AddHostedService<AlertDeliveryWorker>();
        services.AddHostedService<QueueMaintenanceWorker>();

        services.AddAuthentication(BasicAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
                BasicAuthenticationHandler.SchemeName,
                _ => { });

        services.AddAuthorization();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();

        services.AddHealthChecks()
            .AddCheck<PostgreSqlHealthCheck>("postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(5))
            .AddCheck<XmppHealthCheck>("xmpp", tags: ["xmpp"]);
    }

    private static void ConfigureRecipients(XmppOptions options, IConfiguration configuration)
    {
        string? recipientList = configuration["Xmpp:RecipientJids"];

        // An empty scalar must not overwrite recipients supplied through indexed configuration keys.
        if (!string.IsNullOrWhiteSpace(recipientList))
        {
            options.RecipientJids = recipientList.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        options.RecipientJids = (options.RecipientJids ?? [])
            .Select(value => value?.Trim() ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsBareJid(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Contains('/'))
        {
            return false;
        }

        int at = value.IndexOf('@');

        if (at <= 0 || at != value.LastIndexOf('@') || at >= value.Length - 1 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (Uri.CheckHostName(value[(at + 1)..]) == UriHostNameType.Unknown)
        {
            return false;
        }

        try
        {
            XmlConvert.VerifyXmlChars(value);

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
