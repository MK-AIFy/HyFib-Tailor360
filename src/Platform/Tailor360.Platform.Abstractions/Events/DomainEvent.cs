namespace Tailor360.Platform.Abstractions.Events;

/// <summary>Convenience base for domain events. Derive with a positional record.</summary>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When the event occurred, in UTC.</param>
public abstract record DomainEvent(Guid EventId, DateTimeOffset OccurredAt) : IDomainEvent;
