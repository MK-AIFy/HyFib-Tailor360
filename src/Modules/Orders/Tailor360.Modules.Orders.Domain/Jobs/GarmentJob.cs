using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// The unit of production and tracking: one garment, one job card, one barcode identity.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A child entity of <c>Order</c>, not an aggregate root.</strong> Its factory and every mutator below
/// are <see langword="internal" />, so the only route to a job transition is the matching public command on the
/// order — which recomputes the order's derived status afterwards. That is what makes the SQ-02 aggregation
/// impossible to skip, and it is why a mutator here must never be made public: the moment one is, an application
/// handler can move a job and leave the order saying something the jobs no longer support.
/// </para>
/// <para>
/// <strong>Snapshots are copies, not references</strong> (INV-JOB-01,
/// <c>docs/prd/state-transitions.md</c> section 3.2). Republishing the catalogue, retiring a design group,
/// renaming an option, changing the price list or capturing a newer measurement never changes a confirmed job.
/// The only write path is <see cref="Revise"/>, and that is refused the moment the job leaves
/// <see cref="GarmentJobStatus.Confirmed"/> (INV-ORD-05).
/// </para>
/// <para>
/// <strong>Nothing but the gate can make ready state true.</strong> <see cref="IsReadyForDelivery"/>,
/// <see cref="ReadyStateComputedAt"/> and <see cref="ReadyStateBlocks"/> are <c>job_ready_state</c> — what the
/// delivery queue and the dispatch attempt read (section 4.1), not this type's status enumeration — and the only
/// way any of them comes to say <em>ready</em> is <see cref="ApplyReadyGate"/> applying a
/// <see cref="ReadyGateOutcome"/>, which only <see cref="ReadyGate"/> can make (INV-JOB-07,
/// <c>docs/prd/raci.md</c> row 16).
/// </para>
/// <para>
/// <strong>Closing it is a different question from opening it.</strong> Section 3.2's Hold row lists "the ready
/// gate closes" among the transition's own <em>outputs</em>, so <see cref="Hold"/> closes the materialised state
/// in the same breath as it moves the status, writing the gate's own <c>NoOpenHold</c> reason code — the very
/// block the next recomputation produces. Leaving it to that recomputation is what CI-03 calls drift: nothing in
/// the domain can require the application to recompute in the same transaction, and between the two a held
/// garment would advertise itself through <c>IOrderSnapshotQuery</c> as ready with no reason shown. INV-JOB-07
/// constrains <em>who declares a garment ready</em>; it does not licence a stale true.
/// </para>
/// <para>
/// <strong>Priority is deliberately absent.</strong> <c>docs/architecture/module-ownership.md</c> section 5.5,
/// <c>docs/architecture/sequences/order-confirmation.md</c> (on <c>orders.garment-job-created.v1</c>) and
/// <c>docs/security/field-visibility.md</c> all mention a priority on a garment job, and no document anywhere
/// states its vocabulary — no levels, no rush flag, no default. It is a product decision rather than an
/// engineering one, so it is not invented here; adding it later is an additive migration and an additive
/// property. <strong>It is not yet registered in <c>docs/prd/assumptions-and-open-decisions.md</c></strong>,
/// which is where CLAUDE.md section 8 says an undecided question belongs — this remark is not a substitute for
/// that entry, and the gap will otherwise surface as a hole in the published event payload rather than as the
/// product question it is.
/// </para>
/// <para>
/// <strong>Design revision after confirmation is not here either.</strong> INV-JOB-08 and section 8's
/// <c>orders.revise_design</c> row describe a post-confirmation command that records a reason, a price delta and
/// a due-date delta and is refused once the workflow marks the design frozen. It needs the workflow's frozen
/// flag (issue #33) and the alteration and pricing machinery of <strong>issue #34</strong>, neither of which
/// exists yet, so until then the only write path to <see cref="Design"/> is <see cref="Revise"/> and that closes
/// the moment any garment leaves <see cref="GarmentJobStatus.Confirmed"/> (INV-ORD-05).
/// </para>
/// </remarks>
public sealed class GarmentJob
{
    /// <summary>
    /// The longest reason the column holds. Matches <c>CustomerExport.MaximumReasonLength</c>, the repository's
    /// precedent for a free-text reason.
    /// </summary>
    public const int MaximumReasonLength = 500;

