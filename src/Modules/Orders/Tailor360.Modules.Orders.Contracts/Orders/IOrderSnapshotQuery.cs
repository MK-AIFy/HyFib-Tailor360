using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Orders.Contracts.Orders;

/// <summary>
/// An order and its garment jobs, as another module reads them. The only way <c>orders.*</c> leaves the module.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No module reads <c>orders.orders</c> or <c>orders.garment_jobs</c></strong>
/// (<c>docs/architecture/module-ownership.md</c> section 5.5), and three are nevertheless written against an
/// order. <strong>Custody</strong> reads job state for the delivery queue and the fail-closed dispatch scan
/// (section 5.6, issues #37 and #48). <strong>Billing</strong> converts an order into an invoice (#42) and
/// attributes a dispatch amount (#43), which is why the price snapshot is here and not on an event.
/// <strong>Reporting</strong> (#45) reconciles its pipeline, workload, turnaround and quality projections back to
/// the source jobs. This interface is the whole of what those three may see.
/// </para>
/// <para>
/// <strong>It carries no personal data at all</strong>, which is a design choice rather than a happy accident.
/// There is no customer snapshot, because the aggregate holds only a customer identifier and no name, telephone
/// number or address — a consumer that must name the person asks
/// <c>Tailor360.Modules.Customers.Contracts.Customers.ICustomerSnapshotQuery</c>, which re-authorises and masks
/// contact details there. There is no measurement snapshot either: measurements are Sensitive Personal
/// (<c>docs/nfr/data-classification.md</c> sections 5.4 and 5.7), and the workshop reads the sheet through the
/// job-card response view under the Customers measurement-sheet permission rather than across a module boundary.
/// And there is no free text a member of staff wrote — no notes, no hold reason, no cancellation reason, no
/// revision reason — only the configured reason codes.
/// </para>
/// <para>
/// <strong>The money is a read of its own.</strong> <see cref="PricedTotals"/> is Confidential
/// (<c>docs/nfr/data-classification.md</c> section 5.7), section 3 of that document requires a named,
/// branch-scoped permission for a Confidential field, and section 2.1 prefers projecting a class away to
/// restricting the whole screen — a job card shows the customer's name and job number and never her telephone
/// number, which is why a Tailor can be shown a job card at all. So the three state reads carry no amount
/// anywhere, and <see cref="GetPricedAsync"/> is the one method that answers with money. Custody's dispatch scan
/// and Reporting's reconciliation read a garment's state without ever receiving a subtotal, a discount or a
/// grand total, and neither is asked to be trusted to keep one off a workshop surface.
/// </para>
/// <para>
/// <strong>No pricing permission is invented here, and the signature already has room for one.</strong> Orders'
/// permission set runs read, intake, estimate, confirm, revise, cancel, hold, resume, reschedule,
/// start-production and the rest, and nothing in it separates reading an order from reading its money. Coining a
/// key would be deciding the permission matrix, which is <strong>OD-13</strong> in
/// <c>docs/prd/assumptions-and-open-decisions.md</c> — open, and shipped by issue #24 under a documented default
/// rather than settled. So <see cref="GetPricedAsync"/> takes the caller's permission keys today and reads none
/// of them, and <see cref="PricedOrderSnapshot.TotalsIncluded"/> is true on every answer.
/// <strong>The additive path is this.</strong> When OD-13 settles a pricing key, the module reads it here and a
/// caller that does not hold it is answered with <see cref="PricedOrderSnapshot.TotalsIncluded"/> false, no
/// <see cref="PricedOrderSnapshot.Totals"/> and no <see cref="PricedOrderSnapshot.JobTotals"/> — <strong>no
/// signature moves, no record gains or loses a member, and no consumer is recompiled</strong>, which is what
/// <c>docs/architecture/conventions.md</c> section 5.5 requires of a change inside a major version.
/// <c>ICustomerSnapshotQuery</c> is the precedent in both halves: the caller passes what it holds and the mask is
/// applied in the owning module rather than written four times by four consumers, and
/// <c>CustomerSnapshot.ContactIncluded</c> is how a masked answer is told apart from an empty one.
/// </para>
/// <para>
/// <strong>Billing #42 is not short of a pricing input, and that is by design.</strong> Billing owns pricing:
/// <c>IPricingService</c>, the price lists with their design, material and labour surcharges, the tax
/// configuration versions and <c>calculation_snapshots</c> — "the exact pricing result and the configuration
/// versions used" — are all in Billing's own schema (<c>docs/architecture/module-ownership.md</c> section 5.8),
/// and <c>docs/prd/glossary.md</c> records the price snapshot as produced by Billing and merely stored by
/// Orders. Billing also holds <strong>no project reference to Orders at all</strong> (ARCH-010, section 5.8), so
/// it could not re-derive a price from an Orders type if it wanted to.
/// <c>docs/architecture/sequences/invoice-and-payment.md</c> section 2 says it outright — "Billing calls nothing
/// in Orders here" — and has the draft built from the confirmation payload Billing already holds and from the
/// identifiers and priced lines supplied on the command, which the web host composes. What #42 needs from
/// Orders is therefore what <see cref="PricedTotals"/> carries and no more: the amounts Orders froze and the
/// three configuration versions they were frozen under, so that a recomputation under those same versions can be
/// compared against them and diverge as <c>billing.snapshot-mismatch</c>. If #42 finds it must rebuild a
/// <c>PricingRequest</c> from scratch rather than recompute under its own calculation snapshot, the line
/// components of the <c>PricingResult</c> (plan Section 6.2 note 1) are the documented addition, and they land
/// as a trailing member of <see cref="PricedOrderSnapshot"/> — additive, and needing no new major.
/// </para>
/// <para>
/// <strong>Reach is not checked here.</strong> An order belongs to exactly one branch, and
/// <see cref="OrderSnapshot.BranchId"/> is returned so the caller can evaluate its own scope against it; the
/// endpoint that authorised the caller has already taken that decision, and every identifier passed in is a
/// UUIDv7 rather than anything guessable (security rule 8).
/// </para>
/// <para>
/// <strong>Four methods, all by identity, and deliberately no branch-wide query.</strong> Reporting #45's
/// reconciliation wants an aggregate read over a cut-off and a filter that no document specifies; inventing its
/// shape here would be inventing reporting surface. It lands with #45 as an additive method, which is what
/// <c>docs/architecture/conventions.md</c> section 5.5 permits within a major version. The priced read is by
/// order identity alone, because #42 and #43 are the consumers that read money and both read an order; a priced
/// read by garment-job identity is additive in the same way, if one is ever asked for.
/// </para>
/// </remarks>
public interface IOrderSnapshotQuery
{
    /// <summary>One order and all of its garment jobs, without any amount.</summary>
    /// <remarks>
    /// The jobs travel with the order rather than being fetched separately, because every named consumer that
    /// asks about an order asks about its garments in the same breath — an invoice covers them, the delivery
    /// queue parcels them, a pipeline count sums them — and two reads of one aggregate can disagree with each
    /// other where one read cannot.
    /// </remarks>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot, or null when no order has that identity.</returns>
    Task<OrderSnapshot?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>One garment job by its own identity, without its order and without any amount.</summary>
    /// <remarks>
    /// What Custody asks. A scan names a garment and nothing else, so making the caller find the order first
    /// would mean a lookup it has no identifier for and a payload it does not need — and a dispatch scan has no
    /// business receiving a price.
    /// </remarks>
    /// <param name="garmentJobId">The garment job.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot, or null when no garment job has that identity.</returns>
    Task<GarmentJobSnapshot?> GetJobAsync(Guid garmentJobId, CancellationToken cancellationToken = default);

