using JabberAlertBridge.Configuration;
using JabberAlertBridge.Models;
using JabberAlertBridge.Observability;
using JabberAlertBridge.Services.Interfaces;
using Microsoft.Extensions.Options;
using Prometheus;

namespace JabberAlertBridge.BackgroundServices;

public sealed class AlertDeliveryWorker(
    IOutgoingMessageQueue queue,
    IXmppMessageSender sender,
    IOptions<DeliveryOptions> options,
    BridgeMetrics metrics,
    ILogger<AlertDeliveryWorker> logger) : BackgroundService
{
    private readonly DeliveryOptions _options = options.Value;
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!sender.IsReady)
                {
                    await PauseAsync(stoppingToken);
                    continue;
                }

                OutgoingMessage? message = await queue.ClaimNextAsync(_workerId, stoppingToken);
                if (message is null)
                {
                    await PauseAsync(stoppingToken);
                    continue;
                }

                await DeliverAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Ошибка обработчика очереди: {ErrorType}", exception.GetType().Name);
                await PauseAsync(stoppingToken);
            }
        }
    }

    private async Task DeliverAsync(OutgoingMessage message, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.SendTimeoutSeconds));
        try
        {
            using (metrics.SendDuration.NewTimer())
            {
                await sender.SendToRoomAsync(message.RoomJid, message.Content, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Leave the lease intact: a send in flight may already have reached the room.
            throw;
        }
        catch (Exception exception)
        {
            metrics.Failures.Inc();
            double exponential = _options.RetryInitialSeconds * Math.Pow(2, Math.Min(message.Attempts - 1, 20));
            double delay = Math.Min(_options.RetryMaxSeconds, exponential * (0.9 + (Random.Shared.NextDouble() * 0.2)));
            DateTimeOffset next = DateTimeOffset.UtcNow.AddSeconds(delay);
            string error = exception is XmppSendException sendError ? sendError.ErrorCode : exception.GetType().Name;
            bool permanent = exception is XmppSendException { Permanent: true };
            await queue.RetryAsync(message, _workerId, next, error, permanent, stoppingToken);
            logger.LogWarning("Сообщение {MessageId}: попытка {Attempt}, ошибка {ErrorCode}, следующая попытка {NextAttemptAt}, постоянная {Permanent}",
                message.Id, message.Attempts, error, next, permanent);
            return;
        }

        // A failure here must not be treated as a failed XMPP send. The lease will expire after a database outage.
        await queue.CompleteAsync(message, _workerId, stoppingToken);
        metrics.Sent.Inc();
        logger.LogInformation("Сообщение {MessageId} доставлено, попытка {Attempt}", message.Id, message.Attempts);
    }

    private Task PauseAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), cancellationToken);
    }
}
