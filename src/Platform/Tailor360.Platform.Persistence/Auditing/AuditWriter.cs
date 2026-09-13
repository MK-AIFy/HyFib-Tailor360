using System.Text.Json;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Auditing;

/// <summary>
/// Writes audit entries into the caller's own unit of work. <typeparamref name="TContext"/> is whichever
/// module context the caller already has — <c>BillingDbContext</c>, <c>IdentityDbContext</c>, or
/// <c>PlatformDbContext</c> itself when nothing more specific is available — so the entry is tracked by
/// the <em>same</em> change tracker as the change it describes and saved by the same
/// <c>SaveChangesAsync</c> call: the two commit or roll back together, and there is no window where a
/// crash between two separate saves could leave a committed mutation with no audit row, or an audit row
/// for a mutation that never committed
/// (<see href="https://github.com/MK-AIFy/HyFib-Tailor360/issues/179">#179</see>).
/// </summary>
/// <remarks>
/// Generic over the caller's context for the same reason <c>ModuleEventPublisher&lt;TContext&gt;</c> is:
/// a second, separate context is a second connection and a second transaction, so writing through it
/// could only ever be two commits — one of them possibly not the one the caller intended. A module wants
/// its own binding of this rather than depending on it directly for the same reason it has its own event
/// publisher port: the host composes every module at once, so one non-generic registration would resolve
/// to whichever module's context happened to be bound last. <see cref="AuditEventMapping"/> maps
/// <see cref="AuditEvent"/> on every context that needs to track one, so any of them can carry the row.
/// </remarks>
/// <typeparam name="TContext">The caller's own module context.</typeparam>
/// <param name="context">The caller's own context, shared with its stores.</param>
/// <param name="auditContext">Who is acting.</param>
/// <param name="clock">The clock.</param>
/// <param name="idGenerator">The identifier generator.</param>
public class AuditWriter<TContext>(
    TContext context,
    IAuditContext auditContext,
    IClock clock,
    IIdGenerator idGenerator)
    : IAuditWriter
    where TContext : ModuleDbContext
{
    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.Set<AuditEvent>().Add(new AuditEvent
        {
            Id = idGenerator.NewId(),
            OccurredAt = clock.UtcNow,
            // The entry's own actor wins where it names one. That is how a sign-in is attributed to the
            // account it signed in, on a request where nobody was authenticated when it arrived.
            ActorId = entry.ActorId ?? auditContext.ActorId,
            ActorDisplayName = entry.ActorDisplayName is { Length: > 0 } named
                ? named
                : auditContext.ActorDisplayName,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            BranchId = auditContext.BranchId,
            CorrelationId = auditContext.CorrelationId,
            Reason = entry.Reason,
            Summary = entry.Summary,
            Before = Serialise(entry.Before),
            After = Serialise(entry.After),

            // The chain trigger overwrites both. They are set to empty rather than left null because the
            // columns are NOT NULL, and the trigger runs after the insert statement is built.
            PreviousHash = string.Empty,
            Hash = string.Empty,
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Serialises a state snapshot to JSON. The column is <c>jsonb</c>, so a caller passing a plain
    /// value such as a boolean would otherwise produce a database error at write time rather than a
    /// compile-time mistake; serialising here makes every value valid by construction.
    /// </summary>
    private static string? Serialise(object? value)
        => value is null ? null : JsonSerializer.Serialize(value, SerializerOptions);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