    /// <summary>Several garment jobs at once, by their own identities, and without any amount.</summary>
    /// <remarks>
    /// <para>
    /// This exists for the door. A <c>deliver_together</c> parcel is checked at handover rather than trusted from
    /// a stored copy — <strong>SQ-07</strong>, <strong>SQ-08</strong> and <strong>SQ-09</strong> are all proposed
    /// and unconfirmed (<c>docs/prd/state-transitions.md</c> section 10) — so #37's dispatch scan and #48's
    /// delivery queue re-read the whole parcel at the moment it matters. One round trip per garment would make
    /// that check slow exactly where it must not be.
    /// </para>
    /// <para>
    /// Identifiers that name nothing are simply absent from the answer; the result is not padded with nulls and
    /// is in no guaranteed order, so a caller matching them up keys on
    /// <see cref="GarmentJobSnapshot.GarmentJobId"/>.
    /// </para>
    /// </remarks>
    /// <param name="garmentJobIds">The garment jobs to ask about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshots that exist, which may be fewer than were asked for.</returns>
    Task<IReadOnlyList<GarmentJobSnapshot>> GetJobsAsync(
        IReadOnlyCollection<Guid> garmentJobIds,
        CancellationToken cancellationToken = default);

    /// <summary>One order, its garment jobs and the price snapshots — the read that answers with money.</summary>
    /// <remarks>
    /// <para>
    /// Billing #42 converts an order into an invoice and #43 attributes a dispatch amount; both need the amounts
    /// Orders froze and the configuration versions they were frozen under. The state comes back with them, in
    /// <see cref="PricedOrderSnapshot.Order"/>, so that eligibility and money are judged from one read of one
    /// aggregate rather than from two reads that can disagree.
    /// </para>
    /// <para>
    /// <strong>The caller must not put the amounts on a workshop surface</strong> — a job card cannot carry a
    /// price, which the response-view catalogue enforces at build time, and
    /// <c>docs/nfr/data-classification.md</c> section 5.7 keeps the price snapshot out of logs as well.
    /// </para>
    /// </remarks>
    /// <param name="orderId">The order.</param>
    /// <param name="callerPermissions">
    /// The permission keys the caller holds. <strong>None is read today</strong>, because no key in the Orders
    /// catalogue separates reading an order from reading its money and coining one would decide OD-13 — see the
    /// interface's remarks for the whole of the additive path. It is taken now so that the mask, when the
    /// decision lands, changes behaviour inside this module and nowhere else. A caller may pass its whole set
    /// without filtering it first.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The priced snapshot, or null when no order has that identity.</returns>
    Task<PricedOrderSnapshot?> GetPricedAsync(
        Guid orderId,
        IReadOnlyCollection<string> callerPermissions,
        CancellationToken cancellationToken = default);
}

