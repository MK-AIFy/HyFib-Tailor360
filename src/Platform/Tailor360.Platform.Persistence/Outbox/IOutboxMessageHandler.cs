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

    /// <summary>
    /// The schema of the module this handler writes to, and therefore the inbox that records it ran.
    /// </summary>
    /// <remarks>
    /// The inbox row is only worth anything if it commits with the writes it records, so it has to be
    /// in the schema those writes land in. Naming the wrong one, or one no module owns, fails loudly
    /// on the first delivery rather than quietly recording the handler somewhere its effect is not
    /// (issue #77).
    /// </remarks>
    string Schema { get; }

    /// <summary>
    /// Handles one delivery. Must be safe to call twice with the same message.
    /// </summary>
    /// <remarks>
    /// <strong>Stage writes; do not save them.</strong> The dispatcher opens a transaction on the
    /// module's context, calls this, adds the inbox row and saves both together — which is what makes a
    /// redelivered message a no-op rather than a second effect. A handler that called
    /// <c>SaveChangesAsync</c> itself would commit its effect separately from the row that records it,
    /// and a crash in between would leave the effect with nothing to say it had happened.
    /// </remarks>
    /// <param name="delivery">The message.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the handler's writes are staged.</returns>
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