    /// <summary>
    /// The longest reason code the column holds. Matches Catalog's <c>CatalogCode.MaximumLength</c>: a hold or
    /// cancellation reason code is configuration, not prose.
    /// </summary>
    public const int MaximumReasonCodeLength = 40;

    /// <summary>
    /// The longest category or service-type key the column holds. Matches Catalog's
    /// <c>CatalogCode.MaximumLength</c>.
    /// </summary>
    public const int MaximumKeyLength = 40;

    private readonly List<Guid> _referenceMediaIds = [];
    private readonly List<JobDependency> _dependencies = [];
    private readonly List<ReadyGateBlock> _readyStateBlocks = [];

    private GarmentJob()
    {
        // The persistence layer materialises instances through this constructor and writes every property
        // immediately afterwards; the assignments below only satisfy the nullable analysis.
        JobNumber = null!;
        CategoryKey = null!;
        ServiceTypeKey = null!;
        Measurements = null!;
        Design = null!;
        Price = null!;
    }

    private GarmentJob(
        Guid orderId,
        Guid organisationId,
        Guid branchId,
        GarmentJobSpecification specification,
        DateTimeOffset confirmedAt,
        Guid? confirmedBy)
    {
        Id = specification.GarmentJobId;
        OrderId = orderId;
        OrganisationId = organisationId;
        BranchId = branchId;
        JobNumber = specification.JobNumber;
        JobIndex = specification.JobIndex;
        CategoryKey = specification.CategoryKey;
        ServiceTypeKey = specification.ServiceTypeKey;
        WorkflowDefinitionId = specification.WorkflowDefinitionId;
        Measurements = specification.Measurements;
        Design = specification.Design;
        Price = specification.Price;
        DueDate = specification.DueDate;
        Status = GarmentJobStatus.Confirmed;
        ConfirmedAt = confirmedAt;
        ConfirmedBy = confirmedBy;
        UpdatedAt = confirmedAt;
        UpdatedBy = confirmedBy;

        _referenceMediaIds.AddRange(specification.ReferenceMediaIds);

        foreach (var dependency in specification.Dependencies)
        {
            _dependencies.Add(JobDependency.Declare(
                dependency.DependencyId,
                Id,
                dependency.PrerequisiteGarmentJobId,
                dependency.Kind,
                dependency.Reason,
                confirmedAt,
                confirmedBy));
        }
    }

    /// <summary>Identity of the job. A UUIDv7, and the only identifier that appears in a path or a deep link.</summary>
    public Guid Id { get; private set; }

    /// <summary>The order this garment belongs to.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The organisation the job belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch that took the order and where the work is tracked.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>
    /// <c>J-&lt;branch&gt;-&lt;FY&gt;-000001-01</c>. Never the barcode payload, which is opaque and carries no
    /// meaning at all (<c>docs/architecture/conventions.md</c> section 3.3, INV-BID-04).
    /// </summary>
    public GarmentJobNumber JobNumber { get; private set; }

    /// <summary>The one-based position of this garment within its order.</summary>
    public int JobIndex { get; private set; }

    /// <summary>The stitching category, as Catalog keys it.</summary>
    public string CategoryKey { get; private set; }

    /// <summary>The service type within that category, as Catalog keys it.</summary>
    public string ServiceTypeKey { get; private set; }

    /// <summary>The workflow definition recorded at confirmation.</summary>
    public Guid WorkflowDefinitionId { get; private set; }

    /// <summary>
    /// The workflow version pinned at start of production, or null before it.
    /// </summary>
    /// <remarks>
    /// Once set it never changes, even when a newer workflow version is published mid-job (INV-JOB-02). A running
    /// garment finishes under the process it started under, because the tailor was told what the steps were.
    /// </remarks>
    public Guid? WorkflowVersionId { get; private set; }

    /// <summary>The measurement copy frozen at confirmation (INV-JOB-01).</summary>
    public MeasurementSnapshot Measurements { get; private set; }

    /// <summary>The design copy frozen at confirmation (INV-JOB-01).</summary>
    public DesignSnapshot Design { get; private set; }

    /// <summary>
    /// The priced result frozen at confirmation. A snapshot for display and printing; Billing's
    /// <c>IFinancialTotalsQuery</c> holds the authoritative money position (INV-ORD-07).
    /// </summary>
    public PriceSnapshot Price { get; private set; }