/// <summary>One confirmed order as another module reads it.</summary>
/// <remarks>
/// There is no snapshot of a draft. An order row exists only from confirmation onwards — the work in progress at
/// the counter is an order draft, which belongs to Orders and crosses no boundary — so every instance of this
/// record describes a commitment that was actually made. It carries no amount: money is
/// <see cref="IOrderSnapshotQuery.GetPricedAsync"/>.
/// </remarks>
/// <param name="OrderId">The order. A UUIDv7, and the identifier every consumer keys on.</param>
/// <param name="OrderNumber">
/// The display number, <c>O-&lt;branch&gt;-&lt;year&gt;-000001</c>. The human reference for a commercial document
/// (<c>docs/architecture/conventions.md</c> section 3.2) and never a lookup key.
/// </param>
/// <param name="OrganisationId">The organisation the order belongs to.</param>
/// <param name="BranchId">The branch that took it. What a caller evaluates its own reach against.</param>
/// <param name="CustomerId">
/// The customer, as an identifier and nothing else. A consumer that must name the person asks
/// <c>ICustomerSnapshotQuery</c>.
/// </param>
/// <param name="OrderDraftId">The draft that was confirmed. How an idempotent re-confirmation is recognised.</param>
/// <param name="EstimateId">
/// The estimate this order was converted from, or null when it was confirmed without one. Billing #42 records it
/// as the invoice's source estimate.
/// </param>
/// <param name="State">Where the order stands in its lifecycle.</param>
/// <param name="RevisionNumber">
/// Which revision this is. One at confirmation, and incremented by each revision taken before production starts.
/// </param>
/// <param name="DueDate">
/// The order's promised date, already evaluated in the branch timezone against the branch working calendar
/// (<c>docs/architecture/conventions.md</c> section 2.2). A per-garment promise may differ — see
/// <see cref="GarmentJobSnapshot.DueDate"/>.
/// </param>
/// <param name="ConfirmedAt">When the commitment was made, in UTC.</param>
/// <param name="ProductionStartedAt">When the first garment entered production, or null before any has.</param>
/// <param name="DeliveredAt">When the last non-cancelled garment was handed over, or null.</param>
/// <param name="CancelledAt">When the order was cancelled, or null.</param>
/// <param name="CancellationReasonCode">
/// The configured cancellation reason code, or null while the order stands. A code and never the free-text reason
/// beside it: <c>docs/nfr/data-classification.md</c> section 5.7 keeps what a member of staff wrote about a named
/// person inside the module, and the code is what a consumer can group and count by. The vocabulary is branch
/// configuration, reviewed under <strong>OD-10</strong> and seeded by issue #34, so nothing here constrains its
/// shape.
/// </param>
/// <param name="Jobs">
/// Every garment job of the order, cancelled ones included, in job-index order. A cancelled garment is never
/// removed, because an invoice line, a stock reservation and a custody chain all still name it.
/// </param>
public sealed record OrderSnapshot(
    Guid OrderId,
    string OrderNumber,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    Guid OrderDraftId,
    Guid? EstimateId,
    OrderState State,
    int RevisionNumber,
    DateOnly DueDate,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset? ProductionStartedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReasonCode,
    IReadOnlyList<GarmentJobSnapshot> Jobs);

