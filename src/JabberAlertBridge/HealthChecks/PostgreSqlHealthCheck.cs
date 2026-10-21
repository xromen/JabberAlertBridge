using JabberAlertBridge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JabberAlertBridge.HealthChecks;

public sealed class PostgreSqlHealthCheck(IDbContextFactory<AlertDbContext> contextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using AlertDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
            _ = await db.OutgoingMessages.Select(message => message.Id).Take(1).ToListAsync(cancellationToken);
            bool writable = await db.Database.SqlQueryRaw<bool>("""
                SELECT current_setting('transaction_read_only') = 'off'
                    AND has_table_privilege(current_user, 'outgoing_messages', 'INSERT') AS "Value"
                """).SingleAsync(cancellationToken);
            return writable ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("База недоступна для записи очереди.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"Хранилище очереди недоступно: {exception.GetType().Name}");
        }
    }
}
