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
    /// The stable wire name: <c>&lt;module&gt;.&lt;event-name&gt;.v&lt;major&gt;</c>, for example
    /// <c>customers.consent-withdrawn.v1</c>.
    /// </summary>
    /// <remarks>
    /// The shape is fixed by <c>docs/architecture/conventions.md</c> section 5.5 and held to it by
    /// <c>IntegrationEventTests</c> in the contract tier, which also checks that the <c>.v</c> suffix
    /// and <see cref="SchemaVersion"/> agree — two ways of saying the same number, and a subscriber
    /// routing on the name while a producer bumped only the property is how they stop agreeing.
    /// Renaming the .NET type must not change this value.
    /// </remarks>
    string EventType { get; }

    /// <summary>
    /// The schema version of the payload, starting at 1 and incremented for breaking changes.
    /// </summary>
    /// <remarks>
    /// A breaking change publishes the new major <em>alongside</em> the old one for the deprecation
    /// window rather than replacing it, so a subscriber migrates on its own schedule. Within a major
    /// version only additive changes are permitted.
    /// </remarks>
    int SchemaVersion { get; }
}
