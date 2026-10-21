using JabberAlertBridge.Services.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JabberAlertBridge.HealthChecks;

public sealed class XmppHealthCheck(IXmppMessageSender sender) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        HealthCheckResult result = sender.IsReady
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Нет готового XMPP-соединения для личной рассылки.");

        return Task.FromResult(result);
    }
}
