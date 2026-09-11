using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// Everything one garment job needs to exist, validated before the confirmation transaction opens.
/// </summary>
/// <remarks>
/// <para>
/// A validated value object rather than a long parameter list on <c>Order.Confirm</c>, for the reason
/// <c>CustomerDetails</c> gives: adding a field later should not change every call site, and validation gets one
/// home. It also lets the order check the <em>whole set</em> — distinct identities, distinct indices, job numbers
/// minted from this order's own number, dependencies naming garments inside the confirmation — before anything is
/// created. INV-ORD-01 says confirmation is atomic across the whole order; the Domain's contribution to that is
/// that there is never a half-built order to persist.
/// </para>
/// <para>
/// The private non-positional constructor and the get-only properties are the shape convention [5] requires: a
/// positional record would publish a constructor, and an <c>init</c> setter would reopen the <c>with</c>
/// expression, either of which is a second way past <see cref="Create"/>.
/// </para>
/// </remarks>
public sealed record GarmentJobSpecification
{
    /// <summary>
    /// The longest category or service-type key the column holds. Matches Catalog's
    /// <c>CatalogCode.MaximumLength</c>, which is where these keys come from.
    /// </summary>
    public const int MaximumKeyLength = 40;

    private GarmentJobSpecification(
        Guid garmentJobId,
        GarmentJobNumber jobNumber,
        int jobIndex,
        string categoryKey,
        string serviceTypeKey,
        Guid workflowDefinitionId,
        MeasurementSnapshot measurements,
        DesignSnapshot design,
        PriceSnapshot price,
        DateOnly dueDate,
        IReadOnlyList<Guid> referenceMediaIds,
        IReadOnlyList<GarmentJobDependencySpecification> dependencies)
    {
        GarmentJobId = garmentJobId;
        JobNumber = jobNumber;
        JobIndex = jobIndex;
        CategoryKey = categoryKey;
        ServiceTypeKey = serviceTypeKey;
        WorkflowDefinitionId = workflowDefinitionId;
        Measurements = measurements;
        Design = design;
        Price = price;
        DueDate = dueDate;
        ReferenceMediaIds = referenceMediaIds;
        Dependencies = dependencies;
    }

    /// <summary>Identity the job will be created with. A UUIDv7, from <c>IIdGenerator</c> (ARCH-015).</summary>
    public Guid GarmentJobId { get; }

    /// <summary>
    /// <c>J-&lt;branch&gt;-&lt;FY&gt;-000001-01</c>, minted from the order's own number, so the relationship
    /// between a job number and its order is structural rather than a convention two call sites share.
    /// </summary>
    public GarmentJobNumber JobNumber { get; }

    /// <summary>The one-based position of this garment within the order. Must equal the job number's index.</summary>
    public int JobIndex { get; }

    /// <summary>The stitching category, as Catalog keys it.</summary>
    public string CategoryKey { get; }

    /// <summary>The service type within that category, as Catalog keys it.</summary>
    public string ServiceTypeKey { get; }

    /// <summary>
    /// The workflow <em>definition</em>, recorded at confirmation. The version is resolved and pinned at start of
    /// production and not here, because a job confirmed today and started next week runs the version published
    /// when the work begins (INV-JOB-02).
    /// </summary>
    public Guid WorkflowDefinitionId { get; }

    /// <summary>The measurement copy to freeze onto the job (INV-JOB-01).</summary>
    public MeasurementSnapshot Measurements { get; }

    /// <summary>The design copy to freeze onto the job (INV-JOB-01).</summary>
    public DesignSnapshot Design { get; }

    /// <summary>The priced result to freeze onto the job. Display and printing only (INV-ORD-07).</summary>
    public PriceSnapshot Price { get; }

    /// <summary>The promised date for this garment, evaluated in the branch timezone by the caller.</summary>
    public DateOnly DueDate { get; }

    /// <summary>
    /// Reference and material images, by Media id. Never a URL and never an object key (security rule 9).
    /// </summary>
    public IReadOnlyList<Guid> ReferenceMediaIds { get; }

    /// <summary>What this garment waits for, or is delivered with, inside the same order.</summary>
    public IReadOnlyList<GarmentJobDependencySpecification> Dependencies { get; }

