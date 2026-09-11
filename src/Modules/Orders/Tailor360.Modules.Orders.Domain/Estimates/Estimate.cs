using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Estimates;

/// <summary>
/// A priced, shareable snapshot of a draft.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is not a stage of the order and it is not a tax document.</strong> An estimate is never posted
/// and never consumes an invoice number: its series is <c>E-</c> and the invoice series is Billing's, and
/// INV-ORD-04 is what keeps the two apart. The rendered document says "Estimate — not a tax invoice" on its
/// face for the same reason.
/// </para>
/// <para>
/// <strong>A reissue supersedes rather than edits.</strong> The customer may already be holding the first
/// one — it was shared through an expiring customer link — so the price it quoted has to stay readable
/// exactly as it was quoted. <see cref="Supersede"/> retires the old estimate and a new one is issued beside
/// it, and the superseded document keeps the checksum it was stored with
/// (<c>docs/prd/state-transitions.md</c> section 2.2).
/// </para>
/// <para>
/// <strong>Measurements are not required to issue one</strong>, and nothing here asks for them. Design
/// selections must pass the configured rules and pricing must be available; being measured is a condition of
/// confirming, not of quoting (<c>docs/prd/state-transitions.md</c> section 2.1).
/// </para>
/// </remarks>
public sealed class Estimate
{
    /// <summary>
    /// The longest checksum the column holds. A hexadecimal SHA-256 digest is 64 characters; the column is
    /// sized for a longer algorithm name and digest so that changing the hash is not a migration.
    /// </summary>
    public const int MaximumChecksumLength = 128;

    private Estimate()
    {
        // The persistence layer materialises instances through this constructor.
        EstimateNumber = null!;
        Totals = null!;
    }

    private Estimate(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid orderDraftId,
        Guid customerId,
        EstimateNumber estimateNumber,
        PriceSnapshot totals,
        DateOnly issuedOn,
        DateOnly validUntil,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        OrderDraftId = orderDraftId;
        CustomerId = customerId;
        EstimateNumber = estimateNumber;
        Totals = totals;
        IssuedOn = issuedOn;
        ValidUntil = validUntil;
        Status = EstimateStatus.Issued;
        IssuedAt = now;
        IssuedBy = by;
    }

    /// <summary>
    /// Identity of this estimate. A UUIDv7, and what a customer link resolves to — never the estimate number,
    /// which is a display number and never a lookup key on a customer-facing surface
    /// (<c>docs/architecture/conventions.md</c> section 3.2 rule 1).
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the estimate belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch that issued it, and whose sequence the number came from.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>
    /// The draft this prices. An estimate is a snapshot of a draft, not of an order: there is no order yet.
    /// </summary>
    public Guid OrderDraftId { get; private set; }

    /// <summary>The customer it was quoted to. An identifier and nothing more.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>
    /// <c>E-&lt;branch&gt;-&lt;FY&gt;-000001</c>, from the estimate series and never the invoice series
    /// (INV-ORD-04).
    /// </summary>
    public EstimateNumber EstimateNumber { get; private set; }

    /// <summary>
    /// The priced result and the configuration versions it used.
    /// </summary>
    /// <remarks>
    /// A snapshot for display and printing. Billing holds the authoritative money position through
    /// <c>IFinancialTotalsQuery</c> (INV-ORD-07), and this module performs no money arithmetic at all: it
    /// stores what Billing returned.
    /// </remarks>
    public PriceSnapshot Totals { get; private set; }

    /// <summary>The branch-local date it was issued on.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>
    /// The branch-local date it stops being honourable.
    /// </summary>
    /// <remarks>
    /// No default window is documented anywhere, so the caller supplies the date rather than the domain
    /// inventing one: how long a quote stands is a product decision, and an unsourced number here would be a
    /// change to the product.
    /// </remarks>
    public DateOnly ValidUntil { get; private set; }

    /// <summary>Where the estimate stands in its sub-lifecycle.</summary>
    public EstimateStatus Status { get; private set; }

    /// <summary>When it was issued, in UTC.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>Who issued it.</summary>
    public Guid? IssuedBy { get; private set; }

    /// <summary>When it was superseded, or null.</summary>
    public DateTimeOffset? SupersededAt { get; private set; }

    /// <summary>Who superseded it.</summary>
    public Guid? SupersededBy { get; private set; }

    /// <summary>The estimate that replaced this one.</summary>
    public Guid? SupersededByEstimateId { get; private set; }

