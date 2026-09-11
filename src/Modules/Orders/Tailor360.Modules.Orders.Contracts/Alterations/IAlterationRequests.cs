using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Contracts.Alterations;

/// <summary>
/// The alteration hand-off: how something outside Orders asks for a garment to be altered.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Service recovery (#49) is the caller this exists for.</strong> EX-11 in
/// <c>docs/prd/exceptions.md</c> says that when feedback arrives at or below the branch's rating threshold, or the
/// customer explicitly asks for an alteration, a service recovery case is opened — and that an accepted
/// alteration opens a garment job through this contract. Feedback belongs to Notifications and the case to the
/// feedback module; neither may write <c>orders.alterations</c>
/// (<c>docs/architecture/module-ownership.md</c> section 5.5), so this is the one door in. A member of staff
/// raising an alteration at the counter comes through the Orders API rather than through here, and
/// <see cref="AlterationSource"/> is what keeps the two apart in the record.
/// </para>
/// <para>
/// <strong>It is a command, not a query, and it is refusable</strong> — the garment may not be on the order, the
/// order may be cancelled, the caller's branch may not reach it, the reason code may not be one the branch
/// configured. So it returns <see cref="Result{TValue}"/> rather than throwing: a <c>Contracts</c> project may
/// reference <c>Platform.Abstractions</c>, expected failures are results and exceptions stay reserved for
/// defects, and a caller in the middle of closing a service recovery case needs to be told why rather than to
/// unwind.
/// </para>
/// <para>
/// <strong>No free text crosses this boundary, in either direction.</strong> The caller passes the configured
/// reason <em>code</em> and never the words the alteration was asked for in, and for the caller this exists for
/// that distinction is the whole point. A feedback-sourced alteration is asked for in the customer's own comment,
/// and feedback free text is <strong>Personal</strong> — and <strong>Sensitive Personal</strong> where it names
/// or describes a person — under <c>docs/nfr/data-classification.md</c> section 5.14, whose deletion row reads
/// "feedback free text deleted on request, leaving the rating if the customer asked only for the comment to go.
/// Every deletion audited". Orders data, by contrast, has "No routine deletion" under section 5.7. A verbatim
/// copy taken across this boundary would therefore still be standing in <c>orders.alterations</c> after an
/// approved erasure had emptied the row it was copied from, which is exactly what section 3.1 rule 1 forbids: a
/// deletion "is not complete until the projections that carry the value have been rebuilt or purged". So the
/// words stay in the module that owns them and that can delete them, and what travels is a code and two
/// identifiers that lead back to them. It is the same line every one of the eleven Orders events already takes,
/// and worth stating here because this parameter is the obvious place to get it wrong.
/// </para>
/// <para>
/// <strong>The events, the price decision and the due-date decision are #34's.</strong> Nothing in the Domain on
/// this branch records an alteration yet; this interface is published now because #49 is written against it and
/// a consumer cannot be built against a contract that does not exist. <c>orders.alteration-requested.v1</c>, when
/// #34 raises it, carries the same configured code.
/// </para>
/// </remarks>
public interface IAlterationRequests
{
    /// <summary>Records that an alteration has been asked for on one garment of one order.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Opening is recording, and deciding is a separate act.</strong>
    /// <c>docs/prd/state-transitions.md</c> section 3.2 puts the price and due-date decisions, the communication
    /// and the new or reopened garment job at the <em>decision</em>, under
    /// <c>orders.alteration_decide</c> — not here. What this call guarantees is that the request exists, is
    /// attributed and will be worked; what it does not guarantee is that a garment job exists yet, which is why
    /// <see cref="AlterationRequestReference.GarmentJobId"/> is nullable.
    /// </para>
    /// <para>
    /// <strong>The parameters are plan #34's <c>Open(orderId, jobId, reason, source, caseId)</c> with three
    /// deliberate differences</strong>, each corrected in the documents in the same change rather than left to
    /// disagree: the free-text reason is narrowed to a configured reason code for the data-classification reason
    /// in the interface's remarks; <paramref name="feedbackId"/> is added so that a feedback-sourced request can
    /// be traced back to the comment it came from without carrying it; and the method takes the house
    /// <c>Async</c> suffix, which every asynchronous method on every <c>Contracts</c> interface in this
    /// repository carries, together with the trailing cancellation token they all carry too.
    /// </para>
    /// </remarks>
    /// <param name="orderId">The order the garment belongs to.</param>
    /// <param name="garmentJobId">
    /// The garment to be altered. Refused when it is not a garment of that order.
    /// </param>
    /// <param name="reasonCode">
    /// Why the alteration was asked for, as the configured reason code and never as the words it was asked in.
    /// The vocabulary is branch configuration, reviewed under <strong>OD-10</strong> and seeded by issue #34, so
    /// nothing here constrains its shape beyond the forty characters the Orders event schemas bound a reason code
    /// at; a code the branch has not configured is a refusal and not an exception.
    /// </param>
    /// <param name="source">Whether a member of staff raised it or it came from the customer's feedback.</param>
    /// <param name="serviceRecoveryCaseId">
    /// The service recovery case this came out of, or null when a member of staff raised it at the counter. It is
    /// the "on whose authority" handle: a consumer asking why an alteration exists has something to quote back,
    /// while the case itself — who, when, the contact attempts and the free text — stays where it belongs.
    /// </param>
    /// <param name="feedbackId">
    /// The customer feedback the case was opened from, or null when there was none. An identifier and nothing
    /// else, and the handle that leads back to what the customer actually wrote: #49 creates a service recovery
    /// case idempotently on the feedback identifier, and the comment itself stays on the feedback row under
    /// <c>docs/nfr/data-classification.md</c> section 5.14, where its own erasure path can reach it. A reader
    /// entitled to the words asks the module that owns them, having been re-authorised there.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The reference to the recorded request, or the reason it was refused.</returns>
    Task<Result<AlterationRequestReference>> OpenAsync(
        Guid orderId,
        Guid garmentJobId,
        string reasonCode,
        AlterationSource source,
        Guid? serviceRecoveryCaseId,
        Guid? feedbackId,
        CancellationToken cancellationToken = default);
}

