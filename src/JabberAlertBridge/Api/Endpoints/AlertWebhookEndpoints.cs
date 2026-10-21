using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using JabberAlertBridge.Configuration;
using JabberAlertBridge.Models.Webhooks;
using JabberAlertBridge.Observability;
using JabberAlertBridge.Services;
using JabberAlertBridge.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace JabberAlertBridge.Api.Endpoints;

public static class AlertWebhookEndpoints
{
    public const string Path = "/api/webhooks/alerts";
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    public static void MapAlertWebhooks(this WebApplication app)
    {
        app.MapPost(Path, HandleAsync).RequireAuthorization();
    }

    public static async Task ObserveRequestAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path != Path)
        {
            await next(context);
            return;
        }

        var timer = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            string source = context.Items["WebhookSource"] as string ?? "webhook";
            context.RequestServices.GetRequiredService<BridgeMetrics>().Webhooks
                .WithLabels(source, context.Response.StatusCode.ToString(CultureInfo.InvariantCulture)).Inc();
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Webhook")
                .LogInformation("Webhook {RequestId}: {StatusCode}, источник {Source}, длительность {ElapsedMs} мс",
                    context.TraceIdentifier, context.Response.StatusCode, source, timer.Elapsed.TotalMilliseconds);
        }
    }

    private static async Task<IResult> HandleAsync(HttpContext context, AlertProcessor processor,
        IOptions<AlertFormattingOptions> options, BridgeMetrics metrics, ILogger<AlertProcessor> logger)
    {
        if (!context.Request.HasJsonContentType())
        {
            return Results.Problem(statusCode: 415, title: "Требуется Content-Type: application/json.");
        }

        int limit = options.Value.MaxRequestBodyBytes;
        if (context.Request.ContentLength > limit)
        {
            return Results.Problem(statusCode: 413, title: "Превышен размер запроса.");
        }

        try
        {
            using var body = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                if (body.Length + read > limit)
                {
                    return Results.Problem(statusCode: 413, title: "Превышен размер запроса.");
                }

                body.Write(buffer, 0, read);
            }

            body.Position = 0;
            AlertWebhookRequest request = await JsonSerializer.DeserializeAsync<AlertWebhookRequest>(body, s_jsonOptions, context.RequestAborted)
                ?? throw new WebhookValidationException("Требуется JSON-объект уведомления.");
            EnqueueResult result = await processor.ProcessAsync(request, context.RequestAborted);
            context.Items["WebhookSource"] = result.Source;
            metrics.Enqueued.Inc(result.MessageIds.Count);
            logger.LogInformation("Запрос {RequestId}: алертов {AlertCount}, сохранено сообщений {QueuedCount}",
                context.TraceIdentifier, result.AlertCount, result.MessageIds.Count);

            return Results.Json(new { status = "queued", messageIds = result.MessageIds, queuedCount = result.MessageIds.Count }, statusCode: 202);
        }
        catch (JsonException)
        {
            return Results.Problem(statusCode: 400, title: "Некорректный JSON или типы полей.");
        }
        catch (WebhookValidationException exception)
        {
            return Results.Problem(statusCode: 400, title: exception.Message);
        }
        catch (QueueFullException)
        {
            context.Response.Headers.RetryAfter = "30";
            return Results.Problem(statusCode: 429, title: "Очередь заполнена. Повторите запрос позже.");
        }
        catch (BadHttpRequestException exception)
        {
            return Results.Problem(statusCode: exception.StatusCode, title: "Некорректный HTTP-запрос.");
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            logger.LogWarning("Не удалось сохранить webhook {RequestId}: {ErrorType}", context.TraceIdentifier, exception.GetType().Name);
            return Results.Problem(statusCode: 503, title: "Хранилище очереди временно недоступно.");
        }
    }

    private static bool IsDatabaseFailure(Exception exception)
    {
        return exception is DbException or Microsoft.EntityFrameworkCore.DbUpdateException or TimeoutException
            || exception is InvalidOperationException { InnerException: { } inner } && IsDatabaseFailure(inner);
    }
}
