using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Platform.Persistence.Outbox;

/// <summary>
/// Claims and delivers outbox messages. Two dispatcher instances are the expected deployment, so every
/// step is written for that: claims are taken with <c>FOR UPDATE SKIP LOCKED</c> so instances do not
/// block each other, and only the oldest undelivered message of an aggregate is ever eligible, which is
/// what preserves per-aggregate order without a global lock.
/// </summary>
/// <param name="scopeFactory">Creates a scope per cycle.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Dispatcher configuration.</param>
/// <param name="logger">Logger.</param>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    /// <summary>
    /// Claims up to one batch and delivers it. Returns how many messages were processed, so the caller
    /// can poll immediately when a batch was full and back off when it was empty.
    /// </summary>
    public async Task<int> RunCycleAsync(string owner, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var claimed = await ClaimAsync(context, owner, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        PersistenceLog.OutboxClaimed(logger, owner, claimed.Count);

        var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>().ToLookup(h => h.EventType, StringComparer.Ordinal);

        foreach (var delivery in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeliverAsync(context, handlers[delivery.EventType], delivery, cancellationToken);
        }

        return claimed.Count;
    }

    private async Task<IReadOnlyList<OutboxDelivery>> ClaimAsync(
        PlatformDbContext context,
        string owner,
        CancellationToken cancellationToken)
    {
        // Only the oldest pending message per aggregate is eligible (rn = 1). A later message therefore
        // cannot overtake an earlier one for the same aggregate even under two dispatchers, which is a
        // stronger guarantee than ordering the claim query would give.
        const string sql = """
            WITH ranked AS (
                SELECT id,
                       row_number() OVER (PARTITION BY aggregate_id ORDER BY occurred_at, id) AS rn
                  FROM platform.outbox_messages
                 WHERE processed_at IS NULL
                   AND dead_lettered_at IS NULL
            ),
            eligible AS (
                SELECT m.id
                  FROM platform.outbox_messages m
                  JOIN ranked r ON r.id = m.id AND r.rn = 1
                 WHERE m.available_at <= now()
                   AND (m.lease_expires_at IS NULL OR m.lease_expires_at < now())
            ),
            locked AS (
                SELECT m.id
                  FROM platform.outbox_messages m
                 WHERE m.id IN (SELECT id FROM eligible)
                 ORDER BY m.occurred_at
                 LIMIT @batch
                 FOR UPDATE SKIP LOCKED
            )
            UPDATE platform.outbox_messages m
               SET lease_owner = @owner,
                   lease_expires_at = now() + (@lease_seconds * interval '1 second'),
                   attempt_count = m.attempt_count + 1
              FROM locked l
             WHERE m.id = l.id
            RETURNING m.id, m.aggregate_id, m.event_type, m.schema_version, m.payload,
                      m.occurred_at, m.correlation_id, m.attempt_count;
            """;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("batch", options.Value.BatchSize));
            command.Parameters.Add(new NpgsqlParameter("owner", owner));
            command.Parameters.Add(new NpgsqlParameter("lease_seconds", options.Value.LeaseDuration.TotalSeconds));

            var deliveries = new List<OutboxDelivery>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                deliveries.Add(new OutboxDelivery(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetInt32(7)));
            }

            return deliveries;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task DeliverAsync(
        PlatformDbContext context,
        IEnumerable<IOutboxMessageHandler> handlers,
        OutboxDelivery delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var handler in handlers)
            {
                // The inbox row and the handler's own writes commit together, so a handler that has
                // already run is skipped rather than run twice after a crash between the two.
                var alreadyHandled = await context.InboxMessages.AnyAsync(
                    i => i.MessageId == delivery.MessageId && i.HandlerName == handler.HandlerName,
                    cancellationToken);

                if (alreadyHandled)
                {
                    continue;
                }

                await handler.HandleAsync(delivery, cancellationToken);

                context.InboxMessages.Add(new Entities.InboxMessage
                {
                    MessageId = delivery.MessageId,
                    HandlerName = handler.HandlerName,
                    ProcessedAt = clock.UtcNow,
                });

                await context.SaveChangesAsync(cancellationToken);
            }

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE platform.outbox_messages
                    SET processed_at = {clock.UtcNow}, lease_owner = NULL, lease_expires_at = NULL,
                        last_error = NULL
                  WHERE id = {delivery.MessageId}
                 """,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await FailAsync(context, delivery, exception, cancellationToken);
        }
    }

    private async Task FailAsync(
        PlatformDbContext context,
        OutboxDelivery delivery,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The message text is kept, never the payload: a failure diagnostic must not become a second
        // copy of personal data in a column nobody thinks of as personal data.
        var error = Truncate(exception.GetType().Name + ": " + exception.Message, 2000);

        if (delivery.AttemptCount >= options.Value.MaximumAttempts)
        {
            PersistenceLog.OutboxDeadLettered(logger, delivery.MessageId);

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE platform.outbox_messages
                    SET dead_lettered_at = {clock.UtcNow}, lease_owner = NULL, lease_expires_at = NULL,
                        last_error = {error}
                  WHERE id = {delivery.MessageId}
                 """,
                cancellationToken);
            return;
        }

        PersistenceLog.OutboxRetry(logger, delivery.MessageId, delivery.AttemptCount, exception);

        var delay = NextDelay(delivery.AttemptCount);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE platform.outbox_messages
                SET available_at = {clock.UtcNow + delay}, lease_owner = NULL, lease_expires_at = NULL,
                    last_error = {error}
              WHERE id = {delivery.MessageId}
             """,
            cancellationToken);
    }

    /// <summary>
    /// Exponential backoff with jitter. The jitter matters when a downstream provider recovers: without
    /// it every message that failed during the outage would retry in the same instant and knock it over
    /// again.
    /// </summary>
    private TimeSpan NextDelay(int attemptCount)
    {
        var exponent = Math.Min(attemptCount, 10);
        var baseDelay = TimeSpan.FromTicks(options.Value.RetryBaseDelay.Ticks * (long)Math.Pow(2, exponent - 1));
        var capped = baseDelay > options.Value.RetryMaximumDelay ? options.Value.RetryMaximumDelay : baseDelay;
        var jitter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, (int)capped.TotalMilliseconds + 1);
        return capped + TimeSpan.FromMilliseconds(jitter / 2.0);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