/// <summary>One order, its garment jobs and the amounts — the answer to the read that carries money.</summary>
/// <remarks>
/// The state and the money arrive together because Billing judges both at once: #42 checks that the order is
/// confirmed and not already invoiced, and prices the invoice from the same answer. The amounts are separable
/// from the state and the state is not separable from them, which is why this record wraps an
/// <see cref="OrderSnapshot"/> rather than repeating it.
/// </remarks>
/// <param name="Order">
/// The order and its garment jobs, exactly as <see cref="IOrderSnapshotQuery.GetAsync"/> would have answered.
/// </param>
/// <param name="TotalsIncluded">
/// Whether the amounts below were populated. <strong>True on every answer today</strong>, because no key in the
/// Orders permission catalogue separates reading an order from reading its money; it is declared now so that the
/// mask OD-13 unblocks can be applied inside this module without a consumer changing. False would mean the caller
/// was masked, which is a different thing from an order having no price — and the difference matters for the same
/// reason it does on <c>CustomerSnapshot.ContactIncluded</c>: an invoice raised at nothing because the caller was
/// masked is a defect, while one raised at nothing because that is the price is a fact.
/// </param>
/// <param name="Totals">
/// The order's price snapshot, or null when <paramref name="TotalsIncluded"/> is false. Confidential — read the
/// interface's remarks before putting it on a screen.
/// </param>
/// <param name="JobTotals">
/// The price snapshot of each garment job, in job-index order, cancelled garments included. Empty when
/// <paramref name="TotalsIncluded"/> is false.
/// </param>
public sealed record PricedOrderSnapshot(
    OrderSnapshot Order,
    bool TotalsIncluded,
    PricedTotals? Totals,
    IReadOnlyList<GarmentJobPricedTotals> JobTotals);

/// <summary>One garment job's price snapshot, beside the garment it belongs to.</summary>
/// <remarks>
/// Keyed rather than positional: a consumer matching amounts to garments joins on
/// <see cref="GarmentJobSnapshot.GarmentJobId"/>, which is what an invoice line already carries (plan #42's
/// <c>invoice_lines.garment_job_id</c>), so neither side depends on two lists staying in step.
/// </remarks>
/// <param name="GarmentJobId">The garment job these amounts priced.</param>
/// <param name="Totals">Its own price snapshot.</param>
public sealed record GarmentJobPricedTotals(Guid GarmentJobId, PricedTotals Totals);

