using JabberAlertBridge.Configuration;
using JabberAlertBridge.Models;
using JabberAlertBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JabberAlertBridge.Infrastructure.Persistence;

public sealed class PostgresMessageQueue(
    IDbContextFactory<AlertDbContext> contextFactory,
    IOptions<DeliveryOptions> options) : IOutgoingMessageQueue
{
    private readonly DeliveryOptions _options = options.Value;

    public async Task<IReadOnlyList<Guid>> EnqueueAsync(
        IReadOnlyList<string> texts,
        string source,
        IReadOnlyList<string> recipientJids,
        CancellationToken cancellationToken)
    {
        string[] recipients = recipientJids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        long requiredCount = (long)texts.Count * recipients.Length;

        await using AlertDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // All enqueue and requeue operations use the same lock to enforce capacity and commit order.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(742341902)", cancellationToken);

        int count = await db.OutgoingMessages.CountAsync(
            message => message.Status == MessageStatus.Pending || message.Status == MessageStatus.Processing,
            cancellationToken);
        if (requiredCount > _options.MaxPendingMessages - count)
        {
            throw new QueueFullException();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var messages = new List<OutgoingMessage>();

        // One statement at a time guarantees that sequence matches the order of message parts.
        foreach (string text in texts)
        {
            foreach (string recipient in recipients)
            {
                var message = new OutgoingMessage
                {
                    Id = Guid.NewGuid(),
                    Source = source,
                    RecipientJid = recipient,
                    Content = text,
                    CreatedAt = now,
                    NextAttemptAt = now
                };

                db.OutgoingMessages.Add(message);
                await db.SaveChangesAsync(cancellationToken);

                messages.Add(message);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return messages.Select(message => message.Id).ToArray();
    }

    public async Task<OutgoingMessage?> ClaimNextAsync(string workerId, CancellationToken cancellationToken)
    {
        await using AlertDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        int leaseSeconds = _options.LeaseDurationSeconds;

        // Preserve order for each recipient without blocking delivery to other users.
        List<OutgoingMessage> messages = await db.OutgoingMessages.FromSqlInterpolated($"""
            WITH picked AS (
                SELECT id FROM outgoing_messages m
                WHERE (status = 'pending' AND next_attempt_at <= now()
                    OR status = 'processing' AND lease_until < now())
                  AND NOT EXISTS (
                    SELECT 1 FROM outgoing_messages earlier
                    WHERE earlier.status IN ('pending', 'processing')
                      AND earlier.recipient_jid = m.recipient_jid
                      AND earlier.sequence < m.sequence)
                ORDER BY sequence
                FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE outgoing_messages m
            SET status = 'processing', attempts = attempts + 1,
                locked_by = {workerId}, lease_until = now() + ({leaseSeconds} * interval '1 second')
            FROM picked WHERE m.id = picked.id RETURNING m.*
            """).AsNoTracking().ToListAsync(cancellationToken);

        return messages.SingleOrDefault();
    }

    public async Task CompleteAsync(
        OutgoingMessage message,
        string workerId,
        CancellationToken cancellationToken)
    {
        await using AlertDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        int rows = await OwnedMessage(db, message, workerId).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.Status, MessageStatus.Sent)
            .SetProperty(item => item.SentAt, DateTimeOffset.UtcNow)
            .SetProperty(item => item.LockedBy, (string?)null)
            .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
            .SetProperty(item => item.LastError, (string?)null), cancellationToken);

        CheckLease(rows);
    }

    public async Task RetryAsync(
        OutgoingMessage message,
        string workerId,
        DateTimeOffset nextAttemptAt,
        string error,
        bool permanent,
        CancellationToken cancellationToken)
    {
        await using AlertDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        bool dead = permanent || (_options.MaxAttempts > 0 && message.Attempts >= _options.MaxAttempts);

        int rows = await OwnedMessage(db, message, workerId).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.Status, dead ? MessageStatus.Dead : MessageStatus.Pending)
            .SetProperty(item => item.DeadAt, dead ? DateTimeOffset.UtcNow : (DateTimeOffset?)null)
            .SetProperty(item => item.NextAttemptAt, nextAttemptAt)
            .SetProperty(item => item.LastError, error.Length <= 512 ? error : error.Substring(0, 512))
            .SetProperty(item => item.LockedBy, (string?)null)
            .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null), cancellationToken);

        CheckLease(rows);
    }

    private static IQueryable<OutgoingMessage> OwnedMessage(
        AlertDbContext db,
        OutgoingMessage message,
        string workerId)
    {
        return db.OutgoingMessages.Where(item => item.Id == message.Id && item.LockedBy == workerId
            && item.Attempts == message.Attempts && item.Status == MessageStatus.Processing);
    }

    private static void CheckLease(int rows)
    {
        if (rows != 1)
        {
            throw new InvalidOperationException("Аренда сообщения больше не принадлежит обработчику.");
        }
    }
}
