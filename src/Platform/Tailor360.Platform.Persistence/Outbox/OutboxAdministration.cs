using Microsoft.EntityFrameworkCore;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Outbox;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Outbox;

/// <summary>
/// Lists the dead letter and puts messages back on the queue.
/// </summary>
/// <remarks>
/// One difference from the command-line tool as it was written: a replay clears <c>last_error</c> as
/// well as the dead-letter mark. Leaving it set meant a message that had since been dispatched
/// successfully still carried the error it failed with once, which is the kind of stale detail an
/// operator reads as a live problem.
/// </remarks>
/// <param name="context">The platform context.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
public sealed class OutboxAdministration(PlatformDbContext context, IAuditWriter audit, IClock clock)
    : IOutboxAdministration
{
    /// <summary>The audit action recorded when a message is replayed.</summary>
    public const string ReplayedAction = "platform.outbox.replayed";

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeadLetteredMessage>> ListDeadLettersAsync(
        int limit = DeadLetteredMessage.DefaultLimit,
        CancellationToken cancellationToken = default)
        => await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.DeadLetteredAt != null)
            .OrderBy(message => message.DeadLetteredAt)
            .ThenBy(message => message.Id)
            .Take(Math.Clamp(limit, 1, DeadLetteredMessage.MaximumLimit))
            .Select(message => new DeadLetteredMessage(
                message.Id,
                message.AggregateId,
                message.EventType,
                message.SchemaVersion,
                message.OccurredAt,
                message.DeadLetteredAt,
                message.AttemptCount,
                message.LastError,
                message.CorrelationId))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Result<DeadLetteredMessage>> ReplayAsync(
        Guid messageId,
        string reason,
        Guid? actor = null,
        CancellationToken cancellationToken = default)
    {
        var message = await context.OutboxMessages
            .FirstOrDefaultAsync(
                candidate => candidate.Id == messageId && candidate.DeadLetteredAt != null,
                cancellationToken);

        if (message is null)
        {
            return Result.Failure<DeadLetteredMessage>(OutboxErrors.NotDeadLettered);
        }

        var before = Describe(message);

        // The update and its audit entry commit together. Without the transaction the update would
        // commit on its own, and a failure writing the entry would leave a message back on the queue
        // with no record of who put it there — which is precisely what the trail exists for.
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await context.Database.BeginTransactionAsync(cancellationToken);

            Revive(message);

            await audit.WriteAsync(
                new AuditEntry(
                    ReplayedAction,
                    nameof(OutboxMessage),
                    message.Id,
                    $"Replayed a dead-lettered {before.EventType} message after {before.AttemptCount} failed attempt(s).",
                    reason,
                    before,
                    Describe(message),
                    actor),
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return Result.Success(before);
    }

    /// <inheritdoc />
    public async Task<int> ReplayAllAsync(
        string reason,
        Guid? actor = null,
        CancellationToken cancellationToken = default)
    {
        var messages = await context.OutboxMessages
            .Where(message => message.DeadLetteredAt != null)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await context.Database.BeginTransactionAsync(cancellationToken);

            foreach (var message in messages)
            {
                Revive(message);
            }

            // One entry for the drain, not one per message: the operator performed a single act, and a
            // dead letter thousands deep would otherwise bury every other entry of that hour under it.
            // The per-message detail is still recoverable — the messages name themselves in the entry.
            await audit.WriteAsync(
                new AuditEntry(
                    ReplayedAction,
                    nameof(OutboxMessage),
                    Guid.Empty,
                    $"Drained the dead letter, replaying {messages.Count} message(s).",
                    reason,
                    messages.Select(message => message.Id).Order().ToArray(),
                    null,
                    actor),
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return messages.Count;
    }

    private void Revive(OutboxMessage message)
    {
        message.DeadLetteredAt = null;
        message.AttemptCount = 0;
        message.AvailableAt = clock.UtcNow;
        message.LeaseOwner = null;
        message.LeaseExpiresAt = null;
        message.LastError = null;
    }

    private static DeadLetteredMessage Describe(OutboxMessage message) => new(
        message.Id,
        message.AggregateId,
        message.EventType,
        message.SchemaVersion,
        message.OccurredAt,
        message.DeadLetteredAt,
        message.AttemptCount,
        message.LastError,
        message.CorrelationId);
}