    /// <summary>
    /// Validates one garment of a confirmation.
    /// </summary>
    /// <remarks>
    /// Returns the <strong>first</strong> failure found rather than an accumulated list, which is the module-wide
    /// shape (convention [5]). What this type cannot see, it does not pretend to check: whether a prerequisite
    /// names a garment inside the same confirmation, whether two garments were handed the same dependency-row
    /// identity, and whether a set of garments waits for itself round a circle are all <c>Order.Confirm</c>'s
    /// questions, because only the order holds the whole set (INV-JOB-09). What it <em>can</em> see it checks:
    /// the category and service type on the row agree with the design copy being frozen beside them.
    /// </remarks>
    /// <param name="garmentJobId">Identity to give the job, from <c>IIdGenerator</c>.</param>
    /// <param name="jobNumber">The job number minted from the order's number.</param>
    /// <param name="jobIndex">The one-based position of this garment within the order.</param>
    /// <param name="categoryKey">The stitching category key.</param>
    /// <param name="serviceTypeKey">The service type key.</param>
    /// <param name="workflowDefinitionId">The workflow definition recorded at confirmation.</param>
    /// <param name="measurements">The measurement copy to freeze.</param>
    /// <param name="design">The design copy to freeze.</param>
    /// <param name="price">The priced result to freeze.</param>
    /// <param name="dueDate">The promised date for this garment.</param>
    /// <param name="referenceMediaIds">Reference and material images, by Media id.</param>
    /// <param name="dependencies">Dependencies to declare as the job is created.</param>
    /// <returns>The specification, or the first failure found.</returns>
    public static Result<GarmentJobSpecification> Create(
        Guid garmentJobId,
        GarmentJobNumber jobNumber,
        int jobIndex,
        string? categoryKey,
        string? serviceTypeKey,
        Guid workflowDefinitionId,
        MeasurementSnapshot measurements,
        DesignSnapshot design,
        PriceSnapshot price,
        DateOnly dueDate,
        IReadOnlyCollection<Guid>? referenceMediaIds,
        IReadOnlyCollection<GarmentJobDependencySpecification>? dependencies)
    {
        ArgumentNullException.ThrowIfNull(jobNumber);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(price);

        if (garmentJobId == Guid.Empty)
        {
            return Result.Failure<GarmentJobSpecification>(OrdersErrors.Required("garmentJobId"));
        }

        if (jobIndex < DisplayNumberFormat.MinimumJobIndex)
        {
            return Result.Failure<GarmentJobSpecification>(OrdersErrors.JobIndexOutOfRange);
        }

        // The job number carries its own index and the job carries one too, because the column is what an
        // ordering query reads. Letting the two disagree prints one number on the job card and sorts the garment
        // under another on the workboard, and nothing downstream can tell which of the two was meant.
        if (jobIndex != jobNumber.JobIndex)
        {
            return Result.Failure<GarmentJobSpecification>(OrdersErrors.JobIndexOutOfRange);
        }

        var category = categoryKey?.Trim();
        if (string.IsNullOrEmpty(category))
        {
            return Result.Failure<GarmentJobSpecification>(OrdersErrors.Required("categoryKey"));
        }

        if (category.Length > MaximumKeyLength)
        {
            return Result.Failure<GarmentJobSpecification>(
                OrdersErrors.TooLong("categoryKey", MaximumKeyLength));
        }

        var serviceType = serviceTypeKey?.Trim();
        if (string.IsNullOrEmpty(serviceType))
        {
            return Result.Failure<GarmentJobSpecification>(OrdersErrors.Required("serviceTypeKey"));
        }

        if (serviceType.Length > MaximumKeyLength)
        {
            return Result.Failure<GarmentJobSpecification>(
                OrdersErrors.TooLong("serviceTypeKey", MaximumKeyLength));
        }

        // The row and the frozen design copy answer the same two questions, and both are read: the row is
        // what a workboard filters and reports group by, the snapshot is what the job card renders from. A
        // job whose row says `blouse` while its design copy says `shirt` is a disagreement INV-JOB-01
        // freezes permanently — the snapshot is immutable after confirmation and the catalogue cannot be
        // republished into it — so no later reader can resolve it and no command can correct it. Both are
        // inside one validated value object here, so the check costs nothing.
        if (!string.Equals(category, design.CategoryKey, StringComparison.Ordinal))
        {
            return Result.Failure<GarmentJobSpecification>(
                OrdersErrors.DesignSnapshotNotForThisGarment("categoryKey"));
        }

        if (!string.Equals(serviceType, design.ServiceTypeKey, StringComparison.Ordinal))
        {
            return Result.Failure<GarmentJobSpecification>(
                OrdersErrors.DesignSnapshotNotForThisGarment("serviceTypeKey"));
        }

        if (workflowDefinitionId == Guid.Empty)
        {
            return Result.Failure<GarmentJobSpecification>(OrdersErrors.Required("workflowDefinitionId"));
        }

        var declared = new List<GarmentJobDependencySpecification>();

        foreach (var dependency in dependencies ?? [])
        {
            var checkedDependency = Check(dependency, garmentJobId, declared);
            if (checkedDependency.IsFailure)
            {
                return Result.Failure<GarmentJobSpecification>(checkedDependency.Error);
            }

            declared.Add(checkedDependency.Value);
        }

        return Result.Success(new GarmentJobSpecification(
            garmentJobId,
            jobNumber,
            jobIndex,
            category,
            serviceType,
            workflowDefinitionId,
            measurements,
            design,
            price,
            dueDate,
            Distinct(referenceMediaIds),
            declared));
    }

