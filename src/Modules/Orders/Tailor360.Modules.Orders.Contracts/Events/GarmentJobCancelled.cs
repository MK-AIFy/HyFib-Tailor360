using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// One garment of an order was cancelled, while the order itself carries on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Distinct from <see cref="OrderCancelled"/>, and not a weaker form of it.</strong> An order with one
/// garment cancelled is still an order being made; a consumer subscribed to this event has subscribed to exactly
/// the garment it has to stop expecting.
/// </para>
/// <para>
/// <strong>The <c>deliver_together</c> partners this cancellation releases are deliberately not
/// published.</strong> SQ-08 in <c>docs/prd/state-transitions.md</c> section 10 — that a garment leaving the
/// parcel releases the rest of it — is "proposed, to be confirmed", and naming the released set here would freeze
/// an undecided product rule into a v1 contract that only a new major could correct. A consumer re-asks
/// <c>IOrderSnapshotQuery</c>, which answers from the rule in force at the moment it is asked.
/// </para>
/// <para>
/// <strong>Only the code travels, never the reason, and never the actor</strong> — the same grounds as
/// <see cref="OrderCancelled"/>.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the cancellation was recorded, in UTC.</param>
/// <param name="AggregateId">
/// The garment job, so the cancellation is ordered behind <see cref="GarmentJobCreated"/> and behind everything
/// else about the same garment.
/// </param>
/// <param name="OrganisationId">The organisation the garment belongs to.</param>
/// <param name="BranchId">The branch the garment was being made at.</param>
/// <param name="OrderId">The order the garment belongs to, which is not itself cancelled by this event.</param>
/// <param name="GarmentJobNumber">The human reference, so a rack tag or a queue row can be named.</param>
/// <param name="ReasonCode">
/// The configured cancellation reason, by its code. Never null and never blank, and never constrained to a fixed
/// set here: the vocabulary is configuration that issue #34 settles.
/// </param>
public sealed record GarmentJobCancelled(
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
    public const string Type = "orders.job-cancelled.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
