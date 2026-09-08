using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Outbox;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Entities;
using Tailor360.Platform.Persistence.Migrating;

namespace Tailor360.Platform.Persistence.Outbox;

/// <summary>
/// Lists and replays dead-lettered messages, across every module's outbox.
/// </summary>
/// <remarks>
/// <para>
/// Each module owns an <c>outbox_messages</c> table in its own schema (ADR-0008 section 4.1, issue
/// #77), so an operator's question — "what has stopped?" — is answered from all of them at once. A
/// listing that read one table would have been silently answering "nothing" while a module's dead
/// letter grew, which is the failure this screen exists to prevent.
/// </para>
/// <para>
/// A message identifier is a UUIDv7 and unique across the deployment, so replay finds it by looking
/// rather than by being told which module to look in. That keeps the operator's input the one thing
/// they have — an identifier from a log line or from the listing — and the listing names the module
/// anyway.
/// </para>
/// </remarks>
/// <param name="registry">The registered module contexts, which is also the list of outboxes.</param>
/// <param name="provider">Resolves each module's context from the current scope.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
public sealed class OutboxAdministration(
    ModuleContextRegistry registry,
    IServiceProvider provider,
    IAuditWriter audit,
    IClock clock)
    : IOutboxAdministration
{
    /// <summary>The audit action a replay is recorded under.</summary>
    public const string ReplayedAction = "platform.outbox.replayed";

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeadLetteredMessage>> ListDeadLettersAsync(
        int limit = DeadLetteredMessage.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var wanted = Math.Clamp(limit, 1, DeadLetteredMessage.MaximumLimit);
        var found = new List<DeadLetteredMessage>();

        foreach (var module in registry.Registrations)
        {
            var context = Context(module);

            // Each module contributes at most the whole limit, so a module whose dead letter is deep
            // cannot crowd out another module's entirely; the ordering below then takes the oldest.
            found.AddRange(await context.OutboxMessages
                .AsNoTracking()
                .Where(message => message.DeadLetteredAt != null)
                .OrderBy(message => message.DeadLetteredAt)
                .ThenBy(message => message.Id)
                .Take(wanted)
                .Select(message => Describe(message, module.Schema))
                .ToListAsync(cancellationToken));
        }

        return
        [
            .. found
                .OrderBy(message => message.DeadLetteredAt)
                .ThenBy(message => message.Id)
                .Take(wanted),
        ];
    }

    /// <inheritdoc />
    public async Task<Result<DeadLetteredMessage>> ReplayAsync(
        Guid messageId,
        string reason,
        Guid? actor = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var module in registry.Registrations)
        {
            var context = Context(module);

            var message = await context.OutboxMessages
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == messageId && candidate.DeadLetteredAt != null,
                    cancellationToken);

            if (message is null)
            {
                continue;
            }

            var before = Describe(message, module.Schema);

            // The update and its audit entry commit together. Without the transaction the update would
            // commit on its own, and a failure writing the entry would leave a message back on the
            // queue with no record of who put it there — which is precisely what the trail exists for.
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
                        $"Replayed a dead-lettered {before.EventType} message from the {module.Schema} "
                        + $"outbox after {before.AttemptCount} failed attempt(s).",
                        reason,
                        before,
                        Describe(message, module.Schema),
                        actor),
                    cancellationToken);

                await context.SaveChangesAsync(cancellationToken);
                await audit.SaveAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });

            return Result.Success(before);
        }

        return Result.Failure<DeadLetteredMessage>(OutboxErrors.NotDeadLettered);
    }

    /// <inheritdoc />
    public async Task<int> ReplayAllAsync(
        string reason,
        Guid? actor = null,
        CancellationToken cancellationToken = default)
    {
        var replayed = 0;

        foreach (var module in registry.Registrations)
        {
            var context = Context(module);

            var messages = await context.OutboxMessages
                .Where(message => message.DeadLetteredAt != null)
                .ToListAsync(cancellationToken);

            if (messages.Count == 0)
            {
                continue;
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

                // One entry per module's drain, not one per message: the operator performed a single
                // act, and a dead letter thousands deep would otherwise bury every other entry of that
                // hour under it. The per-message detail is still recoverable — the messages name
                // themselves in the entry.
                await audit.WriteAsync(
                    new AuditEntry(
                        ReplayedAction,
                        nameof(OutboxMessage),
                        Guid.Empty,
                        $"Drained the {module.Schema} dead letter, replaying {messages.Count} message(s).",
                        reason,
                        messages.Select(message => message.Id).Order().ToArray(),
                        null,
                        actor),
                    cancellationToken);

                await context.SaveChangesAsync(cancellationToken);
                await audit.SaveAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });

            replayed += messages.Count;
        }

        return replayed;
    }

    private ModuleDbContext Context(ModuleContextRegistration module)
        => (ModuleDbContext)provider.GetRequiredService(module.ContextType);

    private void Revive(OutboxMessage message)
    {
        message.DeadLetteredAt = null;
        message.AttemptCount = 0;
        message.AvailableAt = clock.UtcNow;
        message.LeaseOwner = null;
        message.LeaseExpiresAt = null;
        message.LastError = null;
    }

    private static DeadLetteredMessage Describe(OutboxMessage message, string module) => new(
        message.Id,
        module,
        message.AggregateId,
        message.EventType,
        message.SchemaVersion,
        message.OccurredAt,
        message.DeadLetteredAt,
        message.AttemptCount,
        message.LastError,
        message.CorrelationId);
}