/// <summary>What the caller keeps after asking for an alteration.</summary>
/// <remarks>
/// <para>
/// <strong>Two documents disagree about what exists at open, and this record is the honest reading of both.</strong>
/// Plan #34 says the request is merely recorded at open and that the new or reopened garment job is linked at the
/// <em>decision</em>; plan #49 says the caller stores the returned job identifier. Both cannot be true at the
/// same moment, so the request identifier is always returned and the garment job identifier is returned when
/// there is one.
/// </para>
/// <para>
/// #34 must settle it. If it lands as "the job exists at open", this becomes a value that is simply never null
/// and no signature changes — which is why the question is answered with a nullable field rather than with two
/// overloads or a second method.
/// </para>
/// </remarks>
/// <param name="AlterationRequestId">The recorded request. A UUIDv7, and what a case stores against itself.</param>
/// <param name="GarmentJobId">
/// The new or reopened garment job, or null until the alteration is decided. A null here means "not yet", never
/// "refused" — a refusal is a failed <see cref="Result{TValue}"/> and never a reference with nothing in it.
/// </param>
public sealed record AlterationRequestReference(Guid AlterationRequestId, Guid? GarmentJobId);

/// <summary>Where an alteration request came from.</summary>
/// <remarks>
/// <para>
/// The distinction is not cosmetic. EX-11 makes a feedback-sourced alteration part of a service recovery case
/// with an owning role, a due time in business hours and an escalation ladder, and a staff-raised one carries
/// none of that. A dashboard of pending alterations that could not tell them apart would mix a customer
/// complaint in with a counter correction.
/// </para>
/// <para>
/// <see cref="Staff"/> is deliberately the zero value, so a request that arrives default-constructed is the one
/// that claims no customer complaint behind it rather than inventing one.
/// </para>
/// </remarks>
public enum AlterationSource
{
    /// <summary>A member of staff raised it — at the counter, or on finding a fault before handover.</summary>
    Staff = 0,

    /// <summary>It came from the customer's feedback, through a service recovery case (EX-11, issue #49).</summary>
    Feedback = 1,
}
