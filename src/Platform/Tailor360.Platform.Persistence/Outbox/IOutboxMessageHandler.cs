namespace Tailor360.Platform.Persistence.Outbox;

/// <summary>
/// Handles one kind of integration event as it is dispatched. A module registers a handler for each
/// event it consumes; an event with no handler is not an error, because publishing a fact that nobody
/// currently needs is normal and a consumer may be added later.
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>The wire name of the event this handler consumes.</summary>
    string EventType { get; }

    /// <summary>
    /// A stable name for this handler, used for inbox de-duplication. Two handlers of the same event
    /// must have different names or one would suppress the other's delivery.
    /// </summary>
    string HandlerName { get; }

    /// <summary>Handles one delivery. Must be safe to call twice with the same message.</summary>
    Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>One message being delivered.</summary>
/// <param name="MessageId">Identity of the message.</param>
/// <param name="AggregateId">The aggregate whose ordering is being preserved.</param>
/// <param name="EventType">The wire name of the event.</param>
/// <param name="SchemaVersion">The payload schema version.</param>
/// <param name="Payload">The serialised payload.</param>
/// <param name="OccurredAt">When the event occurred.</param>
/// <param name="CorrelationId">The correlation identifier of the request that produced it.</param>
/// <param name="AttemptCount">Which attempt this is, starting at 1.</param>
public sealed record OutboxDelivery(
    Guid MessageId,
    Guid AggregateId,
    string EventType,
    int SchemaVersion,
    string Payload,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    int AttemptCount);
