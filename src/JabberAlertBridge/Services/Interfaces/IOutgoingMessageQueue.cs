using JabberAlertBridge.Models;

namespace JabberAlertBridge.Services.Interfaces;

public interface IOutgoingMessageQueue
{
    Task<IReadOnlyList<Guid>> EnqueueAsync(
        IReadOnlyList<string> texts,
        string source,
        IReadOnlyList<string> recipientJids,
        CancellationToken cancellationToken);

    Task<OutgoingMessage?> ClaimNextAsync(string workerId, CancellationToken cancellationToken);

    Task CompleteAsync(OutgoingMessage message, string workerId, CancellationToken cancellationToken);

    Task RetryAsync(
        OutgoingMessage message,
        string workerId,
        DateTimeOffset nextAttemptAt,
        string error,
        bool permanent,
        CancellationToken cancellationToken);
}

public sealed class QueueFullException : Exception;
