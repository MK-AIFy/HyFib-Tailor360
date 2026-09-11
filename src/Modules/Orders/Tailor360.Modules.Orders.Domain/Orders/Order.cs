using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Orders;

/// <summary>
/// The confirmed commercial commitment, and the aggregate root over its garment jobs and revisions.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An order exists only from confirmation.</strong> Before it there is an <c>OrderDraft</c>,
/// which is not an obligation and may expire to nothing. <see cref="Confirm"/> is therefore the single
/// factory and there is no other way an instance comes into being — which is what makes INV-ORD-01
/// expressible in the domain at all: every garment job, every snapshot and revision one are built in one
/// construction, so there is no partly built order for a transaction to persist.
/// </para>
/// <para>
/// <strong>Every garment job transition goes through this type.</strong>
/// <see cref="Jobs.GarmentJob"/>'s factory and all of its mutators are <c>internal</c>, so the only route
/// to a job transition is the matching public command here, and each of those commands recomputes the
/// order's derived status before it returns. That is what makes the SQ-02 aggregation impossible to skip:
/// a caller cannot move a job and forget the order. It is also what keeps the ready-for-delivery gate the
/// sole writer of ready state — <see cref="ApplyReadyGate"/> accepts nothing but a
/// <see cref="ReadyGateOutcome"/>, which only <see cref="ReadyGate"/> can produce (INV-JOB-07,
/// <c>docs/prd/raci.md</c> row 16).
/// </para>
/// <para>
/// <strong>The order holds identifiers, not people.</strong> <see cref="CustomerId"/> is the customer:
/// no name, no telephone number, no address anywhere on this aggregate
/// (<c>docs/nfr/data-classification.md</c> section 5.2). The customer snapshot that
/// <c>docs/architecture/module-ownership.md</c> section 5.5 lists on the <c>orders</c> table is a
/// deliberate deferral to the increment that consumes <c>ICustomerSnapshotQuery</c>, not an oversight.
/// </para>
/// <para>
/// <strong>Priority is deliberately absent.</strong>
/// <c>docs/architecture/module-ownership.md</c> section 5.5, <c>docs/security/field-visibility.md</c>,
/// <c>docs/architecture/sequences/order-confirmation.md</c> (which carries it on
/// <c>orders.garment-job-created.v1</c>) and plan #32a all name a priority on the order and on the
/// garment job, and <em>no</em> document states its vocabulary — no levels, no rush flag, no default. It
/// is a product decision and is not invented here; adding it later is an additive migration and an
/// additive property. <strong>It is not yet registered in
/// <c>docs/prd/assumptions-and-open-decisions.md</c></strong>, which is where CLAUDE.md section 8 says an
/// undecided question belongs, and a remark in a source file is not that entry: without it the omission
/// surfaces as a hole in a published event payload rather than as the product question it is.
/// </para>
/// <para>
/// <strong>There is no delete.</strong> Cancellation sets a status and a reason and removes nothing
/// (G-5, INV-ORD-06).
/// </para>
/// </remarks>
public sealed class Order
{
    /// <summary>
    /// The longest reason the column holds. Matches <c>CustomerExport.MaximumReasonLength</c>, the
    /// repository precedent for a free-text reason on a state change.
    /// </summary>
    public const int MaximumReasonLength = 500;

    /// <summary>
    /// The longest reason code the column holds. Matches Catalog's <c>CatalogCode.MaximumLength</c>: a
    /// cancellation reason code is configuration, not prose.
    /// </summary>
    public const int MaximumReasonCodeLength = 40;

    /// <summary>The longest order-level note the column holds.</summary>
    public const int MaximumNotesLength = 2000;

    /// <summary>The revision number a confirmation writes.</summary>
    public const int FirstRevisionNumber = 1;

    private readonly List<GarmentJob> _jobs = [];
    private readonly List<OrderRevision> _revisions = [];

    private Order()
    {
        // The persistence layer materialises instances through this constructor.
        OrderNumber = null!;
        Totals = null!;
    }