    /// <summary>When the draft was confirmed into an order, or null.</summary>
    public DateTimeOffset? ConvertedAt { get; private set; }

    /// <summary>Who confirmed it.</summary>
    public Guid? ConvertedBy { get; private set; }

    /// <summary>The order the draft was confirmed into.</summary>
    public Guid? ConvertedToOrderId { get; private set; }

    /// <summary>
    /// The checksum of the rendered document, recorded once.
    /// </summary>
    /// <remarks>
    /// Rendering happens outside the issuing transaction, so the checksum arrives later. Once recorded it can
    /// never be changed, which is what lets a superseded estimate still be verified against the document the
    /// customer was actually given (<c>docs/prd/state-transitions.md</c> section 2.2).
    /// </remarks>
    public string? ArtefactChecksum { get; private set; }

    /// <summary>When the checksum was recorded, in UTC.</summary>
    public DateTimeOffset? ArtefactRecordedAt { get; private set; }

    /// <summary>Issues a priced estimate against an open draft.</summary>
    /// <param name="id">Identity of the estimate, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchId">The branch issuing it.</param>
    /// <param name="orderDraftId">The draft being priced.</param>
    /// <param name="customerId">The customer it is quoted to.</param>
    /// <param name="estimateNumber">
    /// The allocated display number. Allocation belongs to the application layer, which holds
    /// <c>ISequenceAllocator</c> and the branch code; the aggregate only checks it was given one.
    /// </param>
    /// <param name="totals">The priced result and the configuration versions it used.</param>
    /// <param name="issuedOn">The branch-local date it is issued on.</param>
    /// <param name="validUntil">The branch-local date it stops being honourable.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>The estimate, or the reason it could not be issued.</returns>
    public static Result<Estimate> Issue(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid orderDraftId,
        Guid customerId,
        EstimateNumber estimateNumber,
        PriceSnapshot totals,
        DateOnly issuedOn,
        DateOnly validUntil,
        DateTimeOffset now,
        Guid? by = null)
    {
        ArgumentNullException.ThrowIfNull(estimateNumber);
        ArgumentNullException.ThrowIfNull(totals);

        if (id == Guid.Empty)
        {
            return Result.Failure<Estimate>(OrdersErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<Estimate>(OrdersErrors.Required("organisationId"));
        }

        // Branch scope is evaluated, never inferred (CLAUDE.md security rule 2). The branch is also the
        // sequence key the number came from, so an estimate with none could not be numbered honestly.
        if (branchId == Guid.Empty)
        {
            return Result.Failure<Estimate>(OrdersErrors.NoBranchInContext);
        }

        if (orderDraftId == Guid.Empty)
        {
            return Result.Failure<Estimate>(OrdersErrors.Required("orderDraftId"));
        }

        if (customerId == Guid.Empty)
        {
            return Result.Failure<Estimate>(OrdersErrors.Required("customerId"));
        }

        // A validity date before the issue date is a quote that was never honourable. Caught here because
        // both dates are branch-local and the caller has already resolved the branch timezone; the domain
        // compares them and never asks a clock what today is (ARCH-014).
        if (validUntil < issuedOn)
        {
            return Result.Failure<Estimate>(OrdersErrors.EstimateValidityBeforeIssue);
        }

        return Result.Success(new Estimate(
            id,
            organisationId,
            branchId,
            orderDraftId,
            customerId,
            estimateNumber,
            totals,
            issuedOn,
            validUntil,
            now,
            by));
    }

    /// <summary>
    /// Retires this estimate because a newer one has been issued for the same draft.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guards are ordered, and the order is the message. Naming itself comes first, because it is a
    /// caller defect and says nothing about the estimate; then the two ways the estimate may already be spent,
    /// each with its own answer, so that a second reissue says "already superseded" rather than the vaguer
    /// "not outstanding".
    /// </para>
    /// <para>
    /// A converted estimate is never superseded: the draft it priced has become an order, and rewriting the
    /// history of the quote the customer accepted is exactly what INV-ORD-04 and the append-only reading of
    /// this lifecycle are there to prevent.
    /// </para>
    /// </remarks>
    /// <param name="supersedingEstimateId">The estimate that replaces this one.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Supersede(Guid supersedingEstimateId, DateTimeOffset now, Guid? by)
    {
        if (supersedingEstimateId == Guid.Empty)
        {
            return Result.Failure(OrdersErrors.Required("supersedingEstimateId"));
        }

        if (supersedingEstimateId == Id)
        {
            return Result.Failure(OrdersErrors.EstimateCannotSupersedeItself);
        }

        if (NotOutstanding() is { } refusal)
        {
            return refusal;
        }

        Status = EstimateStatus.Superseded;
        SupersededAt = now;
        SupersededBy = by;
        SupersededByEstimateId = supersedingEstimateId;

        return Result.Success();
    }

    /// <summary>Records that the draft this estimate prices was confirmed into an order.</summary>
    /// <remarks>
    /// <para>
    /// The transition is defined relative to the draft: state-transitions.md section 2.2 reads
    /// "<c>issued</c> | Order confirmed <strong>from the draft</strong> | <c>converted</c>". So the
    /// confirmation says which draft it consumed and that is checked against
    /// <see cref="OrderDraftId"/> rather than trusted, because the aggregate already holds the answer.
    /// </para>
    /// <para>
    /// Recording the conversion against an order made from a different draft would spend this estimate on
    /// somebody else's order and — since <see cref="Supersede"/> refuses a converted estimate for ever —
    /// strand the quote that is still genuinely outstanding for this one, with no command able to retire
    /// it.
    /// </para>
    /// </remarks>
    /// <param name="orderId">The order the draft became.</param>
    /// <param name="orderDraftId">The draft that confirmation consumed, which must be the one this prices.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Convert(Guid orderId, Guid orderDraftId, DateTimeOffset now, Guid? by)
    {
        if (orderId == Guid.Empty)
        {
            return Result.Failure(OrdersErrors.Required("orderId"));
        }

        if (orderDraftId == Guid.Empty)
        {
            return Result.Failure(OrdersErrors.Required("orderDraftId"));
        }

        if (orderDraftId != OrderDraftId)
        {
            return Result.Failure(OrdersErrors.EstimateNotForThisDraft);
        }

        if (NotOutstanding() is { } refusal)
        {
            return refusal;
        }

        Status = EstimateStatus.Converted;
        ConvertedAt = now;
        ConvertedBy = by;
        ConvertedToOrderId = orderId;

        return Result.Success();
    }

    /// <summary>
    /// Records the rendered document's checksum.
    /// </summary>
    /// <remarks>
    /// Deliberately not guarded by status. The document is rendered outside the issuing transaction, and a
    /// render that completes after a reissue must still be able to record what it produced — otherwise the
    /// superseded document could never be verified. What is guarded is a second checksum: once one is held it
    /// stands, so a superseded estimate keeps the checksum it was stored with.
    /// </remarks>
    /// <param name="checksum">The digest of the rendered document.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result RecordArtefact(string? checksum, DateTimeOffset now)
    {
        if (ArtefactChecksum is not null)
        {
            return Result.Failure(OrdersErrors.EstimateArtefactAlreadyRecorded);
        }

        var digest = checksum?.Trim();
        if (string.IsNullOrEmpty(digest))
        {
            return Result.Failure(OrdersErrors.Required("checksum"));
        }

        if (digest.Length > MaximumChecksumLength)
        {
            return Result.Failure(OrdersErrors.TooLong("checksum", MaximumChecksumLength));
        }

        ArtefactChecksum = digest;
        ArtefactRecordedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Whether the estimate is still within its validity on a branch-local date.
    /// </summary>
    /// <remarks>
    /// The dates alone, and deliberately: expiry is derived and never a stored status, and whether the
    /// estimate is also still outstanding is <see cref="Status"/>. The caller resolves the branch timezone
    /// before asking (<c>docs/architecture/conventions.md</c> section 2.2); this type never touches a clock
    /// (ARCH-014).
    /// </remarks>
    /// <param name="branchLocalDate">The branch-local date being asked about.</param>
    /// <returns>True while the date falls inside the validity window.</returns>
    public bool IsValidOn(DateOnly branchLocalDate)
        => branchLocalDate >= IssuedOn && branchLocalDate <= ValidUntil;

    private Result? NotOutstanding()
        => Status switch
        {
            EstimateStatus.Issued => null,
            EstimateStatus.Superseded => Result.Failure(OrdersErrors.EstimateAlreadySuperseded),
            EstimateStatus.Converted => Result.Failure(OrdersErrors.EstimateAlreadyConverted),

            // Unreachable today, and kept so that adding a fourth status is a compiler-guided change rather
            // than a silent one that lets a command through from a state nobody considered.
            _ => Result.Failure(OrdersErrors.EstimateNotIssued),
        };
}
