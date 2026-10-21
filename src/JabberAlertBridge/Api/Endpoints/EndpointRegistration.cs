using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;

namespace JabberAlertBridge.Api.Endpoints;

public static class EndpointRegistration
{
    public static void MapBridgeEndpoints(this WebApplication app)
    {
        app.UseExceptionHandler(handler => handler.Run(WriteErrorAsync));

        app.UseHttpMetrics();
        app.Use(AlertWebhookEndpoints.ObserveRequestAsync);

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapAlertWebhooks();
        app.MapMetrics();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        });

        app.MapHealthChecks("/health/xmpp", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("xmpp")
        });
    }

    private static async Task WriteErrorAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        IResult problem = Results.Problem(
            statusCode: 500,
            title: "Внутренняя ошибка сервиса.",
            extensions: new Dictionary<string, object?> { ["requestId"] = context.TraceIdentifier });

        await problem.ExecuteAsync(context);
    }
}