    /// <summary>The promised date for this garment, evaluated in the branch timezone by the caller.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>Where the job stands.</summary>
    public GarmentJobStatus Status { get; private set; }

    /// <summary>When the job was created, inside the order confirmation transaction.</summary>
    public DateTimeOffset ConfirmedAt { get; private set; }

    /// <summary>Who confirmed the order, where a person did.</summary>
    public Guid? ConfirmedBy { get; private set; }

    /// <summary>When production started, or null while it has not.</summary>
    public DateTimeOffset? ProductionStartedAt { get; private set; }

    /// <summary>Who started production, where a person did.</summary>
    public Guid? ProductionStartedBy { get; private set; }

    /// <summary>The configured hold reason code while the job is held, or null.</summary>
    public string? HoldReasonCode { get; private set; }

    /// <summary>
    /// Why the job is held, in the words of the person who held it, or null. Never personal data
    /// (security rule 7).
    /// </summary>
    /// <remarks>
    /// Kept beside <see cref="HoldReasonCode"/> for the reason <c>OrdersErrors.ReasonCodeRequired</c> gives: the
    /// code is what a report groups by, and the sentence is what the next person to open the job reads. Section 8
    /// makes the sentence mandatory on a hold, and a mandatory value that is validated and then thrown away is a
    /// question asked for nothing — <see cref="Cancel"/> keeps both, and so does this.
    /// </remarks>
    public string? HoldReason { get; private set; }

    /// <summary>
    /// When the current hold began, or null. Kept so the time on hold is visible and the promised date can be
    /// renegotiated honestly rather than quietly (state-transitions.md section 3.2).
    /// </summary>
    public DateTimeOffset? HeldAt { get; private set; }

    /// <summary>Who took the hold, where a person did.</summary>
    public Guid? HeldBy { get; private set; }

    /// <summary>
    /// Who approved the hold, where the branch's hold policy required an approval and one was recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>docs/prd/state-transitions.md</c> section 3.2 makes "approval per the hold policy" a
    /// <strong>precondition</strong> of the transition rather than one of its outputs, and
    /// <c>docs/architecture/module-ownership.md</c> section 5.5 lists <c>holds</c> among the
    /// post-confirmation exceptions carried "with reason <strong>and approval</strong>". The approval
    /// therefore arrives as a fact the application established, in the shape this module already uses for
    /// facts it cannot see for itself — the satisfied prerequisites of <c>Order.StartProduction</c>, the
    /// prohibited states of <c>Order.Cancel</c>.
    /// </para>
    /// <para>
    /// <strong>Nullable, and that is the gap rather than the design.</strong> No document states the hold
    /// policy: not which reason codes need an approval, not above what age or what value, not which role
    /// may give one, not whether the approver must be someone other than the person holding the job. Those
    /// are product decisions and are not invented here, so the domain records the approval it was given
    /// and refuses nothing on its absence. The <c>holds</c> table itself, its approval rule and its
    /// overdue-hold dashboard belong to <strong>issue #34</strong>
    /// (<c>feat/e06-f03-qc-rework-alteration-hold-cancel</c>), which is where the policy has to be settled;
    /// this property is the structure that makes adding it additive.
    /// </para>
    /// </remarks>
    public Guid? HoldApprovedBy { get; private set; }

    /// <summary>When the garment was handed over at the door, or null.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>Who confirmed the handover, where a person did.</summary>
    public Guid? DeliveredBy { get; private set; }

    /// <summary>When the job was cancelled, or null.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Who cancelled it, where a person did.</summary>
    public Guid? CancelledBy { get; private set; }

    /// <summary>The configured cancellation reason code, or null.</summary>
    public string? CancellationReasonCode { get; private set; }

    /// <summary>Why the job was cancelled, or null. Never personal data (security rule 7).</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>When the job was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it, or null where the change was the system's.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>
    /// The gate's materialised outcome. Written by <see cref="ApplyReadyGate"/> and by nothing else
    /// (INV-JOB-07).
    /// </summary>
    public bool IsReadyForDelivery { get; private set; }

