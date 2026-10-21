using JabberAlertBridge.Configuration;
using JabberAlertBridge.Infrastructure.Persistence;
using JabberAlertBridge.Models;
using JabberAlertBridge.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JabberAlertBridge.BackgroundServices;

public sealed class QueueMaintenanceWorker(
    IDbContextFactory<AlertDbContext> contextFactory,
    IOptions<DeliveryOptions> options,
    BridgeMetrics metrics,
    ILogger<QueueMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        metrics.QueueCapacity.Set(options.Value.MaxPendingMessages);
        DateTimeOffset nextCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AlertDbContext db = await contextFactory.CreateDbContextAsync(stoppingToken);
                var active = db.OutgoingMessages.Where(message => message.Status == MessageStatus.Pending
                    || message.Status == MessageStatus.Processing);
                int pending = await active.CountAsync(stoppingToken);
                DateTimeOffset? oldest = await active.MinAsync(message => (DateTimeOffset?)message.CreatedAt, stoppingToken);
                metrics.Pending.Set(pending);
                metrics.OldestAge.Set(oldest is null ? 0 : Math.Max(0, (DateTimeOffset.UtcNow - oldest.Value).TotalSeconds));
                metrics.Dead.Set(await db.OutgoingMessages.CountAsync(message => message.Status == MessageStatus.Dead, stoppingToken));
                metrics.DatabaseReady.Set(1);

                if (DateTimeOffset.UtcNow >= nextCleanup)
                {
                    DateTimeOffset sentCutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.MessageRetentionDays);
                    DateTimeOffset deadCutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.DeadRetentionDays);
                    int removed = await db.OutgoingMessages.Where(message =>
                        message.Status == MessageStatus.Sent && message.SentAt < sentCutoff
                        || message.Status == MessageStatus.Dead && message.DeadAt < deadCutoff)
                        .ExecuteDeleteAsync(stoppingToken);
                    nextCleanup = DateTimeOffset.UtcNow.AddHours(1);
                    logger.LogInformation("Удалено записей истории: {Count}", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                metrics.DatabaseReady.Set(0);
                logger.LogWarning("Не удалось обновить состояние очереди: {ErrorType}", exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
