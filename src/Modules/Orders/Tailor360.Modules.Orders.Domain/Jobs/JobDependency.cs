namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// One declared dependency of a garment job on another job of the same order.
/// </summary>
/// <remarks>
/// <para>
/// A child entity of <see cref="GarmentJob"/>: its factory is <see langword="internal" />, so only the aggregate
/// in this assembly can create one, and it carries no concurrency token because it is append-only — a dependency
/// is a record of something that was declared, so it is never edited
/// (<c>docs/architecture/conventions.md</c>, optimistic concurrency).
/// </para>
/// <para>
/// <strong>The prerequisite is always another job of the same order</strong> (INV-JOB-09). That is checked where
/// the whole set is visible — <c>GarmentJobSpecification.Create</c> for a dependency on the job itself, and
/// <c>Order.Confirm</c> for one naming a garment outside the confirmation — because a single row cannot see the
/// order it belongs to and would have had to trust its caller.
/// </para>
/// </remarks>
public sealed class JobDependency
{
    /// <summary>
    /// The longest reason the column holds. Matches <c>CustomerExport.MaximumReasonLength</c>, which is the
    /// repository's precedent for a free-text reason on a record.
    /// </summary>
    public const int MaximumReasonLength = 500;

    private JobDependency()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private JobDependency(
        Guid id,
        Guid garmentJobId,
        Guid prerequisiteGarmentJobId,
        JobDependencyKind kind,
        string? reason,
        DateTimeOffset declaredAt,
        Guid? declaredBy)
    {
        Id = id;
        GarmentJobId = garmentJobId;
        PrerequisiteGarmentJobId = prerequisiteGarmentJobId;
        Kind = kind;
        Reason = reason;
        DeclaredAt = declaredAt;
        DeclaredBy = declaredBy;
    }

    /// <summary>Identity of this dependency row. A UUIDv7.</summary>
    public Guid Id { get; private set; }

    /// <summary>The dependent job — the one that waits, or that is delivered with the other.</summary>
    public Guid GarmentJobId { get; private set; }

    /// <summary>The job it depends on. Always another job of the same order.</summary>
    public Guid PrerequisiteGarmentJobId { get; private set; }

    /// <summary>Which relationship this is, and therefore which gate enforces it.</summary>
    public JobDependencyKind Kind { get; private set; }

    /// <summary>Why the dependency was declared, where a reason was given.</summary>
    public string? Reason { get; private set; }

    /// <summary>When it was declared.</summary>
    public DateTimeOffset DeclaredAt { get; private set; }

    /// <summary>Who declared it, where a person did.</summary>
    public Guid? DeclaredBy { get; private set; }

    /// <summary>Declares a dependency. Called by the garment job, never directly.</summary>
    /// <param name="id">Identity, from <c>IIdGenerator</c>.</param>
    /// <param name="garmentJobId">The dependent job.</param>
    /// <param name="prerequisiteGarmentJobId">The job it depends on.</param>
    /// <param name="kind">Which relationship this is.</param>
    /// <param name="reason">Why it was declared, where a reason was given.</param>
    /// <param name="declaredAt">The instant, from <c>IClock</c>.</param>
    /// <param name="declaredBy">The actor, where a person declared it.</param>
    /// <returns>The row.</returns>
    internal static JobDependency Declare(
        Guid id,
        Guid garmentJobId,
        Guid prerequisiteGarmentJobId,
        JobDependencyKind kind,
        string? reason,
        DateTimeOffset declaredAt,
        Guid? declaredBy)
        => new(id, garmentJobId, prerequisiteGarmentJobId, kind, reason, declaredAt, declaredBy);
}
