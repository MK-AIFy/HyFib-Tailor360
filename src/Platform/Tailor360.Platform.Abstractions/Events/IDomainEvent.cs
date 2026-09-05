namespace Tailor360.Platform.Abstractions.Events;

/// <summary>
/// Something that happened inside one aggregate. Domain events never leave the module that raised
/// them; what crosses a module boundary is an <see cref="IIntegrationEvent"/> published through the
/// transactional outbox.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Identity of this occurrence.</summary>
    Guid EventId { get; }

    /// <summary>When the event occurred, in UTC.</summary>
    DateTimeOffset OccurredAt { get; }
}