    /// <summary>
    /// Validates one declared dependency against the job it belongs to and the ones already accepted.
    /// </summary>
    private static Result<GarmentJobDependencySpecification> Check(
        GarmentJobDependencySpecification dependency,
        Guid garmentJobId,
        List<GarmentJobDependencySpecification> accepted)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        if (dependency.DependencyId == Guid.Empty)
        {
            return Result.Failure<GarmentJobDependencySpecification>(OrdersErrors.Required("dependencyId"));
        }

        if (dependency.PrerequisiteGarmentJobId == Guid.Empty)
        {
            return Result.Failure<GarmentJobDependencySpecification>(OrdersErrors.Required("prerequisite"));
        }

        // A garment that must finish before itself never starts, and a garment delivered with itself binds the
        // queue to a job that can never become ready. Both are screen defects, and neither is recoverable at the
        // gate — by then the order is confirmed and the only remedy is a new one.
        if (dependency.PrerequisiteGarmentJobId == garmentJobId)
        {
            return Result.Failure<GarmentJobDependencySpecification>(OrdersErrors.DependencyOnItself);
        }

        if (accepted.Exists(held =>
                held.PrerequisiteGarmentJobId == dependency.PrerequisiteGarmentJobId
                && held.Kind == dependency.Kind))
        {
            return Result.Failure<GarmentJobDependencySpecification>(OrdersErrors.DuplicateDependency);
        }

        var reason = dependency.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            reason = null;
        }
        else if (reason.Length > JobDependency.MaximumReasonLength)
        {
            return Result.Failure<GarmentJobDependencySpecification>(
                OrdersErrors.TooLong("reason", JobDependency.MaximumReasonLength));
        }

        return Result.Success(dependency with { Reason = reason });
    }

    /// <summary>
    /// Drops empty identifiers and repeats, preserving the order the caller gave — because the order the images
    /// arrive in is the order the job card prints them in, and a tailor reads the first one as the main reference.
    /// </summary>
    private static List<Guid> Distinct(IReadOnlyCollection<Guid>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var kept = new List<Guid>(values.Count);

        foreach (var value in values)
        {
            if (value != Guid.Empty && !kept.Contains(value))
            {
                kept.Add(value);
            }
        }

        return kept;
    }
}

/// <summary>
/// One dependency to declare on a garment job as it is created.
/// </summary>
/// <remarks>
/// A positional record because it carries no rule of its own: <see cref="GarmentJobSpecification.Create"/>
/// validates it, and <c>Order.Confirm</c> checks that the prerequisite names a garment inside the same
/// confirmation (INV-JOB-09). Nothing reaches a <see cref="JobDependency"/> row without passing both.
/// </remarks>
/// <param name="DependencyId">Identity to give the row, from <c>IIdGenerator</c>.</param>
/// <param name="PrerequisiteGarmentJobId">The job this one depends on. Another job of the same order.</param>
/// <param name="Kind">Which relationship this is, and therefore which gate enforces it.</param>
/// <param name="Reason">Why it was declared, where a reason was given.</param>
public sealed record GarmentJobDependencySpecification(
    Guid DependencyId,
    Guid PrerequisiteGarmentJobId,
    JobDependencyKind Kind,
    string? Reason);
