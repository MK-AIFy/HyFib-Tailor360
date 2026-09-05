namespace Tailor360.Platform.Persistence.Entities;

/// <summary>
/// One entry in the append-only audit trail. Rows are hash-chained by a database trigger, so altering
/// or deleting an entry breaks verification for every later entry and the tampering is detectable even
/// by someone who holds database credentials.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>Identity of the entry.</summary>
    public Guid Id { get; set; }

    /// <summary>When the audited action happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Sequence position within the chain, assigned by the database.</summary>
    public long Sequence { get; set; }

    /// <summary>The user who acted, or null for a system action.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>The actor's display name at the time of the action.</summary>
    public string ActorDisplayName { get; set; } = "system";

    /// <summary>The stable action name, for example <c>orders.order.confirmed</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The type of entity affected.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Identity of the entity affected.</summary>
    public Guid EntityId { get; set; }

    /// <summary>The branch the action belonged to, when it was branch-scoped.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>The correlation identifier of the request that caused the action.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>The reason the actor gave, required for sensitive actions.</summary>
    public string? Reason { get; set; }

    /// <summary>Operator-facing summary.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Redacted prior state.</summary>
    public string? Before { get; set; }

    /// <summary>Redacted resulting state.</summary>
    public string? After { get; set; }

    /// <summary>The hash of the previous entry, forming the chain.</summary>
    public string PreviousHash { get; set; } = string.Empty;

    /// <summary>This entry's hash, computed by the database trigger over its content and the previous hash.</summary>
    public string Hash { get; set; } = string.Empty;
}
