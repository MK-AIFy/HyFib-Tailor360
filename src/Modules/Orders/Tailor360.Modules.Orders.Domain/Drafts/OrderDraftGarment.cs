using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Drafts;

/// <summary>
/// One garment section of an order draft.
/// </summary>
/// <remarks>
/// <para>
/// A child entity of <see cref="OrderDraft"/>: its factory and every mutator are <c>internal</c>, so only the
/// draft can create or change one and every change goes through the aggregate that owns the section set.
/// </para>
/// <para>
/// <strong>The content is flattened onto the row</strong> exactly as <c>Customer</c> flattens
/// <c>CustomerDetails</c>, so a section is one row with its own concurrency token. That is what makes the
/// per-garment <c>If-Match</c> lock genuine: a draft is shared within the branch
/// (<c>docs/prd/state-transitions.md</c> section 2.1), two people commonly build one between them, and
/// last-writer-wins over the whole draft would lose a garment that nobody notices until the tailor does
/// (<c>docs/architecture/conventions.md</c> section 4.2).
/// </para>
/// <para>
/// <strong>It becomes a garment job, and only then.</strong> Nothing here is a snapshot: the design selections
/// live in Catalog behind <see cref="DesignSelectionDraftId"/> and the measurements live in Customers behind
/// <see cref="MeasurementVersionId"/>. Both are copied onto the job at confirmation and are immutable from that
/// moment (INV-JOB-01).
/// </para>
/// </remarks>
public sealed class OrderDraftGarment
{
    private readonly List<Guid> _referenceMediaIds = [];
    private readonly List<OrderDraftGarmentDependency> _dependencies = [];

    private OrderDraftGarment()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private OrderDraftGarment(
        Guid id,
        Guid orderDraftId,
        int position,
        OrderDraftGarmentContent content,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrderDraftId = orderDraftId;
        Position = position;

        Apply(content, now, by);
    }

    /// <summary>Identity of this garment section. A UUIDv7, and what the per-section lock is taken on.</summary>
    public Guid Id { get; private set; }

    /// <summary>The draft the section belongs to.</summary>
    public Guid OrderDraftId { get; private set; }

    /// <summary>
    /// Display order within the draft, one-based.
    /// </summary>
    /// <remarks>
    /// Never renumbered when a section is removed. The job index that ends up in the job number is assigned at
    /// confirmation (<c>docs/architecture/conventions.md</c> section 3.2), so a gap here is only a gap on a
    /// screen, and renumbering would move a garment out from under somebody editing it on another device.
    /// </remarks>
    public int Position { get; private set; }

    /// <summary>The garment category, as a catalogue key.</summary>
    public string CategoryKey { get; private set; } = string.Empty;

    /// <summary>The service type within that category, as a catalogue key.</summary>
    public string ServiceTypeKey { get; private set; } = string.Empty;

    /// <summary>The catalogue version this section is pinned to for its life.</summary>
    public Guid CatalogVersionId { get; private set; }

    /// <summary>The Catalog-owned design selection draft, by id.</summary>
    public Guid? DesignSelectionDraftId { get; private set; }

    /// <summary>What the section says about its measurements.</summary>
    public MeasurementIntent MeasurementIntent { get; private set; }

    /// <summary>The confirmed measurement version being reused, where one is named.</summary>
    public Guid? MeasurementVersionId { get; private set; }

    /// <summary>The template the garment will be measured against, where it is known.</summary>
    public Guid? MeasurementTemplateId { get; private set; }

    /// <summary>The promised date for this garment, evaluated in the branch timezone by the caller.</summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>What Reception typed that is not any option. Never priced.</summary>
    public string? Instructions { get; private set; }

    /// <summary>When the section was last written to, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last wrote to it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Reference and material images, by Media id. Never a URL and never an object key.</summary>
    public IReadOnlyList<Guid> ReferenceMediaIds => _referenceMediaIds;

    /// <summary>What this section must wait for, or be delivered with.</summary>
    public IReadOnlyCollection<OrderDraftGarmentDependency> Dependencies => _dependencies;

    /// <summary>
    /// True when the section has a measurement decision that confirmation can act on.
    /// </summary>
    /// <remarks>
    /// A named confirmed version and nothing else. "Take now" and "take later" are plans, and a plan is not a
    /// measurement: a garment still carrying one when somebody presses confirm is exactly the garment the
    /// field error must name (<c>docs/prd/state-transitions.md</c> section 2.1,
    /// <c>docs/IMPLEMENTATION_PLAN.md</c> #32b). Both routes end here — the capture wizard writes the version
    /// it confirmed back onto the section as an explicit reuse — so reading the version rather than the intent
    /// answers the question for every route at once.
    /// </remarks>
    public bool HasMeasurementDecision => MeasurementVersionId is not null;

    /// <summary>
    /// The content as it stands, so a caller can copy this section into a new one.
    /// </summary>
    /// <remarks>
    /// What <strong>duplicate garment</strong> is built on (<c>docs/IMPLEMENTATION_PLAN.md</c> #32b): the
    /// caller takes the content, replaces what the duplicate must not inherit — the measurement decision
    /// above all, because a second garment is not measured by copying the first — and adds it as a new
    /// section. Returned as a <c>Result</c> rather than the value itself because the content revalidates, and
    /// a section stored before a limit tightened must say so rather than silently produce an invalid copy.
    /// </remarks>
    /// <returns>The validated content, or the reason it could not be read back.</returns>
    public Result<OrderDraftGarmentContent> ToContent()
        => OrderDraftGarmentContent.Create(
            CategoryKey,
            ServiceTypeKey,
            CatalogVersionId,
            DesignSelectionDraftId,
            MeasurementIntent,
            MeasurementVersionId,
            MeasurementTemplateId,
            DueDate,
            Instructions,
            _referenceMediaIds);

