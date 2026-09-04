namespace Tailor360.Platform.Abstractions.Events;

/// <summary>
/// A fact one module publishes for others to consume. Integration events are written to the outbox in
/// the same transaction as the state change that produced them (ADR-0008), so a consumer never sees an
/// event for work that was rolled back.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Identity of this occurrence; consumers de-duplicate on it.</summary>
    Guid EventId { get; }

    /// <summary>When the event occurred, in UTC.</summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// The aggregate the event belongs to. The dispatcher preserves publication order per aggregate,
    /// so consumers see one aggregate's events in the order they happened.
    /// </summary>
    Guid AggregateId { get; }

    /// <summary>
    /// The stable wire name, for example <c>orders.order_confirmed</c>. Renaming a type must not change
    /// this value; a new shape gets a new <see cref="SchemaVersion"/> or a new name.
    /// </summary>
    string EventType { get; }

    /// <summary>The schema version of the payload, starting at 1 and incremented for breaking changes.</summary>
    int SchemaVersion { get; }
}
