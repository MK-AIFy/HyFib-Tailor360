using System.Text.Json;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Auditing;

/// <summary>
/// Writes audit entries into the caller's unit of work. The entry is added to the same context as the
/// change it describes and saved with it, so the two commit or roll back together; an audit trail that
/// could disagree with the data would be worse than none, because it would be believed.
/// </summary>
/// <param name="context">The platform context.</param>
/// <param name="auditContext">Who is acting.</param>
/// <param name="clock">The clock.</param>
/// <param name="idGenerator">The identifier generator.</param>
public sealed class AuditWriter(
    PlatformDbContext context,
    IAuditContext auditContext,
    IClock clock,
    IIdGenerator idGenerator)
    : IAuditWriter
{
    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.AuditEvents.Add(new AuditEvent
        {
            Id = idGenerator.NewId(),
            OccurredAt = clock.UtcNow,
            ActorId = auditContext.ActorId,
            ActorDisplayName = auditContext.ActorDisplayName,
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

    /// <summary>
    /// Serialises a state snapshot to JSON. The column is <c>jsonb</c>, so a caller passing a plain
    /// value such as a boolean would otherwise produce a database error at write time rather than a
    /// compile-time mistake; serialising here makes every value valid by construction.
    /// </summary>
    private static string? Serialise(object? value)
        => value is null ? null : JsonSerializer.Serialize(value, SerializerOptions);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
