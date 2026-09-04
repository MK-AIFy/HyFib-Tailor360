namespace Tailor360.Platform.Persistence.Entities;

/// <summary>
/// Records that a handler has already processed a message. At-least-once delivery means a handler will
/// see the same message twice; this row is how the second delivery becomes a no-op rather than a second
/// stock movement or a second notification.
/// </summary>
public sealed class InboxMessage
{
    /// <summary>Identity of the message that was handled.</summary>
    public Guid MessageId { get; set; }

    /// <summary>The handler that processed it. One message may legitimately be handled by several.</summary>
    public string HandlerName { get; set; } = string.Empty;

    /// <summary>When it was processed.</summary>
    public DateTimeOffset ProcessedAt { get; set; }
}