    /// <summary>Adds a garment section. Called by the draft aggregate, never directly.</summary>
    /// <param name="id">Identity of the section, from <c>IIdGenerator</c>.</param>
    /// <param name="orderDraftId">The draft it belongs to.</param>
    /// <param name="position">Its one-based display order within the draft.</param>
    /// <param name="content">The validated content.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>The section.</returns>
    internal static OrderDraftGarment Add(
        Guid id,
        Guid orderDraftId,
        int position,
        OrderDraftGarmentContent content,
        DateTimeOffset now,
        Guid? by)
        => new(id, orderDraftId, position, content, now, by);

    /// <summary>
    /// Replaces everything the section says about the garment, leaving its identity, its position and its
    /// dependencies alone.
    /// </summary>
    /// <param name="content">The validated new content.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    internal void Apply(OrderDraftGarmentContent content, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(content);

        CategoryKey = content.CategoryKey;
        ServiceTypeKey = content.ServiceTypeKey;
        CatalogVersionId = content.CatalogVersionId;
        DesignSelectionDraftId = content.DesignSelectionDraftId;
        MeasurementIntent = content.MeasurementIntent;
        MeasurementVersionId = content.MeasurementVersionId;
        MeasurementTemplateId = content.MeasurementTemplateId;
        DueDate = content.DueDate;
        Instructions = content.Instructions;

        // Replaced rather than merged, because a merge has no way to say "this image is no longer attached" —
        // the same reason MeasurementDraft.SaveSection replaces a section rather than merging into it.
        _referenceMediaIds.Clear();
        _referenceMediaIds.AddRange(content.ReferenceMediaIds);

        Touch(now, by);
    }

    /// <summary>
    /// Declares that this section waits for, or is delivered with, another section of the same draft.
    /// </summary>
    /// <remarks>
    /// Membership of the draft is checked by the aggregate, which is the only thing that knows the section
    /// set; what is checked here is what only the section knows — that it is not naming itself, and that it is
    /// not declaring the same relationship twice.
    /// </remarks>
    /// <param name="prerequisiteOrderDraftGarmentId">The section this one depends on.</param>
    /// <param name="kind">Which relationship is being declared.</param>
    /// <param name="reason">Why, where a reason was given.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the declaration was refused.</returns>
    internal Result DeclareDependency(
        Guid prerequisiteOrderDraftGarmentId,
        JobDependencyKind kind,
        string? reason,
        DateTimeOffset now,
        Guid? by)
    {
        // A garment that waits for itself can never start, and a garment delivered together with itself says
        // nothing at all. Both are a screen defect rather than anything about the draft.
        if (prerequisiteOrderDraftGarmentId == Id)
        {
            return Result.Failure(OrdersErrors.DependencyOnItself);
        }

        // INV-JOB-09 names two kinds and enforces them in two different places, each testing for its own named
        // member. A row whose kind is neither is carried into the confirmation and then kept by no gate at all,
        // so it is refused where it is declared rather than where it is silently ignored.
        if (!Enum.IsDefined(kind))
        {
            return Result.Failure(OrdersErrors.NotUnderstood("kind"));
        }

        if (_dependencies.Exists(held =>
                held.PrerequisiteOrderDraftGarmentId == prerequisiteOrderDraftGarmentId
                && held.Kind == kind))
        {
            return Result.Failure(OrdersErrors.DuplicateDependency);
        }

        var why = reason?.Trim();
        if (string.IsNullOrEmpty(why))
        {
            why = null;
        }
        else if (why.Length > OrderDraftGarmentDependency.MaximumReasonLength)
        {
            return Result.Failure(
                OrdersErrors.TooLong("reason", OrderDraftGarmentDependency.MaximumReasonLength));
        }

        _dependencies.Add(OrderDraftGarmentDependency.Declare(
            Id,
            prerequisiteOrderDraftGarmentId,
            kind,
            why,
            now,
            by));

        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Withdraws a dependency this section declared.</summary>
    /// <param name="prerequisiteOrderDraftGarmentId">The section it named.</param>
    /// <param name="kind">The relationship that was declared.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the withdrawal was refused.</returns>
    internal Result WithdrawDependency(
        Guid prerequisiteOrderDraftGarmentId,
        JobDependencyKind kind,
        DateTimeOffset now,
        Guid? by)
    {
        var removed = _dependencies.RemoveAll(held =>
            held.PrerequisiteOrderDraftGarmentId == prerequisiteOrderDraftGarmentId
            && held.Kind == kind);

        if (removed == 0)
        {
            return Result.Failure(OrdersErrors.DependencyNotDeclared);
        }

        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Withdraws every dependency naming a section that no longer exists.
    /// </summary>
    /// <remarks>
    /// Called by the draft when a section is removed. A dependency pointing at a section nobody can see is not
    /// a refusal to surface at confirmation — it is a row that would name a garment the order does not have,
    /// so it goes with the section it named rather than outliving it.
    /// </remarks>
    /// <param name="removedOrderDraftGarmentId">The section that has gone.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>How many dependencies were withdrawn.</returns>
    internal int WithdrawDependenciesNaming(
        Guid removedOrderDraftGarmentId,
        DateTimeOffset now,
        Guid? by)
    {
        var removed = _dependencies.RemoveAll(held =>
            held.PrerequisiteOrderDraftGarmentId == removedOrderDraftGarmentId);

        if (removed > 0)
        {
            Touch(now, by);
        }

        return removed;
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
