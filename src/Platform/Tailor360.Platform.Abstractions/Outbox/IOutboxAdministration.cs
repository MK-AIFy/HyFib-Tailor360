using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Platform.Abstractions.Outbox;

/// <summary>
/// Reading the dead letter and putting messages back on the queue, as an operator does.
/// </summary>
/// <remarks>
/// A Platform contract because <c>platform.outbox_messages</c> is Platform's table: the module that
/// publishes the administration screens drives them through this port rather than mapping the table,
/// which is what keeps the module boundary a boundary (ARCH-005, module-ownership MO-2).
/// <para>
/// There are two callers — the command-line tool and the administration endpoint — and they must not
/// drift, because "replay" has to mean the same thing whichever one an operator reaches for at three in
/// the morning. Both go through this interface, so the update, the reason and the audit entry are
/// written once.
/// </para>
/// </remarks>
public interface IOutboxAdministration
{
    /// <summary>The dead letter, oldest failure first.</summary>
    /// <param name="limit">How many to return, clamped to <see cref="DeadLetteredMessage.MaximumLimit"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DeadLetteredMessage>> ListDeadLettersAsync(
        int limit = DeadLetteredMessage.DefaultLimit,
        CancellationToken cancellationToken = default);

    /// <summary>Puts one dead-lettered message back on the queue.</summary>
    /// <param name="messageId">The message.</param>
    /// <param name="reason">Why, as the operator typed it. Recorded in the audit trail.</param>
    /// <param name="actor">
    /// Who is replaying it, or null to attribute the entry to whoever the audit context resolves — which
    /// is what the command-line tool does, having no authenticated caller to name.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message as it stood before the replay, or a failure when it is not dead-lettered.</returns>
    Task<Result<DeadLetteredMessage>> ReplayAsync(
        Guid messageId,
        string reason,
        Guid? actor = null,
        CancellationToken cancellationToken = default);

    /// <summary>Puts every dead-lettered message back on the queue, and reports how many.</summary>
    /// <remarks>
    /// Deliberately not exposed over HTTP. Draining the whole dead letter is the response to an outage
    /// that has been diagnosed and fixed, which is a console decision made with the logs open, not a
    /// button on a screen — and one operator's idea of "everything" is another's idea of a duplicate
    /// delivery storm.
    /// </remarks>
    /// <param name="reason">Why, as the operator typed it.</param>
    /// <param name="actor">Who is replaying, or null to defer to the audit context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> ReplayAllAsync(
        string reason,
        Guid? actor = null,
        CancellationToken cancellationToken = default);
}

/// <summary>One message sitting in the dead letter, as an operator sees it.</summary>
/// <param name="Id">Identity of the message, and of the event it carries.</param>
/// <param name="AggregateId">The aggregate the event belongs to.</param>
/// <param name="EventType">The stable wire name, for example <c>orders.order_confirmed</c>.</param>
/// <param name="SchemaVersion">The payload schema version.</param>
/// <param name="OccurredAt">When the event occurred.</param>
/// <param name="DeadLetteredAt">When it exhausted its attempts, or null once it has been replayed.</param>
/// <param name="AttemptCount">How many delivery attempts were made before it was given up on.</param>
/// <param name="LastError">
/// The failure it was given up on, for diagnosis. The dispatcher never puts a payload value here, so it
/// is safe to show an operator and safe to log.
/// </param>
/// <param name="CorrelationId">The correlation identifier of the request that produced the event.</param>
public sealed record DeadLetteredMessage(
    Guid Id,
    Guid AggregateId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset? DeadLetteredAt,
    int AttemptCount,
    string? LastError,
    string? CorrelationId)
{
    /// <summary>How many the dead letter returns when the caller names no limit.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The most it will return in one call.</summary>
    public const int MaximumLimit = 200;
}

/// <summary>The failures outbox administration reports.</summary>
public static class OutboxErrors
{
    /// <summary>
    /// There is no such dead-lettered message. One code covers "no such message" and "that message is
    /// not dead-lettered" on purpose: the second is the interesting case — a replay that lost a race
    /// with another operator, or with the dispatcher succeeding on its own — and both mean the same
    /// thing to the caller, which is that there is nothing here to put back.
    /// </summary>
    public static Error NotDeadLettered { get; } = Error.NotFound(
        "platform.outbox-message-not-dead-lettered",
        "There is no dead-lettered message with that identifier. It may have been replayed already.");
}
