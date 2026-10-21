using JabberAlertBridge.Services.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JabberAlertBridge.HealthChecks;

public sealed class XmppHealthCheck(IXmppMessageSender sender) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(sender.IsReady ? HealthCheckResult.Healthy() :
            HealthCheckResult.Unhealthy("Бот не подключён к конференции."));
    }
}
