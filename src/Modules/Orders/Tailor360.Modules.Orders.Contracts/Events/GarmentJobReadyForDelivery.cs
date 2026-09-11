using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// The ready-for-delivery gate opened on a garment.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the gate opening, not a routine recomputation.</strong> The gate is re-evaluated on every
/// custody event and can demote a garment as readily as promote one, so the event is raised only on the
/// transition from not-ready to ready. There is no not-ready event, and none is listed in
/// <c>docs/architecture/module-ownership.md</c> section 5.5 either: a consumer that wants the current verdict
/// re-reads it.
/// </para>
/// <para>
/// <strong>No blocks list, by construction.</strong> The gate opening means no predicate blocked, so a block list
/// on this event would always be empty. The reason codes that keep a garment back reach a screen through
/// <c>IOrderSnapshotQuery</c> instead.
/// </para>
/// <para>
/// <strong><see cref="EvaluatedAt"/> is not <see cref="IIntegrationEvent.OccurredAt"/>.</strong> It is the instant
/// the facts the verdict was reached from were gathered, and CI-03 in
/// <c>docs/architecture/invariants.md</c> requires that a ready state never outlives them — the domain refuses a
/// verdict older than the one a garment already carries, and a consumer needs the same instant to make the same
/// comparison rather than trusting arrival order.
/// </para>
/// <para>
/// <strong><see cref="BoundWithGarmentJobIds"/> is the parcel as the gate saw it at
/// <see cref="EvaluatedAt"/>, and never durable membership.</strong> Three questions about a
/// <c>deliver_together</c> parcel are open in <c>docs/prd/state-transitions.md</c> section 10 and all three touch
/// this payload: whether the parcel is the transitive closure of the relation (SQ-07), whether a cancelled or
/// delivered garment leaves it (SQ-08), and whether one member's hold closes the whole parcel's gate (SQ-09).
/// SQ-09's interim position is precisely that it does not — so the queue can show a garment as ready which
/// cannot in fact be handed over until its partner returns, and the promise is kept <em>at the door</em>: a
/// handover that would strand a live partner is refused at the scan. A consumer that stored this array and
/// treated it as the parcel would be enforcing a rule that has since been decided differently. Ask
/// <c>IOrderSnapshotQuery</c> at the handover.
/// </para>
/// <para>
/// <strong>No actor, on principle rather than by omission.</strong> The gate clears it deliberately — the gate is
/// the system, not a person — and <c>docs/prd/raci.md</c> row 16 says no role, however senior, declares a garment
/// ready.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the verdict was applied to the garment, in UTC.</param>
/// <param name="AggregateId">The garment job, ordered behind <see cref="GarmentJobCreated"/>.</param>
/// <param name="OrganisationId">The organisation the garment belongs to.</param>
/// <param name="BranchId">The branch holding it for collection.</param>
/// <param name="OrderId">The order the garment belongs to.</param>
/// <param name="GarmentJobNumber">The human reference, so a rack tag or a queue row can be named.</param>
/// <param name="DueDate">
/// The garment's promised date, branch-local. The delivery queue is ordered by the promise, which is why it is on
/// the event that puts a garment onto it.
/// </param>
/// <param name="EvaluatedAt">
/// When the gate gathered the facts it judged on, in UTC. Earlier than or equal to
/// <see cref="IIntegrationEvent.OccurredAt"/>, and the value CI-03's staleness comparison is made against.
/// </param>
/// <param name="BoundWithGarmentJobIds">
/// The rest of the <c>deliver_together</c> parcel this verdict was reached inside, in job-number order. Empty for
/// a garment bound to nothing and empty where the branch policy permits partial delivery. A snapshot of what the
/// gate evaluated, never a durable statement of membership — see the remarks.
/// </param>
public sealed record GarmentJobReadyForDelivery(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid OrderId,
    string GarmentJobNumber,
    DateOnly DueDate,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<Guid> BoundWithGarmentJobIds)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.job-ready-for-delivery.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
