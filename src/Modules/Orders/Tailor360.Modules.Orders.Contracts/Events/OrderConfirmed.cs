using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// A draft became a confirmed order.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the event the rest of the shop starts from.</strong> Custody allocates a barcode identity per
/// garment inside the same transaction (INV-ORD-01 and INV-BID-05, through the confirmation-participant hook
/// rather than through this event), Billing #42 converts an order to an invoice, Reporting #45 counts a new
/// order into the pipeline, and Notifications tells the customer her order is placed.
/// </para>
/// <para>
/// <strong><see cref="GarmentJobCount"/> is load-bearing.</strong> Confirmation also raises one
/// <see cref="GarmentJobCreated"/> per garment, and each of those carries the <em>job</em> as its aggregate — so
/// per-aggregate ordering (<c>docs/integration/events/README.md</c> section 4.1) orders each garment's own events
/// against each other and says nothing about the set being complete. A consumer that must assemble the whole
/// order counts the garment events it has received against this number rather than guessing when to stop
/// waiting.
/// </para>
/// <para>
/// <strong><see cref="RevisionNumber"/> is on the payload at v1 on purpose.</strong> Plan #32a's revision flow
/// republishes this event with the revision's number, and
/// <c>docs/architecture/conventions.md</c> section 5.5 permits only additive change within a major — so the
/// field that a republication needs is declared before the first subscriber binds, not added after one has.
/// </para>
/// <para>
/// <strong>No money, no notes, no priority, no actor.</strong> The price snapshot is Confidential
/// (<c>docs/nfr/data-classification.md</c> section 2.1) and section 3 admits Confidential data to an integration
/// event only for an approved subscriber, of which there is none: #42 reads the priced result and the
/// configuration versions through <c>IOrderSnapshotQuery</c> and recalculates. Notes are free text a member of
/// staff typed about a named person, which is the line <c>customers.customer-merged.v1</c> already takes about a
/// merge reason. Priority is named as a column in
/// <c>docs/architecture/module-ownership.md</c> section 5.5 but no document fixes its vocabulary, and inventing
/// one would be inventing a product decision.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the order was confirmed, in UTC.</param>
/// <param name="AggregateId">The order. Every order-scoped event that follows is ordered against it.</param>
/// <param name="OrganisationId">The organisation the order belongs to.</param>
/// <param name="BranchId">
/// The branch that took it. <c>Order.Confirm</c> refuses an empty branch, because branch scope is evaluated and
/// never inferred (CLAUDE.md security rule 2) and the branch is the sequence key the number came from.
/// </param>
/// <param name="CustomerId">
/// The customer the order is for. An identifier and nothing more — the aggregate holds no name, no telephone
/// number and no address. Reporting #45 counts new against repeat from it, and Billing #42 starts its bill-to
/// lookup here, through <c>ICustomerSnapshotQuery</c>.
/// </param>
/// <param name="OrderNumber">
/// The human reference, <c>O-&lt;branch&gt;-&lt;FY&gt;-000001</c> (<c>docs/architecture/conventions.md</c>
/// section 3.2). A label a person quotes, never a lookup key.
/// </param>
/// <param name="OrderDraftId">
/// The draft that was confirmed. It is how an idempotent confirmation recognises a retry: the same draft
/// confirmed twice is one order, not two.
/// </param>
/// <param name="EstimateId">
/// The estimate this order was converted from, or null where the counter confirmed without quoting first. #42
/// carries it as the invoice's source estimate.
/// </param>
/// <param name="DueDate">
/// The order's promised date, branch-local (<c>docs/architecture/conventions.md</c> section 2.2). A garment may
/// promise its own, which is why <see cref="GarmentJobCreated.DueDate"/> exists and is the one a workshop queue
/// is ordered by.
/// </param>
/// <param name="GarmentJobCount">
/// How many garments the order was confirmed with — the number of <see cref="GarmentJobCreated"/> events that
/// accompany this one. At least one: an order with no garment is refused.
/// </param>
/// <param name="RevisionNumber">
/// The revision this confirmation states the order at. One at first confirmation, and higher where the event is
/// republished after <see cref="OrderRevised"/>.
/// </param>
public sealed record OrderConfirmed(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    string OrderNumber,
    Guid OrderDraftId,
    Guid? EstimateId,
    DateOnly DueDate,
    int GarmentJobCount,
    int RevisionNumber)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.order-confirmed.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