    private Order(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        OrderNumber orderNumber,
        Guid orderDraftId,
        Guid? estimateId,
        DateOnly dueDate,
        string? notes,
        PriceSnapshot totals,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CustomerId = customerId;
        OrderNumber = orderNumber;
        OrderDraftId = orderDraftId;
        EstimateId = estimateId;
        DueDate = dueDate;
        Notes = notes;
        Totals = totals;
        Status = OrderStatus.Confirmed;
        RevisionNumber = FirstRevisionNumber;
        ConfirmedAt = now;
        ConfirmedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>
    /// Identity of the order. A UUIDv7, and the only identifier that appears in a path or a deep link.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the order belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch that took the order, and whose sequence the number came from.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>
    /// The customer the commitment is to. An identifier: no name, no telephone number, no address.
    /// </summary>
    public Guid CustomerId { get; private set; }

    /// <summary>
    /// <c>O-&lt;branch&gt;-&lt;FY&gt;-000001</c>. Allocated at confirmation, never reused (INV-ORD-03).
    /// </summary>
    /// <remarks>
    /// Set in the private constructor and never re-pointed, so a number cannot come to name a different
    /// order. Allocation itself belongs to the application layer, which holds <c>ISequenceAllocator</c>
    /// and the branch code. A rolled-back confirmation never re-offers its number (FOC-04): nothing here
    /// caches one.
    /// </remarks>
    public OrderNumber OrderNumber { get; private set; }

    /// <summary>
    /// The draft this was confirmed from. Provenance, and what a replayed confirmation resolves through.
    /// </summary>
    public Guid OrderDraftId { get; private set; }

    /// <summary>The estimate the customer accepted, where one was issued.</summary>
    public Guid? EstimateId { get; private set; }

    /// <summary>The promised date for the order, evaluated in the branch timezone by the caller.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>What Reception typed about the order as a whole.</summary>
    public string? Notes { get; private set; }

    /// <summary>Where the order stands. Derived from its garment jobs except when it is cancelled.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>
    /// One at confirmation, incremented by each authorised revision. Carried on
    /// <c>orders.order-confirmed.v1</c> so a consumer can tell a republication from the original.
    /// </summary>
    public int RevisionNumber { get; private set; }

    /// <summary>
    /// The order-level priced result.
    /// </summary>
    /// <remarks>
    /// A snapshot for display and printing. Billing's <c>IFinancialTotalsQuery</c> holds the authoritative
    /// money position (INV-ORD-07), and this module performs no money arithmetic at all: it stores what
    /// Billing returned.
    /// </remarks>
    public PriceSnapshot Totals { get; private set; }

    /// <summary>When the order was confirmed, in UTC.</summary>
    public DateTimeOffset ConfirmedAt { get; private set; }

    /// <summary>Who confirmed it, where a person did.</summary>
    public Guid? ConfirmedBy { get; private set; }

    /// <summary>When the first garment job entered production, or null while none has.</summary>
    public DateTimeOffset? ProductionStartedAt { get; private set; }

    /// <summary>When the last non-cancelled garment job was handed over, or null.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>When the order was cancelled, or null.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Who cancelled it, where a person did.</summary>
    public Guid? CancelledBy { get; private set; }

    /// <summary>The configured reason code the cancellation was filed under, or null.</summary>
    public string? CancellationReasonCode { get; private set; }

    /// <summary>The free-text reason for the cancellation, or null.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>When the order was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>The garment jobs, in job-index order. Never empty on a confirmed order.</summary>
    /// <remarks>
    /// <strong>Published as a collection that cannot be written to.</strong> The backing list handed out
    /// behind an <c>IReadOnlyCollection</c> is one a cast undoes, and this list is not only what a caller
    /// reads: <see cref="DeliverTogetherSiblingsOf"/> and <see cref="DeliverTogetherParcelOf"/> walk it, and
    /// they are the single definition of "the parcel" that both halves of INV-JOB-09 are enforced against. A
    /// <c>Remove</c> through the cast would take a garment out of its parcel without cancelling it or
    /// delivering it — nothing fabricated, both guards disarmed — so the mutation is refused here, exactly as
    /// <see cref="ReadyGateOutcome.BoundWith"/> refuses it. A view over the aggregate's own list rather than a
    /// snapshot of it, because what is guarded against is a caller writing to the order, not a caller reading
    /// it a moment later.
    /// </remarks>
    public IReadOnlyCollection<GarmentJob> Jobs => _jobs.AsReadOnly();

    /// <summary>Every priced position this order has held, oldest first.</summary>
    public IReadOnlyCollection<OrderRevision> Revisions => _revisions;

    /// <summary>
    /// True once any garment job has actually entered production, and never false again afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads every job, including cancelled ones: a job that entered production and was then cancelled
    /// still consumed material and staff time. It is a latch, and <see cref="RecomputeStatus"/> reads
    /// this same property rather than a second, narrower reading of the same question — the two
    /// disagreeing is what let an order read as confirmed while its revision was refused as started.
    /// </para>
    /// <para>
    /// It is <strong>not</strong> what refuses an order revision. INV-ORD-05 permits a revision only while
    /// every job is still <see cref="GarmentJobStatus.Confirmed"/>, which is a wider condition —
    /// <c>GarmentJob.HasLeftConfirmed</c> — and includes a garment cancelled before anything was made.
    /// </para>
    /// </remarks>
    public bool HasEnteredProduction => _jobs.Exists(job => job.HasEnteredProduction);

    /// <summary>
    /// Confirms a draft into an order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the only way an Order comes into existence, and it is irreversible</strong>
    /// (<c>docs/prd/state-transitions.md</c> sections 2.1 and 7). Snapshots are frozen and display
    /// numbers allocated; the remedy afterwards is a revision before production, or a cancellation and a
    /// new order.
    /// </para>
    /// <para>
    /// Every garment specification is validated before anything is created, so there is no partly built
    /// order to persist (INV-ORD-01). The checks that can only be made here — and not inside a single
    /// <see cref="GarmentJobSpecification"/> — are the ones that are about the <em>set</em>: distinct
    /// garment identities, job indices that run from one with no gaps, distinct job numbers, every job
    /// number minted from <em>this</em> order's number, one catalogue, price-list and tax configuration
    /// version across the whole order (INV-ORD-02), distinct dependency-row identities, every declared
    /// dependency naming a garment that is part of this same confirmation, and no circle of garments
    /// waiting for each other. The transaction itself and the confirmation-participant hook that
    /// allocates barcode identities belong to the application layer.
    /// </para>
    /// <para>
    /// Each of those is refused <strong>here</strong> because confirmation is irreversible
    /// (state-transitions.md section 7): after it the only remedy is a cancellation and a new order, and
    /// two of them — a duplicated dependency identity and a mixed configuration version — would
    /// otherwise surface as a persistence exception from inside the transaction rather than as the RFC
    /// 9457 field error the person at the counter can act on.
    /// </para>
    /// <para>
    /// Revision one is written here rather than by the first revise, which is what makes
    /// <see cref="OrderRevision"/>'s append-only history complete: there is no priced position this order
    /// has held that is not a row.
    /// </para>
    /// </remarks>
    /// <param name="id">Identity, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchId">The branch taking the order.</param>
    /// <param name="customerId">The customer the commitment is to.</param>
    /// <param name="orderNumber">The allocated display number.</param>
    /// <param name="orderDraftId">The draft being confirmed.</param>
    /// <param name="estimateId">The accepted estimate, where one was issued.</param>
    /// <param name="dueDate">The promised date, already evaluated in the branch timezone.</param>
    /// <param name="notes">Order-level notes, where any were taken.</param>
    /// <param name="totals">The priced result and the configuration versions it used.</param>
    /// <param name="garments">One validated specification per garment.</param>
    /// <param name="initialRevisionId">Identity for revision one, from <c>IIdGenerator</c>.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>The order, or the reason the confirmation was refused.</returns>
    public static Result<Order> Confirm(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        OrderNumber orderNumber,
        Guid orderDraftId,
        Guid? estimateId,
        DateOnly dueDate,
        string? notes,
        PriceSnapshot totals,
        IReadOnlyCollection<GarmentJobSpecification> garments,
        Guid initialRevisionId,
        DateTimeOffset now,
        Guid? by = null)
    {
        ArgumentNullException.ThrowIfNull(orderNumber);
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(garments);

        if (id == Guid.Empty)
        {
            return Result.Failure<Order>(OrdersErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<Order>(OrdersErrors.Required("organisationId"));
        }

        // Branch scope is evaluated, never inferred: an order with no branch has no sequence to have
        // come from and no policy to be read under.
        if (branchId == Guid.Empty)
        {
            return Result.Failure<Order>(OrdersErrors.NoBranchInContext);
        }

        if (customerId == Guid.Empty)
        {
            return Result.Failure<Order>(OrdersErrors.Required("customerId"));
        }

        if (orderDraftId == Guid.Empty)
        {
            return Result.Failure<Order>(OrdersErrors.Required("orderDraftId"));
        }

        // An empty identifier here is a caller that passed `default` where it meant `null`; accepting it
        // would file the order against an estimate that does not exist.
        if (estimateId is { } estimate && estimate == Guid.Empty)
        {
            return Result.Failure<Order>(OrdersErrors.Required("estimateId"));
        }

        if (initialRevisionId == Guid.Empty)
        {
            return Result.Failure<Order>(OrdersErrors.Required("initialRevisionId"));
        }

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes is not null && trimmedNotes.Length > MaximumNotesLength)
        {
            return Result.Failure<Order>(OrdersErrors.TooLong("notes", MaximumNotesLength));
        }

        if (garments.Count == 0)
        {
            return Result.Failure<Order>(OrdersErrors.OrderHasNoGarmentJobs);
        }

        var garmentIds = new HashSet<Guid>();
        var jobIndices = new HashSet<int>();
        var jobNumbers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var garment in garments)
        {
            if (garment is null)
            {
                return Result.Failure<Order>(OrdersErrors.Required("garments"));
            }

            // Structural, not conventional: the job number carries the order number it was minted from,
            // so a job card printed for one order can never be filed against another.
            if (!string.Equals(garment.JobNumber.OrderNumber.Value, orderNumber.Value, StringComparison.Ordinal))
            {
                return Result.Failure<Order>(OrdersErrors.GarmentJobNumberNotOfThisOrder);
            }

            if (!garmentIds.Add(garment.GarmentJobId))
            {
                return Result.Failure<Order>(OrdersErrors.DuplicateGarmentJob("garmentJobId"));
            }

            if (!jobIndices.Add(garment.JobIndex))
            {
                return Result.Failure<Order>(OrdersErrors.DuplicateGarmentJob("jobIndex"));
            }

            if (!jobNumbers.Add(garment.JobNumber.Value))
            {
                return Result.Failure<Order>(OrdersErrors.DuplicateGarmentJob("jobNumber"));
            }
        }

        // The index is the garment's position within this order and it is printed on its job card, so a
        // two-garment order numbered 5 and 9 prints a set nobody can hand over against. Distinct was
        // never enough: the positions have to be one to however many garments there are.
        for (var position = DisplayNumberFormat.MinimumJobIndex;
            position < DisplayNumberFormat.MinimumJobIndex + garments.Count;
            position++)
        {
            if (!jobIndices.Contains(position))
            {
                return Result.Failure<Order>(OrdersErrors.GarmentJobIndicesNotContiguous);
            }
        }

        // INV-ORD-02 is a statement about the order and not about one snapshot: PriceSnapshot.Create can
        // guarantee that each figure names a version, and only this can guarantee that the whole order
        // names one. Checked against the order's own totals, which is the snapshot the invariant names.
        var versions = SharedConfigurationVersions(totals, garments);
        if (versions.IsFailure)
        {
            return Result.Failure<Order>(versions.Error);
        }

        // A second pass, because a dependency may name a garment that appears later in the set and the
        // whole set is only known once the first pass has finished.
        var dependencyIds = new HashSet<Guid>();

        foreach (var garment in garments)
        {
            foreach (var dependency in garment.Dependencies)
            {
                if (dependency.PrerequisiteGarmentJobId == garment.GarmentJobId)
                {
                    return Result.Failure<Order>(OrdersErrors.DependencyOnItself);
                }

                // INV-JOB-09 binds jobs of one order. A dependency reaching outside this confirmation
                // would be a promise the order cannot keep and the gate cannot read.
                if (!garmentIds.Contains(dependency.PrerequisiteGarmentJobId))
                {
                    return Result.Failure<Order>(OrdersErrors.DependencyNotInSameOrder);
                }

                // One garment can only see its own rows, so two garments handed the same identity for
                // their dependency rows is a collision only the order can see. Left to the transaction it
                // arrives as a primary-key violation from inside the confirmation rather than as the
                // field error the counter can act on (security rule 3).
                if (!dependencyIds.Add(dependency.DependencyId))
                {
                    return Result.Failure<Order>(OrdersErrors.DuplicateGarmentJob("dependencyId"));
                }
            }
        }

        if (HasFinishBeforeCycle(garments))
        {
            return Result.Failure<Order>(OrdersErrors.DependencyCycle);
        }

        var order = new Order(
            id,
            organisationId,
            branchId,
            customerId,
            orderNumber,
            orderDraftId,
            estimateId,
            dueDate,
            trimmedNotes,
            totals,
            now,
            by);

        // Ordered by job index rather than by the caller's ordering, so a job card set and a screen read
        // the same way whatever order the intake screen sent the garments in.
        foreach (var garment in garments.OrderBy(specification => specification.JobIndex))
        {
            order._jobs.Add(GarmentJob.Create(id, organisationId, branchId, garment, now, by));
        }

        order._revisions.Add(OrderRevision.Record(
            initialRevisionId,
            id,
            FirstRevisionNumber,
            reason: null,
            totals,
            dueDate,
            supersededEstimateId: null,
            recordedAt: now,
            recordedBy: by));

        return Result.Success(order);
    }

