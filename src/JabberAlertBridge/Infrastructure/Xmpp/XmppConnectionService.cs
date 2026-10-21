using JabberAlertBridge.Configuration;
using JabberAlertBridge.Observability;
using JabberAlertBridge.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace JabberAlertBridge.Infrastructure.Xmpp;

public sealed class XmppConnectionService(
    IOptions<XmppOptions> options,
    BridgeMetrics metrics,
    ILogger<XmppConnectionService> logger) : BackgroundService, IXmppMessageSender
{
    private readonly XmppOptions _options = options.Value;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private XmppSession? _session;

    public bool IsReady => Volatile.Read(ref _session)?.IsReady == true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            using var session = new XmppSession(_options, logger, () => metrics.XmppReady.Set(0), stoppingToken);

            try
            {
                logger.LogInformation("Подключение к XMPP {Host}:{Port}", _options.Host, _options.Port);

                await session.ConnectAsync();
                Volatile.Write(ref _session, session);

                metrics.XmppReady.Set(1);
                logger.LogInformation(
                    "XMPP подключён, настроено получателей: {RecipientCount}",
                    _options.RecipientJids.Length);

                failures = 0;
                await MonitorConnectionAsync(session);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                string error = exception is XmppSendException sendError
                    ? sendError.ErrorCode
                    : exception.GetType().Name;

                logger.LogWarning("XMPP недоступен: {ErrorCode}", error);
            }
            finally
            {
                Interlocked.CompareExchange(ref _session, null, session);
                metrics.XmppReady.Set(0);

                await session.CloseAsync(stoppingToken.IsCancellationRequested);
            }

            await Task.Delay(ReconnectDelay(failures++), stoppingToken);
        }
    }

    public async Task SendAsync(
        Guid messageId,
        string recipientJid,
        string text,
        CancellationToken cancellationToken)
    {
        XmppSession session = Volatile.Read(ref _session) ?? throw new XmppSendException("not-connected");

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token);
        await _operationLock.WaitAsync(lifetime.Token);

        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            await session.SendAsync(messageId, recipientJid, text, lifetime.Token);
        }
        catch (Exception exception) when (exception is not XmppSendException)
        {
            // A failed or timed-out write leaves the stream counters ambiguous; reconnect before retrying.
            session.Abort();

            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task MonitorConnectionAsync(XmppSession session)
    {
        while (!session.Token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.PingIntervalSeconds), session.Token);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

            await _operationLock.WaitAsync(timeout.Token);

            try
            {
                await session.PingAsync(timeout.Token);
            }
            finally
            {
                _operationLock.Release();
            }
        }
    }

    private static TimeSpan ReconnectDelay(int failures)
    {
        double backoff = Math.Min(300, 5 * Math.Pow(2, Math.Min(failures, 6)));
        double jitter = 0.9 + (Random.Shared.NextDouble() * 0.1);

        return TimeSpan.FromSeconds(backoff * jitter);
    }

    public override void Dispose()
    {
        base.Dispose();
        _operationLock.Dispose();
    }
}
