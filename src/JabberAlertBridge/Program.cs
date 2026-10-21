using System.Xml;
using JabberAlertBridge.Api.Authentication;
using JabberAlertBridge.Api.Endpoints;
using JabberAlertBridge.BackgroundServices;
using JabberAlertBridge.Configuration;
using JabberAlertBridge.HealthChecks;
using JabberAlertBridge.Infrastructure.Persistence;
using JabberAlertBridge.Infrastructure.Xmpp;
using JabberAlertBridge.Observability;
using JabberAlertBridge.Services;
using JabberAlertBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prometheus;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSecretFiles();
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddOptions<XmppOptions>().BindConfiguration(XmppOptions.SectionName).ValidateDataAnnotations()
    .Validate(options => IsBareJid(options.Jid) && IsBareJid(options.RoomJid), "Требуется JID вида user@domain без ресурса.")
    .Validate(options => IsXmlText(options.Nickname) && !options.Nickname.Contains('/'), "Некорректный ник конференции.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Host) && Uri.CheckHostName(options.Host) != UriHostNameType.Unknown,
        "Требуется корректный XMPP Host.")
    .Validate(options => options.Password != "CHANGE_ME", "Задайте Xmpp:Password.").ValidateOnStart();
builder.Services.AddOptions<WebhookAuthOptions>().BindConfiguration(WebhookAuthOptions.SectionName).ValidateDataAnnotations()
    .Validate(options => !options.Username.Contains(':') && options.Password != "CHANGE_ME", "Задайте HTTP-учётные данные.")
    .ValidateOnStart();
builder.Services.AddOptions<DeliveryOptions>().BindConfiguration(DeliveryOptions.SectionName).ValidateDataAnnotations()
    .Validate(options => options.LeaseDurationSeconds > options.SendTimeoutSeconds + 5, "Lease должен превышать timeout отправки минимум на 5 секунд.")
    .Validate(options => options.RetryMaxSeconds >= options.RetryInitialSeconds, "Максимальный retry меньше начального.").ValidateOnStart();
builder.Services.AddOptions<AlertFormattingOptions>().BindConfiguration(AlertFormattingOptions.SectionName).ValidateDataAnnotations()
    .Validate(options => TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZoneId, out _), "Неизвестный часовой пояс.").ValidateOnStart();

string connectionString = builder.Configuration.GetConnectionString("Postgres") ?? "";
try
{
    var connection = new NpgsqlConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
        || string.IsNullOrWhiteSpace(connection.Username) || connection.Password == "CHANGE_ME")
    {
        throw new ArgumentException();
    }
}
catch (ArgumentException)
{
    throw new InvalidOperationException("Задайте корректную ConnectionStrings:Postgres, без пароля CHANGE_ME.");
}

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    try
    {
        var migrationOptions = new DbContextOptionsBuilder<AlertDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new AlertDbContext(migrationOptions);
        await db.Database.MigrateAsync();
        Console.WriteLine("Миграции PostgreSQL применены.");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Не удалось применить миграции: {exception.GetType().Name}");
        Environment.ExitCode = 1;
    }

    return;
}

builder.Services.AddDbContextFactory<AlertDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<IOutgoingMessageQueue, PostgresMessageQueue>();
builder.Services.AddSingleton<IAlertMessageFormatter, AlertMessageFormatter>();
builder.Services.AddScoped<AlertWebhookMapper>();
builder.Services.AddScoped<AlertProcessor>();
builder.Services.AddSingleton<BridgeMetrics>();
builder.Services.AddSingleton<XmppConnectionService>();
builder.Services.AddSingleton<IXmppMessageSender>(provider => provider.GetRequiredService<XmppConnectionService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<XmppConnectionService>());
builder.Services.AddHostedService<AlertDeliveryWorker>();
builder.Services.AddHostedService<QueueMaintenanceWorker>();
builder.Services.AddAuthentication(BasicAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(BasicAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>("postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(5))
    .AddCheck<XmppHealthCheck>("xmpp", tags: ["xmpp"]);

int maxBody = builder.Configuration.GetValue<int?>("AlertFormatting:MaxRequestBodyBytes") ?? 262144;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxBody);

WebApplication app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = 500;
    await Results.Problem(statusCode: 500, title: "Внутренняя ошибка сервиса.",
        extensions: new Dictionary<string, object?> { ["requestId"] = context.TraceIdentifier }).ExecuteAsync(context);
}));
app.UseHttpMetrics();
app.Use(AlertWebhookEndpoints.ObserveRequestAsync);
app.UseAuthentication();
app.UseAuthorization();
app.MapAlertWebhooks();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") });
app.MapHealthChecks("/health/xmpp", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("xmpp") });
app.MapMetrics();
await app.RunAsync();

static bool IsBareJid(string value)
{
    int at = value.IndexOf('@');
    return value.Length <= 512 && at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1
        && !value.Contains('/') && !value.Any(char.IsWhiteSpace) && IsXmlText(value);
}

static bool IsXmlText(string value)
{
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
