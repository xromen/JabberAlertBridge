using JabberAlertBridge.Api.Endpoints;
using JabberAlertBridge.Configuration;
using JabberAlertBridge.Infrastructure.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSecretFiles();

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

string connectionString = DatabaseSetup.GetConnectionString(builder.Configuration);

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await DatabaseSetup.ApplyMigrationsAsync(connectionString);

    return;
}

builder.Services.AddBridgeOptions(builder.Configuration);
builder.Services.AddBridgeServices(connectionString);

int maxBody = builder.Configuration.GetValue<int?>("AlertFormatting:MaxRequestBodyBytes") ?? 262144;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxBody);

WebApplication app = builder.Build();
app.MapBridgeEndpoints();

await app.RunAsync();
