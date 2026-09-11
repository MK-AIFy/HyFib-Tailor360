using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// One garment of a confirmed order came into being.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One of these per garment, raised inside the same confirmation as
/// <see cref="OrderConfirmed"/>.</strong> That is what makes INV-ORD-01 expressible: the order, every garment
/// job, every snapshot, every barcode identity and every outbox row commit together or not at all, so a consumer
/// never sees a garment for an order that rolled back.
/// </para>
/// <para>
/// <strong>The aggregate is the garment, not the order, and that is the whole design.</strong> Every later
/// job-scoped event in this module — production, hold, resume, reschedule, ready, cancellation — uses the same
/// aggregate, so the outbox orders each garment's life against itself
/// (<c>docs/integration/events/README.md</c> section 4.1). A consumer that needs the order's shape counts
/// against <see cref="OrderConfirmed.GarmentJobCount"/> instead, because ordering across aggregates promises
/// nothing.
/// </para>
/// <para>
/// <strong>No customer identifier.</strong> A garment event does not need one — Custody's barcode payload
/// carries no personal data at all, and Reporting joins through <see cref="OrderId"/> — and
/// <c>docs/nfr/data-classification.md</c> section 3.1 prefers projecting a personal reference away wherever a
/// consumer can do without it.
/// </para>
/// <para>
/// <strong>No measurement, in any form.</strong> Not the values, not the template, not the version, not a count
/// of them: section 5.4's rule for exports is flat — never in a report, an analytical export or an integration
/// event payload. A consumer printing a measurement sheet reads it inside the module's own job-card view under
/// the Customers permission that guards it, not across a module boundary.
/// </para>
/// <para>
/// <strong>No design selections, no garment instructions, no reference media, no price.</strong> The first two
/// are free text and bulk nobody asked for — the labels a printer needs are on <c>IOrderSnapshotQuery</c>
/// instead; media identifiers are refused by section 5.5 and by rule 3.1.3 (media is never given a URL), because
/// an identifier broadcast to every registered handler is the first half of a link nobody re-authorised; and the
/// price snapshot is Confidential.
/// </para>
/// <para>
/// <strong>No dependencies.</strong> Whether a <c>deliver_together</c> parcel is the transitive closure of the
/// relation (SQ-07) and whether a cancelled or delivered garment leaves it (SQ-08) are both "proposed, to be
/// confirmed" in <c>docs/prd/state-transitions.md</c> section 10. A frozen copy of parcel membership on a v1
/// contract would publish an undecided product rule; a consumer asks <c>IOrderSnapshotQuery</c>, which answers
/// from the rule in force when it is asked.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the order was confirmed, in UTC. The same instant for every garment of it.</param>
/// <param name="AggregateId">
/// The garment job. Ordering is preserved against this identifier, so a consumer sees this event before every
/// other event about the same garment.
/// </param>
/// <param name="OrganisationId">The organisation the garment belongs to.</param>
/// <param name="BranchId">The branch the order was taken at, and the workshop the garment is made in.</param>
/// <param name="OrderId">The order this garment is part of.</param>
/// <param name="GarmentJobNumber">
/// The human reference, <c>J-&lt;branch&gt;-&lt;FY&gt;-000001-01</c> (<c>docs/architecture/conventions.md</c>
/// section 3.2). What a tailor reads off a rack tag, and never a lookup key.
/// </param>
/// <param name="JobIndex">The garment's one-based position within its order.</param>
/// <param name="CategoryKey">
/// The stitching category, by its stable key. Reporting #45 projects the pipeline and the category mix from it,
/// which is why the key travels and the label does not: a label is display text that may be re-worded, and
/// <c>IOrderSnapshotQuery</c> carries it for anything that prints.
/// </param>
/// <param name="ServiceTypeKey">The service type within that category, by its stable key.</param>
/// <param name="WorkflowDefinitionId">
/// The workflow this garment will be made under. Confirmation records the <em>definition</em> and instantiates
/// nothing; the version is resolved and pinned at start-production (INV-JOB-02), which is what
/// <see cref="GarmentJobEnteredProduction.WorkflowVersionId"/> announces.
/// </param>
/// <param name="DueDate">
/// The garment's own promised date, branch-local (<c>docs/architecture/conventions.md</c> section 2.2). It is the
/// promise a workshop queue is ordered by and the one #45 measures promised-against-actual turnaround from.
/// </param>
public sealed record GarmentJobCreated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid OrderId,
    string GarmentJobNumber,
    int JobIndex,
    string CategoryKey,
    string ServiceTypeKey,
    Guid WorkflowDefinitionId,
    DateOnly DueDate)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.garment-job-created.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