    /// <summary>When the gate that produced the current verdict was evaluated, or null before the first one.</summary>
    public DateTimeOffset? ReadyStateComputedAt { get; private set; }

    /// <summary>
    /// Why the gate is closed, one entry per failing predicate, in <see cref="ReadyGatePredicate"/> order. Empty
    /// when the gate is open, and empty before the gate has ever run.
    /// </summary>
    public IReadOnlyCollection<ReadyGateBlock> ReadyStateBlocks => _readyStateBlocks;

    /// <summary>
    /// Reference and material images, by Media id. Never a URL and never an object key: every object is streamed
    /// by an endpoint that re-authorises the request (security rule 9).
    /// </summary>
    public IReadOnlyList<Guid> ReferenceMediaIds => _referenceMediaIds;

    /// <summary>What this job waits for, or is delivered with (INV-JOB-09).</summary>
    public IReadOnlyCollection<JobDependency> Dependencies => _dependencies;

    /// <summary>
    /// True once the job has left <see cref="GarmentJobStatus.Confirmed"/>, whatever it has done since.
    /// </summary>
    /// <remarks>
    /// This is what refuses an order revision. INV-ORD-05 permits a revision only while <em>every</em> job is
    /// still confirmed, so the question the order asks is "has this one moved at all", not "is it in production
    /// right now" — a job that was cancelled, or held, or delivered has moved, and re-snapshotting it would
    /// rewrite a position somebody has already acted on.
    /// </remarks>
    public bool HasLeftConfirmed => Status is not GarmentJobStatus.Confirmed;

    /// <summary>
    /// True once work has actually begun on this garment, and never false again afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A latch on <see cref="ProductionStartedAt"/>, which <see cref="StartProduction"/> is the only writer of.
    /// It stays true through a hold, a delivery and a cancellation, because a garment that was started and then
    /// cancelled still consumed material and staff time.
    /// </para>
    /// <para>
    /// <strong>Deliberately not "has left confirmed"</strong>, which is <see cref="HasLeftConfirmed"/> and a
    /// different question. A garment cancelled straight from <see cref="GarmentJobStatus.Confirmed"/> has left
    /// confirmed and has <em>not</em> entered production: nobody cut anything. Answering both with one predicate
    /// is what stamped an order with a production start date on which nothing was ever made, and what took an
    /// order's status backwards (state-transitions.md section 2.1, SQ-02).
    /// </para>
    /// </remarks>
    public bool HasEnteredProduction => ProductionStartedAt is not null;

    /// <summary>
    /// True while the job is still part of what the order owes the customer — that is, it is neither cancelled,
    /// nor delivered, nor closed. What the order's status aggregation counts (SQ-02, interim).
    /// </summary>
    public bool IsDeliverable => Status is not (
        GarmentJobStatus.Cancelled or GarmentJobStatus.Delivered or GarmentJobStatus.Closed);

    /// <summary>The prerequisites of one kind, as identifiers.</summary>
    /// <param name="kind">Which relationship to read.</param>
    /// <returns>The jobs this one depends on under that kind. Empty when it declares none.</returns>
    public IReadOnlyCollection<Guid> PrerequisiteJobIds(JobDependencyKind kind)
        => _dependencies
            .Where(dependency => dependency.Kind == kind)
            .Select(dependency => dependency.PrerequisiteGarmentJobId)
            .ToList();

