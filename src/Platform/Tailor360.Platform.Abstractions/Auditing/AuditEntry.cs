namespace Tailor360.Platform.Abstractions.Auditing;

/// <summary>
/// One entry in the append-only audit trail. Entries are written in the same transaction as the change
/// they describe and are hash-chained by a database trigger, so a later edit is detectable (#21, #57).
/// </summary>
/// <param name="Action">Stable dotted action name, for example <c>orders.order.confirmed</c>.</param>
/// <param name="EntityType">The type of the entity affected, for example <c>Order</c>.</param>
/// <param name="EntityId">Identity of the entity affected.</param>
/// <param name="Summary">Operator-facing summary. Never include secrets; personal data only where the entry is about it.</param>
/// <param name="Reason">The reason the actor supplied, required for sensitive actions.</param>
/// <param name="Before">
/// Redacted prior state. Any object; the writer serialises it, so a caller cannot store something the
/// column will reject. Null for creations.
/// </param>
/// <param name="After">Redacted resulting state, serialised the same way. Null for deletions.</param>
public sealed record AuditEntry(
    string Action,
    string EntityType,
    Guid EntityId,
    string Summary,
    string? Reason = null,
    object? Before = null,
    object? After = null);