    /// <summary>
    /// Re-prices and re-validates the order before production.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Permitted only while every garment job is still confirmed</strong> (INV-ORD-05,
    /// state-transitions.md section 2.1). That is the literal reading and it is a choice worth seeing: an
    /// order carrying one cancelled job cannot be revised, because "every job still confirmed" is not
    /// "every job that is left". The route once any job has moved is an alteration request (issue #34).
    /// </para>
    /// <para>
    /// A revision <strong>appends</strong>; it never rewrites the previous position. The order's own
    /// totals and promised date move to the new position, and the position the order used to hold stays
    /// readable as the previous <see cref="OrderRevision"/>.
    /// </para>
    /// <para>
    /// <paramref name="garments"/> may name some of the jobs rather than all of them: a revision that
    /// re-prices one garment is an ordinary revision. The order's totals are replaced wholesale from
    /// <paramref name="totals"/> regardless, because Billing computed them and this module never adds up
    /// its own money (INV-ORD-07).
    /// </para>
    /// <para>
    /// It is refused, however, when the new totals name a catalogue, price-list or tax configuration
    /// version that any garment on the order — revised or not — does not share. INV-ORD-02 is about the
    /// order, and a partial re-price against a newly published catalogue would leave the order recording
    /// two of each.
    /// </para>
    /// </remarks>
    /// <param name="revisionId">Identity for the appended revision, from <c>IIdGenerator</c>.</param>
    /// <param name="reason">Why the order is being revised. Mandatory (state-transitions.md section 8).</param>
    /// <param name="totals">The re-priced result.</param>
    /// <param name="dueDate">The promised date as at this revision.</param>
    /// <param name="garments">The re-validated position of each garment job being revised.</param>
    /// <param name="supersededEstimateId">The outstanding estimate this revision retires, where there is one.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the revision was refused.</returns>
    public Result Revise(
        Guid revisionId,
        string? reason,
        PriceSnapshot totals,
        DateOnly dueDate,
        IReadOnlyCollection<GarmentJobRevision> garments,
        Guid? supersededEstimateId,
        DateTimeOffset now,
        Guid? by = null)
    {
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(garments);

        if (revisionId == Guid.Empty)
        {
            return Result.Failure(OrdersErrors.Required("revisionId"));
        }

        var reasoned = Reasoned(reason, out var trimmedReason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        if (supersededEstimateId is { } superseded && superseded == Guid.Empty)
        {
            return Result.Failure(OrdersErrors.Required("supersededEstimateId"));
        }

        // The terminal statuses first. A cancelled order is refused for being cancelled and not for having
        // started: its garments may every one still be Confirmed, and "raise an alteration instead" is the
        // wrong thing to tell the counter about an order nobody is making.
        var revisable = EnsureNotTerminal();
        if (revisable.IsFailure)
        {
            return revisable;
        }

        // INV-ORD-05 read literally: a job that has left Confirmed — into production, on hold, ready,
        // delivered or cancelled — closes the revision window for the whole order. Asked before the order's
        // own status, because that status is derived from these same jobs: the moment one starts, the order
        // reads as in production, and answering "an order cannot move from InProduction to Confirmed" would
        // hide the one refusal the person at the counter can act on.
        if (_jobs.Exists(job => job.HasLeftConfirmed))
        {
            return Result.Failure(OrdersErrors.RevisionRefusedAfterProduction);
        }

        // Unreachable while every job is still Confirmed, because an order whose jobs have not moved is
        // itself Confirmed. Kept so that a status some later command writes cannot slip past unnoticed.
        if (Status is not OrderStatus.Confirmed)
        {
            return Result.Failure(
                OrdersErrors.StatusTransitionNotAllowed(Status, OrderStatus.Confirmed));
        }

        // Resolved before anything is applied, so a revision naming an unknown job leaves the order
        // exactly as it was rather than half re-priced.
        var applications = new List<(GarmentJob Job, GarmentJobRevision Revision)>(garments.Count);
        var revised = new HashSet<Guid>();

        foreach (var revision in garments)
        {
            if (revision is null)
            {
                return Result.Failure(OrdersErrors.Required("garments"));
            }

            if (!revised.Add(revision.GarmentJobId))
            {
                return Result.Failure(OrdersErrors.DuplicateGarmentJob("garmentJobId"));
            }

            var job = FindJob(revision.GarmentJobId);
            if (job is null)
            {
                return Result.Failure(OrdersErrors.GarmentJobNotFound);
            }

            // Including that the design copy being frozen in answers the same category and service type the
            // garment was taken under — the check GarmentJobSpecification.Create makes at confirmation, made
            // here too so the revision path cannot swap in another garment's design.
            var permitted = job.CheckRevision(revision);
            if (permitted.IsFailure)
            {
                return permitted;
            }

            applications.Add((job, revision));
        }

        // INV-ORD-02 again, and it is the revision that makes it easy to break: the totals are replaced
        // wholesale from the caller while the garments nobody re-priced keep the versions they were
        // confirmed under, so a re-price against a newly published catalogue has to carry every garment
        // with it or be refused. Checked before anything is applied.
        var positions = applications.ToDictionary(pair => pair.Job.Id, pair => pair.Revision);

        foreach (var job in _jobs)
        {
            var agrees = positions.TryGetValue(job.Id, out var position)
                ? AgreesWithTotals(totals, position.Price, position.Design)
                : AgreesWithTotals(totals, job.Price, job.Design);

            if (agrees.IsFailure)
            {
                return agrees;
            }
        }

        foreach (var (job, revision) in applications)
        {
            // The job repeats the "still Confirmed" guard, so a job cannot be re-snapshotted through any
            // other route. It cannot fail here, because the same condition was checked above for every
            // job on the order.
            var applied = job.Revise(revision, now, by);
            if (applied.IsFailure)
            {
                return applied;
            }
        }

        RevisionNumber++;
        Totals = totals;
        DueDate = dueDate;

        _revisions.Add(OrderRevision.Record(
            revisionId,
            Id,
            RevisionNumber,
            trimmedReason,
            totals,
            dueDate,
            supersededEstimateId,
            recordedAt: now,
            recordedBy: by));

        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Starts production on one garment job and pins its workflow version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Irreversible: order revision is refused from this moment (INV-JOB-02, state-transitions.md
    /// section 7). The pinned version never migrates, even when a newer workflow version is published
    /// mid-job.
    /// </para>
    /// <para>
    /// <paramref name="satisfiedPrerequisiteJobIds"/> names the <c>finish_before</c> prerequisites the
    /// application has established are complete (issue #33). Phase-level completion is not something the
    /// Orders domain can see in this increment, so the fact arrives as a parameter rather than being
    /// guessed; a prerequisite absent from that set blocks the dependent job's first phase (INV-JOB-09).
    /// What counts as complete — including whether a cancelled prerequisite counts — is the application's
    /// call and is deliberately not decided here.
    /// </para>
    /// </remarks>
    /// <param name="garmentJobId">The job entering production.</param>
    /// <param name="workflowVersionId">The published workflow version to pin.</param>
    /// <param name="satisfiedPrerequisiteJobIds">The prerequisites the application established are complete.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the transition was refused.</returns>
    public Result StartProduction(
        Guid garmentJobId,
        Guid workflowVersionId,
        IReadOnlyCollection<Guid> satisfiedPrerequisiteJobIds,
        ReadyAggregation aggregation,
        DateTimeOffset now,
        Guid? by = null)
    {
        ArgumentNullException.ThrowIfNull(satisfiedPrerequisiteJobIds);

        var open = EnsureNotTerminal();
        if (open.IsFailure)
        {
            return open;
        }

        var policy = Understood(aggregation);
        if (policy.IsFailure)
        {
            return policy;
        }

        if (workflowVersionId == Guid.Empty)
        {
            return Result.Failure(OrdersErrors.Required("workflowVersionId"));
        }

        var job = FindJob(garmentJobId);
        if (job is null)
        {
            return Result.Failure(OrdersErrors.GarmentJobNotFound);
        }

        var prerequisites = new List<GarmentJob>();

        foreach (var dependency in job.Dependencies)
        {
            if (dependency.Kind is not JobDependencyKind.FinishBefore)
            {
                continue;
            }

            var prerequisite = FindJob(dependency.PrerequisiteGarmentJobId);
            if (prerequisite is null)
            {
                return Result.Failure(OrdersErrors.DependencyNotInSameOrder);
            }

            prerequisites.Add(prerequisite);
        }

        // Sorted so that a job blocked by two prerequisites names the same one on every attempt; a
        // refusal that moves around reads as a different problem each time.
        prerequisites.Sort(static (left, right)
            => string.CompareOrdinal(left.JobNumber.Value, right.JobNumber.Value));

        var satisfied = new HashSet<Guid>(satisfiedPrerequisiteJobIds);
        var unfinished = prerequisites.Find(prerequisite => !satisfied.Contains(prerequisite.Id));

        if (unfinished is not null)
        {
            // The job number is operational data, not personal data, so it is safe in a message.
            return Result.Failure(OrdersErrors.PrerequisiteNotFinished(unfinished.JobNumber.Value));
        }

        var started = job.StartProduction(workflowVersionId, now, by);
        if (started.IsFailure)
        {
            return started;
        }

        Touch(now, by);

        return RecomputeStatus(aggregation, now);
    }

    /// <summary>
    /// Puts one garment job on hold with a configured reason code and a reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Permitted from in production and ready only (state-transitions.md section 3.2). The hold closes
    /// the job's ready state with the gate's own <c>NoOpenHold</c> reason code, which is what section
    /// 3.2's Hold row lists among the transition's outputs; the order's status follows.
    /// </para>
    /// <para>
    /// <paramref name="approvedBy"/> carries the approval section 3.2 makes a precondition. It is
    /// recorded and not evaluated: the hold policy — which reason codes need an approval, from which role,
    /// above what threshold, and whether the approver may be the person holding the job — is stated by no
    /// document, so it is not invented here. The <c>holds</c> table, that policy and the overdue-hold
    /// dashboard belong to <strong>issue #34</strong>
    /// (<c>feat/e06-f03-qc-rework-alteration-hold-cancel</c>); until it lands, the hold is job state and
    /// the approval is whatever the application established.
    /// </para>
    /// </remarks>
    /// <param name="garmentJobId">The job being held.</param>
    /// <param name="reasonCode">The configured hold reason code.</param>
    /// <param name="reason">Why the job is being held. Mandatory (state-transitions.md section 8).</param>
    /// <param name="approvedBy">Who approved it under the branch's hold policy, where one did.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the transition was refused.</returns>
    public Result Hold(
        Guid garmentJobId,
        string? reasonCode,
        string? reason,
        Guid? approvedBy,
        ReadyAggregation aggregation,
        DateTimeOffset now,
        Guid? by = null)
    {
        var open = EnsureNotTerminal();
        if (open.IsFailure)
        {
            return open;
        }

        var policy = Understood(aggregation);
        if (policy.IsFailure)
        {
            return policy;
        }

        var coded = Coded(reasonCode, out var trimmedReasonCode);
        if (coded.IsFailure)
        {
            return coded;
        }

        var reasoned = Reasoned(reason, out var trimmedReason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        var job = FindJob(garmentJobId);
        if (job is null)
        {
            return Result.Failure(OrdersErrors.GarmentJobNotFound);
        }

        var held = job.Hold(trimmedReasonCode, trimmedReason, approvedBy, now, by);
        if (held.IsFailure)
        {
            return held;
        }

        Touch(now, by);

        return RecomputeStatus(aggregation, now);
    }

    /// <summary>
    /// Returns a held garment job to production with a reason.
    /// </summary>
    /// <remarks>
    /// Resuming never moves the promised date. State-transitions.md section 3.2 is explicit that a new
    /// date is a separate reschedule with its own reason and its own customer communication, so that a
    /// date is never quietly moved behind the customer's back. The job returns to in production even when
    /// the hold was taken from ready; the gate then recomputes.
    /// </remarks>
    /// <param name="garmentJobId">The job being resumed.</param>
    /// <param name="reason">Why the hold is lifted. Mandatory (state-transitions.md section 8).</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the transition was refused.</returns>
    public Result Resume(
        Guid garmentJobId,
        string? reason,
        ReadyAggregation aggregation,
        DateTimeOffset now,
        Guid? by = null)
    {
        var open = EnsureNotTerminal();
        if (open.IsFailure)
        {
            return open;
        }

        var policy = Understood(aggregation);
        if (policy.IsFailure)
        {
            return policy;
        }

        var reasoned = Reasoned(reason, out var trimmedReason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        var job = FindJob(garmentJobId);
        if (job is null)
        {
            return Result.Failure(OrdersErrors.GarmentJobNotFound);
        }

        var resumed = job.Resume(trimmedReason, now, by);
        if (resumed.IsFailure)
        {
            return resumed;
        }

        Touch(now, by);

        return RecomputeStatus(aggregation, now);
    }

    /// <summary>
    /// Moves one garment job's promised date with a reason.
    /// </summary>
    /// <remarks>
    /// The job's status does not move, so no aggregation is needed and the order's status is not
    /// recomputed. The new date is evaluated in the branch timezone against the branch working calendar
    /// by the caller (<c>docs/architecture/conventions.md</c> section 2.2); the domain never resolves a
    /// calendar. The order's own promised date is not touched either — moving that is a revision, with
    /// its own priced position.
    /// </remarks>
    /// <param name="garmentJobId">The job being rescheduled.</param>
    /// <param name="dueDate">The new promised date, already evaluated in the branch timezone.</param>
    /// <param name="reason">Why the date is moving. Mandatory (state-transitions.md section 8).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the transition was refused.</returns>
    public Result Reschedule(
        Guid garmentJobId,
        DateOnly dueDate,
        string? reason,
        DateTimeOffset now,
        Guid? by = null)
    {
        var open = EnsureNotTerminal();
        if (open.IsFailure)
        {
            return open;
        }

        var reasoned = Reasoned(reason, out var trimmedReason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        var job = FindJob(garmentJobId);
        if (job is null)
        {
            return Result.Failure(OrdersErrors.GarmentJobNotFound);
        }

        var rescheduled = job.Reschedule(dueDate, trimmedReason, now, by);
        if (rescheduled.IsFailure)
        {
            return rescheduled;
        }

        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Applies a ready-gate outcome to the job it was evaluated for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is how INV-JOB-07 is enforced by the type system rather than by review.</strong> The
    /// only argument that moves ready state is a <see cref="ReadyGateOutcome"/>, whose constructor is
    /// private and whose only factory is internal to <see cref="ReadyGate"/>. No Application, Api or host
    /// code can fabricate a verdict of ready, which matches <c>docs/prd/raci.md</c> row 16: no role,
    /// however senior, declares a garment ready.
    /// </para>
    /// <para>
    /// <strong>Guarded by <see cref="EnsureNotTerminal"/> like every other command here</strong>, and that
    /// is load-bearing rather than tidy. <see cref="Cancel"/> deliberately does not cascade into the
    /// garment jobs (SQ-04), so a cancelled order still holds jobs in production and ready; the job-level
    /// refusal only catches a cancelled <em>job</em>, and the gate's six predicates are about a garment
    /// and never about its order. Without this guard the gate promoted a garment of a cancelled order to
    /// <see cref="GarmentJobStatus.Ready"/> and materialised its ready state, which is a delivery queue
    /// entry and a dispatchable parcel for an order nobody is making — section 2.1 says there is no
    /// un-cancel, and section 3.2 draws no row that makes such a garment ready. The only thing that
    /// refuses it afterwards is the doorstep confirmation, by which time the parcel has left the shop.
    /// </para>
    /// <para>
    /// Nothing is lost by refusing. A cancelled order's gate reasons are history, not a screen somebody is
    /// working from, and <see cref="RecomputeStatus"/> already declines to recompute a terminal order.
    /// </para>
    /// <para>
    /// <strong>One garment, but never half a parcel.</strong> A garment bound to nothing is the
    /// overwhelmingly common case and is exactly what this overload is for. A garment that <em>is</em> bound
    /// <c>deliver_together</c> carries the rest of its parcel on the verdict itself
    /// (<see cref="ReadyGateOutcome.BoundWith"/>), and a verdict of <em>ready</em> applied here is refused
    /// with <c>OrdersErrors.ReadyGateWouldSplitParcel</c> unless every live partner already stands at
    /// <see cref="GarmentJobStatus.Ready"/> — INV-JOB-09 held by the domain rather than by the caller
    /// remembering to use the set overload. The remedy for that refusal is
    /// <see cref="ApplyReadyGate(IReadOnlyCollection{ReadyGateOutcome}, ReadyAggregation, DateTimeOffset)"/>
    /// over the whole evaluation. A verdict that <em>closes</em> this garment's gate needs no partner's
    /// agreement and is recorded on its own, which is what a QC failure, a custody case or a missing document
    /// recomputed for one garment is (<see cref="ParcelPresented"/>).
    /// </para>
    /// </remarks>
    /// <param name="garmentJobId">The job the outcome was evaluated for.</param>
    /// <param name="outcome">The gate's verdict.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>Success, or the reason the outcome could not be applied.</returns>
    public Result ApplyReadyGate(
        Guid garmentJobId,
        ReadyGateOutcome outcome,
        ReadyAggregation aggregation,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var open = EnsureNotTerminal();
        if (open.IsFailure)
        {
            return open;
        }

        var policy = Understood(aggregation);
        if (policy.IsFailure)
        {
            return policy;
        }

        // A verdict reached for one garment says nothing about another. Applying it across jobs would
        // make a garment ready on evidence that was never about it.
        if (outcome.GarmentJobId != garmentJobId)
        {
            return Result.Failure(OrdersErrors.ReadyGateOutcomeForAnotherJob);
        }

        // One garment is a set of one, and the set overload is where every rule about applying a verdict
        // lives — including the one that refuses to record half a bound parcel. Kept as a delegation rather
        // than a second copy so the two routes cannot drift apart, which is exactly how the single-garment
        // route came to carry no binding at all.
        return ApplyReadyGate([outcome], aggregation, now);
    }

    /// <summary>
    /// Applies a whole evaluation's verdicts, promoting the garments they are about together or not at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the applying half of INV-JOB-09.</strong> A <c>deliver_together</c> dependency binds its
    /// garments at the ready gate, and <see cref="ReadyGate.EvaluateSet"/> is what makes that possible: it judges
    /// every member of the bound set on its own six predicates in one evaluation, so a set whose members all pass
    /// gets a verdict of ready for every one of them at once. Applying those verdicts one command at a time would
    /// hand back the problem the set evaluation solved — a refusal on the second garment would leave the first
    /// promoted, on the delivery queue, and bound to a garment that is not coming.
    /// </para>
    /// <para>
    /// <strong>Every verdict is checked before any is written.</strong> The guards live on the job as
    /// <c>GarmentJob.CheckReadyGate</c> and are pure, so this can ask them of the whole set first; only then does
    /// anything move. The single-garment overload is the same thing over a set of one — it delegates here — and
    /// neither weakens INV-JOB-07: the only argument either accepts is a <see cref="ReadyGateOutcome"/>, which
    /// nothing outside this assembly can make.
    /// </para>
    /// <para>
    /// <strong>And a parcel is promoted whole or not at all.</strong> Each verdict names the rest of the bound
    /// set it was reached inside, so a command that would promote one member while a live partner is left short
    /// of <c>ready</c> is refused with <c>OrdersErrors.ReadyGateWouldSplitParcel</c> — see
    /// <see cref="ParcelPresented"/> — whether that partner is missing from the command or carried in it with a
    /// verdict of blocked. A partner already standing at <c>ready</c>, and a partner that has left the parcel,
    /// are both satisfied. Demotions are not refused for a partner's sake at all: they take a garment off the
    /// delivery queue, and no parcel is split by that.
    /// </para>
    /// <para>
    /// The order's own status is recomputed once, at the end, from the garments as they then stand (SQ-02).
    /// </para>
    /// </remarks>
    /// <param name="outcomes">The gate's verdicts, one per garment it was evaluated for.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>Success, or the first reason a verdict could not be applied, with nothing written.</returns>
    public Result ApplyReadyGate(
        IReadOnlyCollection<ReadyGateOutcome> outcomes,
        ReadyAggregation aggregation,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        var open = EnsureNotTerminal();
        if (open.IsFailure)
        {
            return open;
        }

        var policy = Understood(aggregation);
        if (policy.IsFailure)
        {
            return policy;
        }

        var applications = new List<(GarmentJob Job, ReadyGateOutcome Outcome)>(outcomes.Count);
        var addressed = new Dictionary<Guid, ReadyGateOutcome>(outcomes.Count);

        foreach (var outcome in outcomes)
        {
            if (outcome is null)
            {
                return Result.Failure(OrdersErrors.Required("outcomes"));
            }

            // Two verdicts about one garment inside a single evaluation cannot both be the gate's answer, and
            // which of them won would be the order they happened to arrive in.
            if (!addressed.TryAdd(outcome.GarmentJobId, outcome))
            {
                return Result.Failure(OrdersErrors.DuplicateGarmentJob("garmentJobId"));
            }

            var job = FindJob(outcome.GarmentJobId);
            if (job is null)
            {
                return Result.Failure(OrdersErrors.GarmentJobNotFound);
            }

            var permitted = job.CheckReadyGate(outcome);
            if (permitted.IsFailure)
            {
                return permitted;
            }

            applications.Add((job, outcome));
        }

        var whole = ParcelPresented(applications, addressed);
        if (whole.IsFailure)
        {
            return whole;
        }

        foreach (var (job, outcome) in applications)
        {
            // Cannot fail: the same guards were asked of every one of them above, and nothing between then and
            // now can have moved a garment of this order.
            var applied = job.ApplyReadyGate(outcome, now);
            if (applied.IsFailure)
            {
                return applied;
            }
        }

        if (applications.Count == 0)
        {
            return Result.Success();
        }

        // The gate runs as the system, so there is no actor to attribute the change to.
        Touch(now, by: null);

        return RecomputeStatus(aggregation, now);
    }

    /// <summary>
    /// Records the doorstep handover of one garment job.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Irreversible (state-transitions.md section 7). Custody owns the preconditions — a valid unexpired
    /// dispatch authorisation, an unchanged custodian, the recipient confirmation — and has checked them
    /// before this is called; what Orders owns is that the job was ready and that the order's status
    /// follows. A failed or returned delivery is a compensating custody transfer, never an undo of this.
    /// </para>
    /// <para>
    /// <strong>And that the parcel is not split at the door.</strong> INV-JOB-09 says a
    /// <c>deliver_together</c> dependency binds its garments "at the ready gate <em>and in the delivery
    /// queue</em>", and section 3.2's delivery row says the same. Only the first half was ever built: the gate
    /// promoted a parcel together, and then a hold on one member — which closes that member's ready state and
    /// nobody else's — left its partner standing at ready, on the queue, and handed over on its own. That is a
    /// garment at the customer's door while the garment it was promised to travel with is still being made,
    /// which is the sentence the binding exists to prevent, so it is refused here with
    /// <c>OrdersErrors.DeliveryWouldSplitParcel</c> — see <see cref="ParcelTravelsTogether"/>.
    /// </para>
    /// <para>
    /// It belongs on the order rather than on the garment because the promise is about garments, plural: a
    /// <see cref="GarmentJob"/> cannot see its siblings, and Custody's preconditions are about this one
    /// garment's chain of custody and are a different question, already answered by the time this is called.
    /// </para>
    /// </remarks>
    /// <param name="garmentJobId">The job handed over.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <param name="partialDeliveryPermitted">
    /// The branch policy permits a <c>deliver_together</c> sibling to go on its own (issue #48), read at the
    /// scan as the ready gate reads it. A parameter and not stored state for the reason
    /// <see cref="ReadyAggregation"/> is one: it is branch configuration in force now, not when the order was
    /// taken.
    /// </param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the transition was refused.</returns>
    public Result ConfirmDelivery(
        Guid garmentJobId,
        ReadyAggregation aggregation,
        bool partialDeliveryPermitted,
        DateTimeOffset now,
        Guid? by = null)
    {
        var open = EnsureNotTerminal();
        if (open.IsFailure)
        {
            return open;
        }

        var policy = Understood(aggregation);
        if (policy.IsFailure)
        {
            return policy;
        }

        var job = FindJob(garmentJobId);
        if (job is null)
        {
            return Result.Failure(OrdersErrors.GarmentJobNotFound);
        }

        // This garment's own answer first, and asked without writing anything: a garment that is on hold is
        // refused because it is on hold, not because of the company it keeps.
        var deliverable = job.CheckDelivery();
        if (deliverable.IsFailure)
        {
            return deliverable;
        }

        var whole = ParcelTravelsTogether(job, partialDeliveryPermitted);
        if (whole.IsFailure)
        {
            return whole;
        }

        var delivered = job.ConfirmDelivery(now, by);
        if (delivered.IsFailure)
        {
            return delivered;
        }

        Touch(now, by);

        return RecomputeStatus(aggregation, now);
    }

    /// <summary>
    /// Cancels one garment job with a configured reason code and a reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="prohibitedStates"/> carries the blocking financial, stock and custody states the
    /// application established; a non-empty list is a refusal naming the first of them. The list itself
    /// is issue #34's and is supplied rather than invented here (INV-ORD-06). Cancellation is blocked,
    /// not forced: the compensating flows run first (<c>docs/prd/exceptions.md</c> EX-08).
    /// </para>
    /// <para>
    /// <strong>Cancelling every garment job does not cancel the order.</strong> That is the interim
    /// position of <strong>SQ-04</strong> and it is why this command never writes
    /// <see cref="OrderStatus.Cancelled"/>: order cancellation stays an explicit, separately authorised
    /// and reasoned command because its financial consequences differ. When the last job is cancelled the
    /// order's status is simply left where it was.
    /// </para>
    /// </remarks>
    /// <param name="garmentJobId">The job being cancelled.</param>
    /// <param name="reasonCode">The configured cancellation reason code.</param>
    /// <param name="reason">Why the job is being cancelled. Mandatory (state-transitions.md section 8).</param>
    /// <param name="prohibitedStates">The blocking state codes the application established, if any.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the cancellation was refused.</returns>
    public Result CancelJob(
        Guid garmentJobId,
        string? reasonCode,
        string? reason,
        IReadOnlyCollection<string> prohibitedStates,
        ReadyAggregation aggregation,
        DateTimeOffset now,
        Guid? by = null)
    {
        ArgumentNullException.ThrowIfNull(prohibitedStates);

        var open = EnsureNotTerminal();
        if (open.IsFailure)
        {
            return open;
        }

        var policy = Understood(aggregation);
        if (policy.IsFailure)
        {
            return policy;
        }

        var coded = Coded(reasonCode, out var trimmedReasonCode);
        if (coded.IsFailure)
        {
            return coded;
        }

        var reasoned = Reasoned(reason, out var trimmedReason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        // The garment is resolved before its own rules are applied, as every other job-scoped command
        // here does. Asking about the blockers first answered "this garment cannot be cancelled while
        // unreturned-customer-material stands" for a garment that is not on the order at all — a blocker
        // named against something the order does not have.
        var job = FindJob(garmentJobId);
        if (job is null)
        {
            return Result.Failure(OrdersErrors.GarmentJobNotFound);
        }

        var blocked = FirstProhibitedState(prohibitedStates, out var blockingState);
        if (blocked.IsFailure)
        {
            return blocked;
        }

        if (blockingState is not null)
        {
            return Result.Failure(OrdersErrors.JobCancellationBlocked(blockingState));
        }

        var cancelled = job.Cancel(trimmedReasonCode, trimmedReason, now, by);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        Touch(now, by);

        return RecomputeStatus(aggregation, now);
    }

    /// <summary>
    /// Cancels the order with a configured reason code and a reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Permitted from <see cref="OrderStatus.Confirmed"/>, <see cref="OrderStatus.InProduction"/> and
    /// <see cref="OrderStatus.Ready"/> only (state-transitions.md section 2.1). <em>Draft</em> appears in
    /// that table's From column for the draft aggregate, which is discarded rather than cancelled; no
    /// <see cref="Order"/> is ever in that state. There is no un-cancel.
    /// </para>
    /// <para>
    /// Blocked, not forced, while a prohibited financial, stock or custody state stands — recognised value
    /// on a posted invoice, unreturned customer material, a garment in another custodian's hands
    /// (INV-ORD-06, <c>docs/prd/exceptions.md</c> EX-08). The list arrives from the application, which
    /// holds the contracts that can answer; a non-empty list refuses the command naming the first state
    /// code, never a value.
    /// </para>
    /// <para>
    /// This is the only writer of <see cref="OrderStatus.Cancelled"/> (SQ-04). It deliberately does
    /// <strong>not</strong> cascade into the garment jobs: no document states that cancelling an order
    /// cancels each of its jobs, and a cascade would write a cancellation with no reason code of its own
    /// onto rows that each need one. The compensating flows the transition's outputs describe — released
    /// reservations, a cancellation credit intent, customer material returned — belong to the application
    /// layer, which runs them against the modules that own them.
    /// </para>
    /// </remarks>
    /// <param name="reasonCode">The configured cancellation reason code.</param>
    /// <param name="reason">Why the order is being cancelled. Mandatory (state-transitions.md section 8).</param>
    /// <param name="prohibitedStates">The blocking state codes the application established, if any.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the cancellation was refused.</returns>
    public Result Cancel(
        string? reasonCode,
        string? reason,
        IReadOnlyCollection<string> prohibitedStates,
        DateTimeOffset now,
        Guid? by = null)
    {
        ArgumentNullException.ThrowIfNull(prohibitedStates);

        if (Status is not (OrderStatus.Confirmed or OrderStatus.InProduction or OrderStatus.Ready))
        {
            return Result.Failure(
                OrdersErrors.StatusTransitionNotAllowed(Status, OrderStatus.Cancelled));
        }

        var coded = Coded(reasonCode, out var trimmedReasonCode);
        if (coded.IsFailure)
        {
            return coded;
        }

        var reasoned = Reasoned(reason, out var trimmedReason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        var blocked = FirstProhibitedState(prohibitedStates, out var blockingState);
        if (blocked.IsFailure)
        {
            return blocked;
        }

        if (blockingState is not null)
        {
            return Result.Failure(OrdersErrors.CancellationBlocked(blockingState));
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        CancelledBy = by;
        CancellationReasonCode = trimmedReasonCode;
        CancellationReason = trimmedReason;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>One garment job by its identity, or null when it is not on this order.</summary>
    /// <param name="garmentJobId">The job.</param>
    /// <returns>The job, or null.</returns>
    public GarmentJob? FindJob(Guid garmentJobId)
        => _jobs.Find(job => job.Id == garmentJobId);

    /// <summary>
    /// The jobs bound to this one by a <c>deliver_together</c> dependency, in either direction.
    /// </summary>
    /// <remarks>
    /// The relationship is symmetric even though the row is not: "this garment goes with that one" is the
    /// same promise read from either end, so a sibling that declared the dependency and a sibling that was
    /// named by it are both returned (INV-JOB-09). This is what the ready gate's <c>DependenciesMet</c>
    /// predicate reads, and the result is ordered by job number so the queue screen names the blocking
    /// siblings in a stable order.
    /// </remarks>
    /// <param name="garmentJobId">The job whose siblings are wanted.</param>
    /// <returns>The sibling jobs, empty when there are none or the job is not on this order.</returns>
    public IReadOnlyCollection<GarmentJob> DeliverTogetherSiblingsOf(Guid garmentJobId)
    {
        var job = FindJob(garmentJobId);
        if (job is null)
        {
            return [];
        }

        var siblingIds = new HashSet<Guid>(job.PrerequisiteJobIds(JobDependencyKind.DeliverTogether));

        foreach (var other in _jobs)
        {
            if (other.Id == garmentJobId)
            {
                continue;
            }

            if (other.PrerequisiteJobIds(JobDependencyKind.DeliverTogether).Contains(garmentJobId))
            {
                siblingIds.Add(other.Id);
            }
        }

        siblingIds.Remove(garmentJobId);

        var siblings = _jobs.FindAll(candidate => siblingIds.Contains(candidate.Id));
        siblings.Sort(static (left, right)
            => string.CompareOrdinal(left.JobNumber.Value, right.JobNumber.Value));

        return siblings;
    }

    /// <summary>
    /// The whole <c>deliver_together</c> parcel one garment is bound into, itself excluded: the connected
    /// component of the relation over the garments the order still owes, in job-number order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One definition of "the parcel", read by both halves of INV-JOB-09.</strong>
    /// <c>ReadyGate.EvaluateSet</c> reads it to decide which garments are judged in one evaluation, and
    /// <see cref="ConfirmDelivery"/> reads it to refuse a handover that would strand a partner. Written once
    /// here rather than twice, because SQ-07 and SQ-08 are both unsettled and a parcel that meant one thing at
    /// the gate and another at the door would be two interim positions pretending to be one.
    /// </para>
    /// <para>
    /// <strong>Closed under the relation</strong> — a garment bound to a second which is bound to a third is
    /// one parcel of three (<strong>SQ-07</strong>, interim), because stopping at the garments a row names
    /// directly would let the first go while the third was still being made.
    /// </para>
    /// <para>
    /// <strong>And only over the garments the order still owes.</strong> A garment that is no longer
    /// deliverable — cancelled, or already handed over — is neither a member nor a step between two members
    /// (<strong>SQ-08</strong>, interim). That holds of the named garment too: one that has left has no parcel
    /// of its own, so this is empty for it rather than naming garments still in the shop.
    /// </para>
    /// </remarks>
    /// <param name="garmentJobId">The garment whose parcel is wanted.</param>
    /// <returns>
    /// The garments bound to it, empty when it is bound to none, is not on this order, or is no longer
    /// deliverable.
    /// </returns>
    public IReadOnlyList<GarmentJob> DeliverTogetherParcelOf(Guid garmentJobId)
    {
        var job = FindJob(garmentJobId);

        if (job is null || !job.IsDeliverable)
        {
            return [];
        }

        var parcel = new List<GarmentJob>();
        var seen = new HashSet<Guid> { job.Id };
        var frontier = new Queue<GarmentJob>();
        frontier.Enqueue(job);

        while (frontier.Count > 0)
        {
            foreach (var sibling in DeliverTogetherSiblingsOf(frontier.Dequeue().Id))
            {
                if (sibling.IsDeliverable && seen.Add(sibling.Id))
                {
                    parcel.Add(sibling);
                    frontier.Enqueue(sibling);
                }
            }
        }

        parcel.Sort(static (left, right)
            => string.CompareOrdinal(left.JobNumber.Value, right.JobNumber.Value));

        return parcel;
    }

    /// <summary>
    /// Recomputes the derived status from the garment jobs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the interim position of SQ-02 and must not be presented as settled.</strong>
    /// <c>docs/prd/state-transitions.md</c> section 10 proposes: in production once any job has entered
    /// production, ready when the job set the branch dispatch policy requires is ready, delivered when
    /// every non-cancelled job is delivered. The part the interim position leaves open — which job set
    /// the policy requires — arrives as <paramref name="aggregation"/> rather than being guessed, so the
    /// rule is visible at every call site and changes in one place when SQ-02 settles.
    /// </para>
    /// <para>
    /// Two statuses are never produced here. <see cref="OrderStatus.Cancelled"/> is written only by
    /// <see cref="Cancel"/>, so that cancelling every garment job cannot cancel the order
    /// (<strong>SQ-04</strong>); when every job is cancelled the status is simply left unchanged.
    /// <see cref="OrderStatus.Closed"/> is never produced because <strong>SQ-01</strong> has not settled
    /// what closes a delivered order, and until it does, delivered is the last automatic state. Both are
    /// nonetheless treated as terminal on the way in, so a status read back from the database is never
    /// silently recomputed away.
    /// </para>
    /// <para>
    /// Public so that a projection or a custody-driven recomputation can invoke it; every command above
    /// already calls it, so an ordinary caller never needs to.
    /// </para>
    /// </remarks>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>
    /// Success, or <c>OrdersErrors.NotUnderstood</c> for a dispatch policy this module does not name — which a
    /// terminal order never reports, because there is nothing for the policy to decide about one.
    /// </returns>
    public Result RecomputeStatus(ReadyAggregation aggregation, DateTimeOffset now)
    {
        // Asked after the terminal short-circuit and not before it, which is the order every command above
        // uses: the status a terminal order is in is the answer whatever the policy says, and refusing it for
        // a value nothing was going to read would be this one type giving two answers to one question.
        if (Status is OrderStatus.Cancelled or OrderStatus.Closed)
        {
            return Result.Success();
        }

        var policy = Understood(aggregation);
        if (policy.IsFailure)
        {
            return policy;
        }

        // Recorded the first time it becomes true rather than only on the in-production branch: a job
        // that starts and passes the gate between two recomputations still started production, and the
        // moment it did is not recoverable later. HasEnteredProduction is a latch over the garments'
        // own ProductionStartedAt, so a garment cancelled straight from confirmed — one nobody ever
        // began — no longer stamps a start date on an order on which nothing was made.
        if (ProductionStartedAt is null && HasEnteredProduction)
        {
            ProductionStartedAt = now;
        }

        var live = _jobs.FindAll(job => job.Status is not GarmentJobStatus.Cancelled);

        // Every job cancelled leaves the order exactly where it was (SQ-04).
        if (live.Count == 0)
        {
            return Result.Success();
        }

        if (live.TrueForAll(job => job.Status is GarmentJobStatus.Delivered or GarmentJobStatus.Closed))
        {
            Status = OrderStatus.Delivered;

            // Set once and never cleared. An accepted post-delivery alteration moves the status back to
            // in production, but the date the customer received their garments did not stop being true.
            DeliveredAt ??= now;

            return Result.Success();
        }

        if (IsReadySetSatisfied(live, aggregation))
        {
            Status = OrderStatus.Ready;
            return Result.Success();
        }

        // Read over every job and not only the live ones, and it is the same latch this aggregate's own
        // HasEnteredProduction answers four lines above. SQ-02's interim position is
        // "in_production once **any** job has entered production" — 'non-cancelled' qualifies its
        // delivered clause alone — and section 2's order diagram draws no edge from in production back to
        // confirmed. Counting only the live jobs drew one: cancelling the single garment that had started
        // took the order backwards to confirmed, the one status from which revision is permitted, while
        // Revise went on refusing it as started. Two answers to the counter about one order.
        Status = HasEnteredProduction ? OrderStatus.InProduction : OrderStatus.Confirmed;

        return Result.Success();
    }

    /// <summary>
    /// Checks that every garment of a confirmation was priced and validated under the order's own versions.
    /// </summary>
    /// <remarks>
    /// INV-ORD-02 — "a confirmed order records <strong>exactly one</strong> catalog version, one price-list
    /// version and one tax configuration version" — read as the statement about the order that it is. The
    /// design snapshot's catalogue version is checked against the same one, because a garment whose
    /// choices were validated against one published catalogue and priced against another is a garment
    /// nobody can re-derive, and INV-JOB-01 freezes both copies permanently.
    /// </remarks>
    private static Result SharedConfigurationVersions(
        PriceSnapshot totals,
        IReadOnlyCollection<GarmentJobSpecification> garments)
    {
        foreach (var garment in garments)
        {
            var agrees = AgreesWithTotals(totals, garment.Price, garment.Design);
            if (agrees.IsFailure)
            {
                return agrees;
            }
        }

        return Result.Success();
    }

    private static Result AgreesWithTotals(PriceSnapshot totals, PriceSnapshot price, DesignSnapshot design)
    {
        if (price.CatalogVersionId != totals.CatalogVersionId
            || design.CatalogVersionId != totals.CatalogVersionId)
        {
            return Result.Failure(OrdersErrors.ConfigurationVersionNotShared("catalogVersionId"));
        }

        if (price.PriceListVersionId != totals.PriceListVersionId)
        {
            return Result.Failure(OrdersErrors.ConfigurationVersionNotShared("priceListVersionId"));
        }

        return price.TaxConfigurationVersionId != totals.TaxConfigurationVersionId
            ? Result.Failure(OrdersErrors.ConfigurationVersionNotShared("taxConfigurationVersionId"))
            : Result.Success();
    }

    /// <summary>
    /// Whether any set of garments in this confirmation waits for itself round a circle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A depth-first walk of the <c>finish_before</c> edges alone. INV-JOB-09 gives the two kinds different
    /// meanings: <c>finish_before</c> is an ordering and a circle in it is a set of garments none of which
    /// can start, while <c>deliver_together</c> is symmetric by design — "this garment goes with that one"
    /// read from either end — so a pair naming each other under that kind is the ordinary case and not a
    /// defect.
    /// </para>
    /// <para>
    /// Checked at confirmation for the reason <see cref="GarmentJobSpecification"/> already gives for the
    /// one-garment case: confirmation is irreversible, <c>StartProduction</c> would refuse every garment of
    /// the circle for ever, and by the time the gate saw it the only remedy would be a cancellation and a
    /// new order.
    /// </para>
    /// </remarks>
    private static bool HasFinishBeforeCycle(IReadOnlyCollection<GarmentJobSpecification> garments)
    {
        var prerequisites = new Dictionary<Guid, List<Guid>>(garments.Count);

        foreach (var garment in garments)
        {
            prerequisites[garment.GarmentJobId] =
            [
                .. garment.Dependencies
                    .Where(dependency => dependency.Kind is JobDependencyKind.FinishBefore)
                    .Select(dependency => dependency.PrerequisiteGarmentJobId),
            ];
        }

        var settled = new HashSet<Guid>();
        var onPath = new HashSet<Guid>();

        return prerequisites.Keys.Any(garmentJobId => Reaches(garmentJobId, prerequisites, settled, onPath));
    }

    private static bool Reaches(
        Guid garmentJobId,
        Dictionary<Guid, List<Guid>> prerequisites,
        HashSet<Guid> settled,
        HashSet<Guid> onPath)
    {
        if (settled.Contains(garmentJobId))
        {
            return false;
        }

        // Already on the path being walked: the garment waits, however indirectly, for itself.
        if (!onPath.Add(garmentJobId))
        {
            return true;
        }

        // Every prerequisite has already been checked to be a garment of this same confirmation, so the
        // lookup cannot miss.
        if (prerequisites[garmentJobId].Any(prerequisite
                => Reaches(prerequisite, prerequisites, settled, onPath)))
        {
            return true;
        }

        onPath.Remove(garmentJobId);
        settled.Add(garmentJobId);

        return false;
    }

    /// <summary>
    /// Refuses a branch dispatch policy that is none of the ones this module names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQ-02's answer arrives as an argument rather than as stored state, so it crosses a public command
    /// boundary seven times and <see cref="RecomputeStatus"/> an eighth — and an enumeration in C# accepts
    /// any number a cast can produce, which is what a deserialiser does with a value it did not recognise.
    /// </para>
    /// <para>
    /// An unnamed value is not a weaker answer but an uninterpretable one.
    /// <see cref="IsReadySetSatisfied"/> tests for one named member and reads everything else as the other,
    /// so a branch whose dispatch policy nobody recognised would silently get the conservative rule and
    /// never be told which rule it got. Asked before anything is written, so the refusal is a refusal and
    /// not a half-applied command.
    /// </para>
    /// </remarks>
    /// <param name="aggregation">The value the caller supplied.</param>
    /// <returns>Success, or <c>OrdersErrors.NotUnderstood</c>.</returns>
    private static Result Understood(ReadyAggregation aggregation)
        => Enum.IsDefined(aggregation)
            ? Result.Success()
            : Result.Failure(OrdersErrors.NotUnderstood("aggregation"));

    /// <summary>
    /// Whether these verdicts would leave a garment on the delivery queue without the parcel it travels with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is INV-JOB-09 on the applying side, and it is a domain guarantee rather than a caller
    /// convention.</strong> <c>ReadyGate.EvaluateSet</c> reaches an independent verdict for every member of
    /// a bound parcel, and each verdict records the rest of that parcel in
    /// <see cref="ReadyGateOutcome.BoundWith"/>. Without this, one member's verdict could be applied on its
    /// own — leaving a garment promoted, on the delivery queue and dispatchable, while the garment it was
    /// promised to travel with stood in production. That split is what the binding exists to prevent, and
    /// before the parcel was evaluated as a set it was unreachable only because the old predicate deadlocked
    /// the parcel instead.
    /// </para>
    /// <para>
    /// <strong>And it refuses in one direction only.</strong> What splits a parcel is a garment left standing
    /// at <c>ready</c> with a live partner that is not, so that is what is refused. Closing a member's gate
    /// only ever takes a garment <em>off</em> the delivery queue and can leave no half parcel standing on it,
    /// so it is never refused for the company the garment keeps. Refusing it did real harm: a QC failure
    /// recomputed for the garment that failed — which is what <c>docs/prd/state-transitions.md</c> section 9.1
    /// says a QC event causes — was refused while its partner stood at ready, so the garment that failed QC
    /// stayed at <c>ready</c> and stayed dispatchable. The guard written to stop half a parcel leaving was
    /// what kept a failed garment on the queue.
    /// </para>
    /// <para>
    /// <strong>Being in the command is not being answered for.</strong> The question is where each partner
    /// will stand once this command has been written, and there are two ways to know: a partner the command
    /// carries a verdict for will stand where <em>that</em> verdict puts it, and a partner it does not will
    /// stand where it already does. Read that way, a partner already at ready satisfies a verdict of ready
    /// without being re-applied — nothing is left behind, because it is already there — and one command still
    /// cannot promote a garment while holding its partner back, because the promotion is the half this
    /// refusal reads. A partner that is no longer deliverable is not in the parcel at all (SQ-08, interim),
    /// so it is neither waited for nor answered for, which is the same reading the gate itself takes when it
    /// builds the set.
    /// </para>
    /// <para>
    /// <strong>What it does not do is close the partners' gates as well.</strong> A verdict recorded here
    /// takes the one garment off the queue and leaves its partners standing at <c>ready</c>, exactly as
    /// <c>GarmentJob.Hold</c> does. Whether the rest of a promoted parcel should come off with it is
    /// <strong>SQ-09</strong>, which is not settled and is not this guard's to settle; the promise is kept at
    /// the door instead (<see cref="ParcelTravelsTogether"/>).
    /// </para>
    /// </remarks>
    /// <param name="applications">The verdicts about to be written, with the jobs they are about.</param>
    /// <param name="addressed">Every garment this command carries a verdict for, by the verdict it carries.</param>
    /// <returns>Success, or <c>OrdersErrors.ReadyGateWouldSplitParcel</c> naming the garment left behind.</returns>
    private Result ParcelPresented(
        List<(GarmentJob Job, ReadyGateOutcome Outcome)> applications,
        Dictionary<Guid, ReadyGateOutcome> addressed)
    {
        foreach (var (_, outcome) in applications)
        {
            foreach (var boundWith in outcome.BoundWith)
            {
                var partner = FindJob(boundWith);
                if (partner is null)
                {
                    return Result.Failure(OrdersErrors.GarmentJobNotFound);
                }

                // A garment that is no longer deliverable has left the parcel (SQ-08, interim): there is
                // nothing to leave behind and nothing to disagree with.
                if (!partner.IsDeliverable)
                {
                    continue;
                }

                // Where the partner stands once this command has been written — its own verdict when the
                // command carries one, and where it already stands when it does not.
                var partnerWillBeReady = addressed.TryGetValue(boundWith, out var theirs)
                    ? theirs.IsReady
                    : partner.IsReadyForDelivery;

                // One direction. A verdict that would leave this garment on the delivery queue while a live
                // partner is not there with it is the split; a verdict that takes this garment off the queue
                // splits nothing, whatever its partners are doing.
                if (outcome.IsReady && !partnerWillBeReady)
                {
                    return Result.Failure(OrdersErrors.ReadyGateWouldSplitParcel(partner.JobNumber.Value));
                }
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Whether this garment can be handed over without stranding a garment it was promised to travel with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is INV-JOB-09's second half — the delivery queue — and it is the only place it is
    /// kept.</strong> <see cref="ParcelPresented"/> holds the parcel together while the gate writes ready
    /// state, but ready state does not stay where the gate left it: <c>GarmentJob.Hold</c>,
    /// <c>GarmentJob.Resume</c> and <c>GarmentJob.Cancel</c> each close one garment's gate and no other's, so a
    /// parcel promoted together could be taken apart by a hold on one member and the survivor handed over
    /// alone. The gate cannot prevent that — the split happens after it has spoken — so the promise is kept
    /// again at the door, against the garments as they then stand.
    /// </para>
    /// <para>
    /// <strong>What is asked is that every live partner is itself ready</strong>, not that it goes out in the
    /// same command. A handover is one garment at a time by construction — Custody authorises one chain of
    /// custody — and two garments that are both ready are handed over one after the other, the first leaving
    /// the parcel as it goes (SQ-08, interim). A partner that has left the parcel, cancelled or already
    /// delivered, is not waited for at all.
    /// </para>
    /// <para>
    /// <strong>The branch's waiver still waives it.</strong> Where the branch policy permits partial delivery
    /// the garments were never bound (issue #48): the gate waives <c>DependenciesMet</c> and records no parcel
    /// on the verdict, and this refusal has to agree with it or the waiver would hold at the gate and fail at
    /// the door. The policy is read at the scan, as it is read at the gate, which is also why a parcel bound in
    /// one evaluation and waived in another — facts the gate cannot compare across two calls — still cannot be
    /// split at the door while the branch's answer today is that garments travel together.
    /// </para>
    /// <para>
    /// <see cref="ReadyAggregation"/> is deliberately not consulted. <c>AnyDeliverableJob</c> loosens which
    /// garments the <em>order's status</em> waits for; it says nothing about what one customer was promised
    /// together, and reading it as the waiver would let a branch that hands garments over as they finish break
    /// a <c>deliver_together</c> promise it never waived.
    /// </para>
    /// <para>
    /// <strong>What this does not do is take the partner off the queue.</strong> A garment whose partner is
    /// held stays at <c>ready</c> with its own gate open, and is refused only when somebody tries to hand it
    /// over. Whether a hold on one member should instead close the whole parcel's ready state is a question
    /// about what the customer was promised rather than an engineering one, and it is recorded as
    /// <strong>SQ-09</strong> in state-transitions.md section 10, unsettled.
    /// </para>
    /// </remarks>
    /// <param name="job">The garment being handed over, already known to be ready itself.</param>
    /// <param name="partialDeliveryPermitted">The branch policy permits a sibling to go on its own.</param>
    /// <returns>
    /// Success, or <c>OrdersErrors.DeliveryWouldSplitParcel</c> naming the garment that would be stranded.
    /// </returns>
    private Result ParcelTravelsTogether(GarmentJob job, bool partialDeliveryPermitted)
    {
        if (partialDeliveryPermitted)
        {
            return Result.Success();
        }

        foreach (var partner in DeliverTogetherParcelOf(job.Id))
        {
            // The materialised verdict and not the status, which is the same question ParcelPresented asks of a
            // partner it is not writing: job_ready_state is what the delivery queue is built from (section 4.1).
            if (!partner.IsReadyForDelivery)
            {
                return Result.Failure(OrdersErrors.DeliveryWouldSplitParcel(partner.JobNumber.Value));
            }
        }

        return Result.Success();
    }

    private static bool IsReadySetSatisfied(List<GarmentJob> live, ReadyAggregation aggregation)
    {
        if (aggregation is ReadyAggregation.AnyDeliverableJob)
        {
            return live.Exists(job => job.Status is GarmentJobStatus.Ready);
        }

        var outstanding = live.FindAll(job
            => job.Status is not (GarmentJobStatus.Delivered or GarmentJobStatus.Closed));

        // "At least one such job exists" matters: with none outstanding the delivered branch above has
        // already answered, and treating the empty set as satisfied would report an order ready that has
        // nothing left to be ready.
        return outstanding.Count > 0
            && outstanding.TrueForAll(job => job.Status is GarmentJobStatus.Ready);
    }

    private static Result Reasoned(string? reason, out string trimmed)
    {
        trimmed = reason?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Result.Failure(OrdersErrors.ReasonRequired);
        }

        return trimmed.Length > MaximumReasonLength
            ? Result.Failure(OrdersErrors.TooLong("reason", MaximumReasonLength))
            : Result.Success();
    }

    private static Result Coded(string? reasonCode, out string trimmed)
    {
        trimmed = reasonCode?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Result.Failure(OrdersErrors.ReasonCodeRequired);
        }

        return trimmed.Length > MaximumReasonCodeLength
            ? Result.Failure(OrdersErrors.TooLong("reasonCode", MaximumReasonCodeLength))
            : Result.Success();
    }

    /// <summary>
    /// Reads the first blocking state out of what the application established.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A blank entry is refused rather than reported as the blocking state: a refusal that cannot name
    /// what is blocking leaves the person at the counter with nothing to act on, and an unnamed blocker
    /// is a defect in the caller rather than a state of the order.
    /// </para>
    /// <para>
    /// Every entry is bounded by <see cref="MaximumReasonCodeLength"/>, because the first one is
    /// interpolated into a problem detail. A blocking state is a configured <em>code</em> like every other
    /// code this module carries — the list is issue #34's — and bounding it here is what stops a sentence,
    /// or anything read off a customer record, travelling into an RFC 9457 response
    /// (<c>OrdersErrors</c>'s header: the messages name fields, statuses and operational references, and
    /// never values). The shape of the code is deliberately <em>not</em> checked: the vocabulary belongs
    /// to the issue that fixes the list, and inventing a pattern here would refuse a code somebody else
    /// gets to choose.
    /// </para>
    /// </remarks>
    private static Result FirstProhibitedState(IReadOnlyCollection<string> prohibitedStates, out string? blocking)
    {
        blocking = null;

        foreach (var state in prohibitedStates)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return Result.Failure(OrdersErrors.Required("prohibitedStates"));
            }

            var code = state.Trim();

            if (code.Length > MaximumReasonCodeLength)
            {
                return Result.Failure(OrdersErrors.TooLong("prohibitedStates", MaximumReasonCodeLength));
            }

            blocking ??= code;
        }

        return Result.Success();
    }

    /// <summary>
    /// Refuses anything at all on an order that has reached the end of its lifecycle.
    /// </summary>
    /// <remarks>
    /// Names no target status, deliberately. Most callers here are moving a <em>garment</em>, and the
    /// refusal used to be built from the garment's target — so holding a garment of a cancelled order
    /// answered "an order cannot move from Cancelled to OnHold", naming a status
    /// <c>docs/prd/state-transitions.md</c> section 2 is explicit that an order never has. What the
    /// person at the counter needs is the one fact that refused them: this order is cancelled.
    /// </remarks>
    private Result EnsureNotTerminal()
        => Status is OrderStatus.Cancelled or OrderStatus.Closed
            ? Result.Failure(OrdersErrors.OrderNoLongerOpen(Status))
            : Result.Success();

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
