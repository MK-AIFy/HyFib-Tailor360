using Tailor360.Modules.Orders.Domain.Jobs;

namespace Tailor360.Modules.Orders.Domain.Drafts;

/// <summary>
/// A dependency declared between two garment sections of one draft.
/// </summary>
/// <remarks>
/// <para>
/// Declared at the counter and carried into <c>job_dependencies</c> at confirmation, which is where it starts
/// having consequences: <see cref="JobDependencyKind.FinishBefore"/> blocks the dependent job's first phase and
/// <see cref="JobDependencyKind.DeliverTogether"/> binds siblings at the ready gate and in the delivery queue
/// (INV-JOB-09). While the draft is open it is only a note of what the customer asked for — the sari blouse
/// goes home with the sari — and removing the section it names removes it too.
/// </para>
/// <para>
/// A child of the garment section, so its factory is <c>internal static</c> and only
/// <see cref="OrderDraftGarment"/> can create one. It carries no concurrency token because it is append-only:
/// a dependency is declared or withdrawn, never edited.
/// </para>
/// </remarks>
public sealed class OrderDraftGarmentDependency
{
    /// <summary>
    /// The longest reason the column holds. Matches the reason length used across this module and
    /// <c>CustomerExport.MaximumReasonLength</c> before it.
    /// </summary>
    public const int MaximumReasonLength = 500;

    private OrderDraftGarmentDependency()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private OrderDraftGarmentDependency(
        Guid orderDraftGarmentId,
        Guid prerequisiteOrderDraftGarmentId,
        JobDependencyKind kind,
        string? reason,
        DateTimeOffset declaredAt,
        Guid? declaredBy)
    {
        OrderDraftGarmentId = orderDraftGarmentId;
        PrerequisiteOrderDraftGarmentId = prerequisiteOrderDraftGarmentId;
        Kind = kind;
        Reason = reason;
        DeclaredAt = declaredAt;
        DeclaredBy = declaredBy;
    }

    /// <summary>The dependent garment section.</summary>
    public Guid OrderDraftGarmentId { get; private set; }

    /// <summary>The section it depends on. Always another section of the same draft.</summary>
    public Guid PrerequisiteOrderDraftGarmentId { get; private set; }

    /// <summary>Which of the two declared relationships this is.</summary>
    public JobDependencyKind Kind { get; private set; }

    /// <summary>Why the dependency was declared, where a reason was given.</summary>
    public string? Reason { get; private set; }

    /// <summary>When it was declared, in UTC.</summary>
    public DateTimeOffset DeclaredAt { get; private set; }

    /// <summary>Who declared it.</summary>
    public Guid? DeclaredBy { get; private set; }

    /// <summary>Declares a dependency. Called by the draft aggregate, never directly.</summary>
    /// <param name="orderDraftGarmentId">The dependent section.</param>
    /// <param name="prerequisiteOrderDraftGarmentId">The section it depends on.</param>
    /// <param name="kind">Which relationship is being declared.</param>
    /// <param name="reason">The reason, already trimmed and length-checked by the caller.</param>
    /// <param name="declaredAt">The instant, from <c>IClock</c>.</param>
    /// <param name="declaredBy">The actor.</param>
    /// <returns>The row.</returns>
    internal static OrderDraftGarmentDependency Declare(
        Guid orderDraftGarmentId,
        Guid prerequisiteOrderDraftGarmentId,
        JobDependencyKind kind,
        string? reason,
        DateTimeOffset declaredAt,
        Guid? declaredBy)
        => new(orderDraftGarmentId, prerequisiteOrderDraftGarmentId, kind, reason, declaredAt, declaredBy);
}
