namespace Tailor360.Platform.Abstractions.Events;

/// <summary>Convenience base for integration events. Derive with a positional record.</summary>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When the event occurred, in UTC.</param>
/// <param name="AggregateId">The aggregate whose ordering must be preserved.</param>
public abstract record IntegrationEvent(Guid EventId, DateTimeOffset OccurredAt, Guid AggregateId)
    : IIntegrationEvent
{
    /// <inheritdoc />
    public abstract string EventType { get; }

    /// <inheritdoc />
    public virtual int SchemaVersion => 1;
}
