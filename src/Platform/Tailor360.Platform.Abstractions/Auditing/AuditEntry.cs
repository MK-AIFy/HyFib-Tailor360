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
/// <param name="ActorId">
/// Who acted, when the entry cannot take it from <c>IAuditContext</c>. Almost every entry leaves this
/// null and is attributed to the caller the request resolved. The exception is the sign-in path, where
/// the actor is established by the very action being recorded: on the login request nobody is
/// authenticated yet, so an entry that deferred to the context would attribute a person's own sign-in
/// to <c>system</c> and leave it out of every "what did this person do" query. A failed attempt against
/// an identifier that matches no account stays anonymous, because there is nobody to name.
/// </param>
/// <param name="ActorDisplayName">The actor's name, supplied with <paramref name="ActorId"/>.</param>
public sealed record AuditEntry(
    string Action,
    string EntityType,
    Guid EntityId,
    string Summary,
    string? Reason = null,
    object? Before = null,
    object? After = null,
    Guid? ActorId = null,
    string? ActorDisplayName = null);
