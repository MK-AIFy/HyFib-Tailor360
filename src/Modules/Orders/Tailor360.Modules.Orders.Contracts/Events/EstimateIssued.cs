using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Orders.Contracts.Events;

/// <summary>
/// A priced estimate was issued against an open draft.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An estimate is a quote, and this event says only that one now exists.</strong> It is never posted and
/// never consumes an invoice number — INV-ORD-04 in <c>docs/architecture/invariants.md</c> is what keeps the
/// <c>E-</c> series and Billing's invoice series apart — so a consumer that treated this as a receivable would be
/// treating a quotation as a debt.
/// </para>
/// <para>
/// <strong>There is no money on it, deliberately.</strong> A garment job's price snapshot is Confidential under
/// <c>docs/nfr/data-classification.md</c> section 2.1, and section 3's "Appears in integration events and
/// webhooks" row admits Confidential data "only to a subscriber approved for it". No such approval is recorded,
/// so no subtotal, no tax component, no grand total, and none of the catalogue, price-list or tax-configuration
/// version identifiers that only mean anything beside them. Plan #42 prefills an invoice draft from an estimate
/// by reading the priced result through a read contract that re-authorises, never from this payload.
/// </para>
/// <para>
/// <strong>And no document.</strong> The rendered artefact and its checksum stay in Orders; Billing owns
/// <c>documents/</c> (<c>docs/architecture/module-ownership.md</c> section 5.5). Nor is there an actor — the same
/// line <c>customers.customer-merged.v1</c> takes, where who decided, when and why stays inside the publishing
/// module and the audit trail (ARCH-008) is the authority on it.
/// </para>
/// <para>
/// <strong>There is no estimate-superseded and no estimate-converted event.</strong> A reissue is already
/// announced by <see cref="OrderRevised.SupersededEstimateId"/> and a conversion by
/// <see cref="OrderConfirmed.EstimateId"/>; publishing both would make one fact look like two, which is the
/// argument <c>docs/integration/events/README.md</c> section 3 already makes about
/// <c>catalog.catalog-version-retired.v1</c>.
/// </para>
/// </remarks>
/// <param name="EventId">Identity of this occurrence. Delivery is at least once; consumers de-duplicate on it.</param>
/// <param name="OccurredAt">When the estimate was issued, in UTC.</param>
/// <param name="AggregateId">
/// The estimate. It is the thing the event announces, so it is the thing ordering is preserved against — the same
/// choice <c>customers.measurement-version-confirmed.v1</c> makes with the version it confirms.
/// </param>
/// <param name="OrganisationId">The organisation the estimate belongs to.</param>
/// <param name="BranchId">
/// The branch that issued it, and whose sequence the number came from. Branch scope is evaluated and never
/// inferred, so <c>Estimate.Issue</c> refuses an empty one and this field is never absent.
/// </param>
/// <param name="CustomerId">
/// The customer it was quoted to. An identifier and nothing more: the aggregate holds no name, no telephone
/// number and no address (<c>docs/nfr/data-classification.md</c> section 5.2). A consumer that must name the
/// person asks <c>ICustomerSnapshotQuery</c>, which re-authorises the read and masks what the caller may not see.
/// </param>
/// <param name="OrderDraftId">
/// The draft that was priced. An estimate is a snapshot of a draft and not of an order, because there is no order
/// yet — and the draft is what the confirmation that may follow is keyed on.
/// </param>
/// <param name="EstimateNumber">
/// The human reference, <c>E-&lt;branch&gt;-&lt;FY&gt;-000001</c>. A display number under
/// <c>docs/architecture/conventions.md</c> section 3.2: it is what a person quotes back over the counter, and it
/// is never a lookup key — every reference is by <see cref="IIntegrationEvent.AggregateId"/>.
/// </param>
/// <param name="IssuedOn">The branch-local date it was issued on, already resolved in the branch timezone
/// (<c>docs/architecture/conventions.md</c> section 2.2).</param>
/// <param name="ValidUntil">
/// The branch-local date it stops being honourable. The one fact on an estimate that decays, and the reason a
/// consumer holding one has to re-read rather than assume it still stands.
/// </param>
public sealed record EstimateIssued(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    Guid OrderDraftId,
    string EstimateNumber,
    DateOnly IssuedOn,
    DateOnly ValidUntil)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "orders.estimate-issued.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
