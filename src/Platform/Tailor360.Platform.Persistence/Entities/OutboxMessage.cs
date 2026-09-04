namespace Tailor360.Platform.Persistence.Entities;

/// <summary>
/// An integration event awaiting delivery. The row is written in the same transaction as the state
/// change that produced it, which is what makes "the order was confirmed" and "someone was told the
/// order was confirmed" impossible to disagree about.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Identity of the message, and of the event it carries.</summary>
    public Guid Id { get; set; }

    /// <summary>When the event occurred.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// The aggregate the event belongs to. The dispatcher never has two messages for one aggregate in
    /// flight at the same time, so a consumer sees one aggregate's events in order.
    /// </summary>
    public Guid AggregateId { get; set; }

    /// <summary>The stable wire name of the event, for example <c>orders.order_confirmed</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The payload schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The serialised payload.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>The correlation identifier of the request that produced the event.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Earliest time the message may be dispatched; moved forward by the retry backoff.</summary>
    public DateTimeOffset AvailableAt { get; set; }

    /// <summary>How many delivery attempts have been made.</summary>
    public int AttemptCount { get; set; }

    /// <summary>The dispatcher instance currently holding the message, if any.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// When the current lease expires. A crashed dispatcher's messages become available again at this
    /// time, without an operator having to intervene.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>When the message was successfully dispatched.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>When the message was moved to the dead-letter state after exhausting its attempts.</summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }

    /// <summary>The last failure, kept for diagnosis. Never contains a payload value.</summary>
    public string? LastError { get; set; }
}
