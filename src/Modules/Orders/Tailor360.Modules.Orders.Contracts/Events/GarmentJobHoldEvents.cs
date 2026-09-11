using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// Work on a garment was suspended.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A hold closes the ready state of this garment and of no other.</strong> That is SQ-09 in
/// <c>docs/prd/state-transitions.md</c> section 10, and it is "proposed, to be confirmed" rather than settled, so
/// a consumer should know which reading the system currently takes: a <c>deliver_together</c> partner left
/// standing at ready stays at ready and stays on the delivery queue, and the promise is kept at the door instead
/// — a handover that would strand a live partner is refused at the scan. The alternative, closing the whole
/// parcel's gate on one member's hold, would make a hold a writer of another garment's ready state, which
/// INV-JOB-07 forbids.
/// </para>
/// <para>
/// So a consumer that keeps a delivery queue must not infer anything about any other garment from this event.
/// The parcel is asked for again at handover, against <c>IOrderSnapshotQuery</c>.
/// </para>
/// <para>
/// <strong>Only the code travels.</strong> The free-text hold reason is what a member of staff typed about a
/// named person's garment, which <c>docs/architecture/conventions.md</c> section 5.5 does not admit to a payload;
/// it stays on the hold record. Nor does the approval or the actor travel — the hold policy and who signed it off
/// are staff personal data under <c>docs/nfr/data-classification.md</c> section 5.15, and audit (ARCH-008) is the
/// authority on who.
/// </para>
/// <para>
/// <strong>And no previous status.</strong> The transition is the event; taking a garment off a delivery queue is
/// idempotent, so the bit buys a consumer nothing it would not otherwise do.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the hold was recorded, in UTC.</param>
/// <param name="AggregateId">The garment job, ordered behind <see cref="GarmentJobCreated"/>.</param>
/// <param name="OrganisationId">The organisation the garment belongs to.</param>
/// <param name="BranchId">The branch whose workshop holds it.</param>
/// <param name="OrderId">The order the garment belongs to.</param>
/// <param name="GarmentJobNumber">The human reference, so a rack tag or a queue row can be named.</param>
/// <param name="ReasonCode">
/// The configured hold reason, by its code. Never null and never blank, and never constrained to a fixed set
/// here: the vocabulary is configuration that issue #34 settles.
/// </param>
public sealed record GarmentJobHeld(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid OrderId,
    string GarmentJobNumber,
    string ReasonCode)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.job-held.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>
/// A held garment went back to being made.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The thinnest payload of the eleven, and deliberately so.</strong>
/// </para>
/// <para>
/// <strong>There is no reason code, because there is nothing configured to publish.</strong> <c>Order.Resume</c>
/// takes free text and no code at all, and inventing a code here would be inventing a vocabulary — a product
/// decision a reviewer would treat as a change to the product. The free text itself stays in Orders, on the same
/// grounds as every other reason in this namespace.
/// </para>
/// <para>
/// <strong>And no ready state.</strong> Resuming clears the garment's ready state rather than restoring it: the
/// gate recomputation that follows is what decides, so a consumer waits for
/// <see cref="GarmentJobReadyForDelivery"/> rather than reading readiness out of this event. That is
/// <c>docs/prd/state-transitions.md</c> section 3.2's reading and INV-JOB-07's single writer.
/// </para>
/// <para>
/// <strong>A resume never moves the promised date.</strong> Time lost on hold is renegotiated openly through
/// <see cref="GarmentJobRescheduled"/>, which is its own event with its own permission — so a consumer must not
/// infer a new due date from a resume.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the resume was recorded, in UTC.</param>
/// <param name="AggregateId">The garment job, ordered behind the <see cref="GarmentJobHeld"/> it answers.</param>
/// <param name="OrganisationId">The organisation the garment belongs to.</param>
/// <param name="BranchId">The branch whose workshop resumed it.</param>
/// <param name="OrderId">The order the garment belongs to.</param>
/// <param name="GarmentJobNumber">The human reference, so a rack tag or a queue row can be named.</param>
public sealed record GarmentJobResumed(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid OrderId,
    string GarmentJobNumber)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.job-resumed.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