/// <summary>What an order or a garment job was priced at, and the configuration that priced it.</summary>
/// <remarks>
/// <para>
/// <strong>INV-ORD-02 crosses the boundary with the amounts.</strong> Exactly one catalogue version, one
/// price-list version and one tax configuration version are named, so every figure can be recomputed from its own
/// snapshot — which is precisely what Billing #42 does when it converts an order into an invoice, and what #43
/// does when it works out an advance's share. A total that arrived without the versions it was calculated under
/// would be a figure nobody could check.
/// </para>
/// <para>
/// <strong>These totals are for display and printing (INV-ORD-07).</strong> The authoritative money position is
/// Billing's own; Orders performs no money arithmetic beyond adding the tax components it was handed. A consumer
/// that needs what is owed asks Billing, not this.
/// </para>
/// <para>
/// <strong>Confidential.</strong> <c>docs/nfr/data-classification.md</c> section 5.7 classes the price snapshot
/// so; section 3 admits Confidential to an integration event only for a subscriber approved for it, and no such
/// approval is recorded — which is why not one of the eleven Orders events carries an amount, why this contract
/// does, and why it does so on a read of its own. It must reach neither a workshop surface nor a log.
/// </para>
/// </remarks>
/// <param name="CatalogVersionId">The published catalogue version the priced lines were resolved against.</param>
/// <param name="PriceListVersionId">The price-list version the rates came from.</param>
/// <param name="TaxConfigurationVersionId">The tax configuration version in force at the calculation.</param>
/// <param name="Subtotal">Line amounts before discount.</param>
/// <param name="DiscountTotal">The discount given, as a positive amount.</param>
/// <param name="TaxableValue">What tax was computed on.</param>
/// <param name="CentralTax">CGST. Zero on an inter-state supply.</param>
/// <param name="StateTax">SGST. Zero on an inter-state supply.</param>
/// <param name="IntegratedTax">IGST. Zero on an intra-state supply.</param>
/// <param name="Cess">Cess, where the tax code carries one.</param>
/// <param name="RoundOff">
/// The document round-off, shown and never absorbed. The one amount that may be negative
/// (<c>docs/architecture/conventions.md</c> section 1.2).
/// </param>
/// <param name="GrandTotal">What the document asks for.</param>
/// <param name="CalculatedAt">When the calculation was made, in UTC.</param>
public sealed record PricedTotals(
    Guid CatalogVersionId,
    Guid PriceListVersionId,
    Guid TaxConfigurationVersionId,
    Money Subtotal,
    Money DiscountTotal,
    Money TaxableValue,
    Money CentralTax,
    Money StateTax,
    Money IntegratedTax,
    Money Cess,
    Money RoundOff,
    Money GrandTotal,
    DateTimeOffset CalculatedAt);

/// <summary>Where an order stands in its lifecycle, as another module sees it.</summary>
/// <remarks>
/// <para>
/// A published mirror of the module's own <c>OrderStatus</c>: same ordinals, same member names, one for one. The
/// type is nonetheless its own, following <c>ConsentStatus</c> beside <c>ConsentDecision</c> in Customers — a
/// <c>Contracts</c> project references <c>Platform.Abstractions</c> and nothing else, so a domain enumeration
/// cannot cross the boundary however tempting it is to let it.
/// </para>
/// <para>
/// <strong>Two members are declared and are unreachable today, and both are honest about it.</strong>
/// <see cref="Draft"/> is a lifecycle position held by an order draft rather than by an order, so no
/// <see cref="OrderSnapshot"/> ever carries it. <see cref="Closed"/> has no trigger at all:
/// <strong>SQ-01</strong> is open (<c>docs/prd/state-transitions.md</c> section 10) and until it is decided,
/// delivered is the last automatic state. Omitting either would be a lie about what the column can hold, and
/// adding one back later would be a change to a published set.
/// </para>
/// <para>
/// How job states aggregate into this one is <strong>SQ-02</strong>, which is proposed and not confirmed. The
/// interim reading is encoded in the domain, so a change to SQ-02 is a change there first and a change here
/// second, and not a breaking change to these members.
/// </para>
/// </remarks>
public enum OrderState
{
    /// <summary>
    /// Work in progress at the counter. Never carried by a snapshot; an order row exists from confirmation.
    /// </summary>
    Draft = 0,

    /// <summary>The commitment is made: snapshots frozen, display numbers allocated. Irreversible.</summary>
    Confirmed = 1,

    /// <summary>At least one garment job has entered production. Revision is refused from here (INV-ORD-05).</summary>
    InProduction = 2,

    /// <summary>The job set the branch dispatch policy requires has passed the ready-for-delivery gate.</summary>
    Ready = 3,

    /// <summary>Every non-cancelled garment job has been handed over at the door.</summary>
    Delivered = 4,

    /// <summary>Finished and out of the working set. Declared, and unreachable while SQ-01 is open.</summary>
    Closed = 5,

    /// <summary>Cancelled by an explicit, reasoned, separately authorised command. There is no un-cancel.</summary>
    Cancelled = 6,
}
