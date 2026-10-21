using JabberAlertBridge.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JabberAlertBridge.Infrastructure.Persistence;

public sealed class AlertDbContextFactory : IDesignTimeDbContextFactory<AlertDbContext>
{
    public AlertDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        configuration.AddSecretFiles();
        string connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Database=jabber_alert_bridge;Username=jabber_bridge";
        var options = new DbContextOptionsBuilder<AlertDbContext>().UseNpgsql(connectionString).Options;
        return new AlertDbContext(options);
    }
}
