using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// A confirmed order was revised.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The consumers that must act on this are the ones holding work derived from the order.</strong> Billing
/// #42 refuses to post an invoice over garments that have been revised under it and invalidates a draft covering
/// them; Reporting #45 re-reads the garments named here. Both are told which garments moved by
/// <see cref="RevisedGarmentJobIds"/> rather than having to re-read the whole order to find out.
/// </para>
/// <para>
/// <strong>A revision is refused once a garment has entered production</strong> (INV-JOB-02), so this event never
/// contradicts a pinned workflow version — which is why it carries no workflow field at all.
/// </para>
/// <para>
/// <strong><see cref="RevisionId"/> is the "on whose authority" handle.</strong> It is exactly the role
/// <c>customers.customer-merged.v1</c> gives its merge identifier: a consumer asking why the order changed has
/// something to quote back, while the decision itself — who, when and why — stays inside Orders, where the audit
/// trail (ARCH-008) and the erasure workflow can reach it. The revision reason is free text a member of staff
/// typed and is never published.
/// </para>
/// <para>
/// <strong>No money.</strong> The revised totals and the three configuration version identifiers are Confidential
/// under <c>docs/nfr/data-classification.md</c> section 2.1 and no subscriber is approved for them; Billing reads
/// the priced result through <c>IOrderSnapshotQuery</c> and recalculates.
/// </para>
/// <para>
/// <strong>Whether the module also republishes <see cref="OrderConfirmed"/> alongside this event is an
/// implementation choice, not a contract question.</strong> Both are expressible, because
/// <see cref="OrderConfirmed.RevisionNumber"/> exists at v1 precisely so a republication needs no new major.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the revision was recorded, in UTC.</param>
/// <param name="AggregateId">
/// The order, so a revision is ordered against the confirmation it revises and against every later revision.
/// </param>
/// <param name="OrganisationId">The organisation the order belongs to.</param>
/// <param name="BranchId">The branch the order was taken at.</param>
/// <param name="CustomerId">The customer the order is for. An identifier and nothing more.</param>
/// <param name="OrderNumber">The human reference. Unchanged by a revision: the order is the same order.</param>
/// <param name="RevisionId">
/// The revision record this event announces. A handle a consumer can quote back; the reason it carries stays in
/// Orders.
/// </param>
/// <param name="RevisionNumber">
/// The order's revision number after this change. Two or higher — revision one is the confirmation itself.
/// </param>
/// <param name="DueDate">The order's promised date as at this revision, branch-local.</param>
/// <param name="SupersededEstimateId">
/// The outstanding estimate this revision supersedes, or null where there was none. It is the same fact a
/// separate estimate-superseded event would otherwise state twice.
/// </param>
/// <param name="RevisedGarmentJobIds">
/// Every garment the revision touched. Empty is possible — a revision may move only the order's own promised
/// date — and a consumer that holds nothing about a named garment does nothing.
/// </param>
public sealed record OrderRevised(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    string OrderNumber,
    Guid RevisionId,
    int RevisionNumber,
    DateOnly DueDate,
    Guid? SupersededEstimateId,
    IReadOnlyList<Guid> RevisedGarmentJobIds)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.order-revised.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