    /// <summary>
    /// Creates the job inside the order confirmation. Called by the order aggregate, never directly.
    /// </summary>
    /// <remarks>
    /// Takes no <c>Result</c> because everything that could be refused has already been refused:
    /// <see cref="GarmentJobSpecification.Create"/> validated the garment and <c>Order.Confirm</c> validated the
    /// set. INV-ORD-01 wants confirmation atomic across the whole order, and a factory that could still fail here
    /// would mean a partly built order in memory at the moment the transaction opens.
    /// </remarks>
    /// <param name="orderId">The order being confirmed.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchId">The branch that took the order.</param>
    /// <param name="specification">The validated garment.</param>
    /// <param name="confirmedAt">The instant, from <c>IClock</c>.</param>
    /// <param name="confirmedBy">The actor.</param>
    /// <returns>The job.</returns>
    internal static GarmentJob Create(
        Guid orderId,
        Guid organisationId,
        Guid branchId,
        GarmentJobSpecification specification,
        DateTimeOffset confirmedAt,
        Guid? confirmedBy)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return new GarmentJob(orderId, organisationId, branchId, specification, confirmedAt, confirmedBy);
    }

    /// <summary>
    /// Replaces the snapshots and the promised date during an order revision.
    /// </summary>
    /// <remarks>
    /// Refused unless the job is still <see cref="GarmentJobStatus.Confirmed"/>, which is the one window
    /// INV-JOB-01's immutability leaves open and the same window INV-ORD-05 gives the order. The guard is repeated
    /// here rather than trusted to <c>Order.Revise</c>, so a job cannot be re-snapshotted through any other route
    /// that is added later. A job that has been cancelled is refused by the same rule and for the same reason: it
    /// has left <see cref="GarmentJobStatus.Confirmed"/>, and somebody has already acted on that.
    /// </remarks>
    /// <param name="revision">The re-validated and re-priced position.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the revision was refused.</returns>
    internal Result Revise(GarmentJobRevision revision, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(revision);

        // A revision addressed to a different job would silently re-snapshot the wrong garment; the order locates
        // the job before calling, so reaching this is a defect in a caller inside this assembly rather than input.
        if (revision.GarmentJobId != Id)
        {
            return Result.Failure(OrdersErrors.GarmentJobNotFound);
        }

        if (Status is not GarmentJobStatus.Confirmed)
        {
            return Result.Failure(OrdersErrors.RevisionRefusedAfterProduction);
        }

        Measurements = revision.Measurements;
        Design = revision.Design;
        Price = revision.Price;
        DueDate = revision.DueDate;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Pins the workflow version and enters production.
    /// </summary>
    /// <remarks>
    /// Irreversible (state-transitions.md section 7): the pinned version never changes, even when a newer one is
    /// published mid-job, and order revision is refused from this moment (INV-JOB-02). The
    /// <c>finish_before</c> prerequisites are checked by <c>Order.StartProduction</c>, which is the only place
    /// that can see the sibling jobs.
    /// </remarks>
    /// <param name="workflowVersionId">The published workflow version to pin.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    internal Result StartProduction(Guid workflowVersionId, DateTimeOffset now, Guid? by)
    {
        if (workflowVersionId == Guid.Empty)
        {
            return Result.Failure(OrdersErrors.Required("workflowVersionId"));
        }

        if (Status is not GarmentJobStatus.Confirmed)
        {
            return Result.Failure(
                OrdersErrors.JobStatusTransitionNotAllowed(Status, GarmentJobStatus.InProduction));
        }

        // Reachable only from a confirmed job that somehow already carries a version, which would be a defect in
        // the materialiser rather than in a command — and re-pinning would migrate a running job to a process
        // nobody told the tailor about (INV-JOB-02).
        if (WorkflowVersionId is not null)
        {
            return Result.Failure(OrdersErrors.WorkflowVersionAlreadyPinned);
        }

        WorkflowVersionId = workflowVersionId;
        Status = GarmentJobStatus.InProduction;
        ProductionStartedAt = now;
        ProductionStartedBy = by;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Holds the job against a configured reason code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Permitted from <see cref="GarmentJobStatus.InProduction"/> and <see cref="GarmentJobStatus.Ready"/> only,
    /// which is exactly what state-transitions.md section 3.2 names.
    /// </para>
    /// <para>
    /// <strong>The hold closes the ready state itself.</strong> Section 3.2 lists "the ready gate closes" among
    /// this transition's outputs, not among the things that happen to follow it, and the materialised state — not
    /// the status enumeration — is what the delivery-team receive scan reads (section 4.1). Leaving a true flag
    /// standing until the application happened to recompute would let a held garment be handed to the delivery
    /// team with no reason shown anywhere, which is the drift CI-03 names. What is written is the gate's own
    /// <c>NoOpenHold</c> reason code carrying this hold's code, which is exactly what the next recomputation
    /// produces: the hold closes the gate, it does not decide anything the gate would not.
    /// </para>
    /// <para>
    /// The mandatory reason is stored as well as validated. Section 8 makes it mandatory so that it can be read
    /// back, and <see cref="Cancel"/> keeps both parts of the same pair.
    /// </para>
    /// </remarks>
    /// <param name="reasonCode">The configured hold reason code.</param>
    /// <param name="reason">Why the job is being held. Required (section 8).</param>
    /// <param name="approvedBy">Who approved it, where the branch's hold policy required an approval.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    internal Result Hold(string reasonCode, string reason, Guid? approvedBy, DateTimeOffset now, Guid? by)
    {
        if (Status is not (GarmentJobStatus.InProduction or GarmentJobStatus.Ready))
        {
            return Result.Failure(OrdersErrors.JobStatusTransitionNotAllowed(Status, GarmentJobStatus.OnHold));
        }

        var code = ReasonCode(reasonCode);
        if (code.IsFailure)
        {
            return Result.Failure(code.Error);
        }

        var text = Reason(reason);
        if (text.IsFailure)
        {
            return Result.Failure(text.Error);
        }

        Status = GarmentJobStatus.OnHold;
        HoldReasonCode = code.Value;
        HoldReason = text.Value;
        HoldApprovedBy = approvedBy;
        HeldAt = now;
        HeldBy = by;

        // The reason code and never the sentence: a block travels to a queue screen, and section 9.1's
        // blocking reason for this predicate is "the hold reason and its age".
        RecordReadyState(false, [new ReadyGateBlock(ReadyGatePredicate.NoOpenHold, code.Value)], now);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Returns a held job to production.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Permitted from <see cref="GarmentJobStatus.OnHold"/> only, and it always returns the job to
    /// <see cref="GarmentJobStatus.InProduction"/> — even when the hold was taken from
    /// <see cref="GarmentJobStatus.Ready"/> — because section 3.2 says so and because the gate, not this command,
    /// decides whether the garment is ready again. Resuming never moves the promised date: that is a separate
    /// reschedule with its own reason and its own customer communication.
    /// </para>
    /// <para>
    /// The hold's block goes with the hold. What is left is a garment that is <em>not</em> ready and carries no
    /// reasons yet — the state a garment is in before the gate has ever run — and section 3.2's Resume row makes
    /// the gate recomputation the transition's output, which is what fills the reasons in. Keeping the
    /// <c>NoOpenHold</c> block would name a hold that has been lifted.
    /// </para>
    /// </remarks>
    /// <param name="reason">Why the hold is being lifted. Required (section 8).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    internal Result Resume(string reason, DateTimeOffset now, Guid? by)
    {
        if (Status is not GarmentJobStatus.OnHold)
        {
            return Result.Failure(
                OrdersErrors.JobStatusTransitionNotAllowed(Status, GarmentJobStatus.InProduction));
        }

        var text = Reason(reason);
        if (text.IsFailure)
        {
            return Result.Failure(text.Error);
        }

        Status = GarmentJobStatus.InProduction;
        HoldReasonCode = null;
        HoldReason = null;
        HoldApprovedBy = null;
        HeldAt = null;
        HeldBy = null;
        RecordReadyState(false, [], now);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Moves the promised date.
    /// </summary>
    /// <remarks>
    /// Permitted from <see cref="GarmentJobStatus.InProduction"/> and <see cref="GarmentJobStatus.OnHold"/> only,
    /// because section 3.2 names exactly those two — a confirmed job is not rescheduled through this command, and
    /// neither is a job that is already ready. The status does not move. The new date is evaluated in the branch
    /// timezone against the branch working calendar by the caller
    /// (<c>docs/architecture/conventions.md</c> section 2.2); this type never touches a clock or a calendar.
    /// </remarks>
    /// <param name="dueDate">The new promised date, already evaluated in the branch timezone.</param>
    /// <param name="reason">Why the date is moving. Required (section 8).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    internal Result Reschedule(DateOnly dueDate, string reason, DateTimeOffset now, Guid? by)
    {
        if (Status is not (GarmentJobStatus.InProduction or GarmentJobStatus.OnHold))
        {
            // The status does not move, so the refusal names the command rather than a target status — the same
            // shape Customers uses when it refuses a correction on a record that is not active.
            return Result.Failure(OrdersErrors.JobActionNotAllowed(Status, "rescheduled"));
        }

        var text = Reason(reason);
        if (text.IsFailure)
        {
            return Result.Failure(text.Error);
        }

        DueDate = dueDate;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Records the gate's verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only thing that can make <see cref="IsReadyForDelivery"/> true, and — with <see cref="Hold"/> and
    /// <see cref="Resume"/>, which only ever close it — one of the three writers of
    /// <see cref="ReadyStateComputedAt"/> and <see cref="ReadyStateBlocks"/> (INV-JOB-07). It moves
    /// <see cref="GarmentJobStatus.InProduction"/> to <see cref="GarmentJobStatus.Ready"/> when the verdict is
    /// ready, and <see cref="GarmentJobStatus.Ready"/> back to <see cref="GarmentJobStatus.InProduction"/> when it
    /// is not.
    /// </para>
    /// <para>
    /// <strong>A verdict of ready is refused when the job is no longer one that could be ready.</strong> Section
    /// 3.2 draws exactly one edge into <see cref="GarmentJobStatus.Ready"/> — from in production — so a job that
    /// was held, cancelled or confirmed since the facts were gathered has outlived the verdict. The refusal is
    /// <c>orders.ready-gate-outcome-stale</c> and nothing at all is written: materialising the flag and then
    /// declining the promotion, which is what this method used to do, published <c>ready_state = true</c> with an
    /// empty reason list on a garment nobody had begun and on a garment that was on hold — and
    /// <c>job_ready_state</c>, not the status, is what the delivery-team receive scan reads (section 4.1).
    /// </para>
    /// <para>
    /// <strong>A delivered or closed job is left exactly as it stands</strong>, verdict included: the garment has
    /// physically left, a gate recomputation can never pull it back (section 7), and overwriting the ready state
    /// it was dispatched under would destroy the evidence of why it was dispatched. A cancelled job is refused
    /// outright, because a verdict about a garment nobody is making says nothing at all.
    /// </para>
    /// </remarks>
    /// <param name="outcome">The verdict, which only <see cref="ReadyGate"/> can have produced.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    internal Result ApplyReadyGate(ReadyGateOutcome outcome, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.GarmentJobId != Id)
        {
            return Result.Failure(OrdersErrors.ReadyGateOutcomeForAnotherJob);
        }

        if (Status is GarmentJobStatus.Cancelled)
        {
            return Result.Failure(OrdersErrors.JobStatusTransitionNotAllowed(
                Status,
                outcome.IsReady ? GarmentJobStatus.Ready : GarmentJobStatus.InProduction));
        }

        // Not a refusal: the gate is recomputed on every custody event, and a garment handed over yesterday will
        // still be recomputed today. Succeeding without a change is what stops that routine recomputation from
        // rewriting a delivered job's history.
        if (Status is GarmentJobStatus.Delivered or GarmentJobStatus.Closed)
        {
            return Result.Success();
        }

        // The gate reads the job's pinned version and its hold when it evaluates, so a ready verdict reaching a
        // job that is neither in production nor already ready was reached about a job that has moved since.
        if (outcome.IsReady && Status is not (GarmentJobStatus.InProduction or GarmentJobStatus.Ready))
        {
            return Result.Failure(OrdersErrors.ReadyGateOutcomeStale);
        }

        RecordReadyState(outcome.IsReady, outcome.Blocks, outcome.EvaluatedAt);

        if (outcome.IsReady)
        {
            Status = GarmentJobStatus.Ready;
        }
        else if (Status is GarmentJobStatus.Ready)
        {
            Status = GarmentJobStatus.InProduction;
        }

        // The gate is the system, not a person, so the actor is cleared rather than carried over: the last change
        // to this row genuinely was nobody's.
        Touch(now, null);

        return Result.Success();
    }

    /// <summary>
    /// Records the doorstep handover.
    /// </summary>
    /// <remarks>
    /// Permitted from <see cref="GarmentJobStatus.Ready"/> only, and irreversible (section 7). Custody owns every
    /// precondition — a valid unexpired dispatch authorisation, an unchanged chain since dispatch, the recipient's
    /// OTP or signature — and this call records the result of them, never re-derives them.
    /// </remarks>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    internal Result ConfirmDelivery(DateTimeOffset now, Guid? by)
    {
        if (Status is not GarmentJobStatus.Ready)
        {
            return Result.Failure(OrdersErrors.JobStatusTransitionNotAllowed(Status, GarmentJobStatus.Delivered));
        }

        Status = GarmentJobStatus.Delivered;
        DeliveredAt = now;
        DeliveredBy = by;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Cancels the job against a configured reason code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Permitted from <see cref="GarmentJobStatus.Confirmed"/>, <see cref="GarmentJobStatus.InProduction"/>,
    /// <see cref="GarmentJobStatus.OnHold"/> and <see cref="GarmentJobStatus.Ready"/> (section 3.2). Nothing is
    /// removed: G-5 keeps the row, its snapshots, its dependencies and its history, and the prohibited financial,
    /// stock and custody states are checked by <c>Order.CancelJob</c> before this is reached (INV-ORD-06). The
    /// hold fields are left standing, because how long the garment waited is part of why it was cancelled.
    /// </para>
    /// <para>
    /// The ready state closes, and for the same reason <see cref="Hold"/>'s does: the delivery queue reads
    /// <c>job_ready_state</c>, a garment nobody is making must never appear on it, and
    /// <see cref="ApplyReadyGate"/> refuses a cancelled job outright — so a verdict left standing here is one
    /// nothing could ever close. No reason code is written with it: why the garment will not be delivered is the
    /// cancellation on the row, not a gate predicate.
    /// </remarks>
    /// <param name="reasonCode">The configured cancellation reason code.</param>
    /// <param name="reason">Why the garment is being cancelled. Required (section 8).</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    internal Result Cancel(string reasonCode, string reason, DateTimeOffset now, Guid? by)
    {
        if (Status is not (
            GarmentJobStatus.Confirmed
            or GarmentJobStatus.InProduction
            or GarmentJobStatus.OnHold
            or GarmentJobStatus.Ready))
        {
            return Result.Failure(OrdersErrors.JobStatusTransitionNotAllowed(Status, GarmentJobStatus.Cancelled));
        }

        var code = ReasonCode(reasonCode);
        if (code.IsFailure)
        {
            return Result.Failure(code.Error);
        }

        var text = Reason(reason);
        if (text.IsFailure)
        {
            return Result.Failure(text.Error);
        }

        Status = GarmentJobStatus.Cancelled;
        CancelledAt = now;
        CancelledBy = by;
        CancellationReasonCode = code.Value;
        CancellationReason = text.Value;
        RecordReadyState(false, [], now);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Validates a mandatory free-text reason. Never logged and never carried into a message
    /// (security rule 7); it is stored on the row and on the audit event.
    /// </summary>
    private static Result<string> Reason(string? reason)
    {
        var text = reason?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return Result.Failure<string>(OrdersErrors.ReasonRequired);
        }

        if (text.Length > MaximumReasonLength)
        {
            return Result.Failure<string>(OrdersErrors.TooLong("reason", MaximumReasonLength));
        }

        return Result.Success(text);
    }

    /// <summary>Validates a mandatory configured reason code.</summary>
    private static Result<string> ReasonCode(string? reasonCode)
    {
        var code = reasonCode?.Trim();

        if (string.IsNullOrEmpty(code))
        {
            return Result.Failure<string>(OrdersErrors.ReasonCodeRequired);
        }

        if (code.Length > MaximumReasonCodeLength)
        {
            return Result.Failure<string>(OrdersErrors.TooLong("reasonCode", MaximumReasonCodeLength));
        }

        return Result.Success(code);
    }

    /// <summary>
    /// The one place <c>job_ready_state</c> is written.
    /// </summary>
    /// <remarks>
    /// Private, and every caller of it is in this file, so the three fields move together and can never disagree
    /// — a true flag beside a block list, or a verdict beside the timestamp of a different one, is the drift
    /// CI-03 is about. <see cref="ApplyReadyGate"/> is the only caller that may pass <c>true</c>; the transitions
    /// section 3.2 says close the gate pass <c>false</c> and the reason the gate itself would give.
    /// </remarks>
    /// <param name="isReady">The verdict.</param>
    /// <param name="blocks">Why the gate is closed, in predicate order. Empty when it is open.</param>
    /// <param name="computedAt">When the verdict was reached.</param>
    private void RecordReadyState(bool isReady, IReadOnlyList<ReadyGateBlock> blocks, DateTimeOffset computedAt)
    {
        IsReadyForDelivery = isReady;
        ReadyStateComputedAt = computedAt;
        _readyStateBlocks.Clear();
        _readyStateBlocks.AddRange(blocks);
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
