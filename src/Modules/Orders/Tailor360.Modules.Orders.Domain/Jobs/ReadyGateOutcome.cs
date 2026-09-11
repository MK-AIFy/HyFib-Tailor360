namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// The ready-for-delivery gate's verdict on one garment job.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This type is how INV-JOB-07 is enforced by the compiler rather than by review.</strong> Its constructor
/// is private and its only factory is <see langword="internal" />, so nothing outside this assembly can fabricate
/// one — and <c>Order.ApplyReadyGate</c> accepts nothing else. No Application, Api or host code can therefore hand
/// the order a verdict of ready that <see cref="ReadyGate"/> did not reach, which is exactly what
/// <c>docs/prd/raci.md</c> row 16 asks for: no role, however senior, declares a garment ready.
/// </para>
/// <para>
/// <see cref="IsReady"/> is derived from <see cref="Blocks"/> and is never supplied, so "no blocks" and "ready"
/// cannot disagree. A verdict that carried both a block list and an independent boolean would have been one
/// mis-ordered assignment away from dispatching a garment that had failed QC.
/// </para>
/// </remarks>
public sealed record ReadyGateOutcome
{
    private ReadyGateOutcome(
        Guid garmentJobId,
        bool isReady,
        IReadOnlyList<ReadyGateBlock> blocks,
        DateTimeOffset evaluatedAt)
    {
        GarmentJobId = garmentJobId;
        IsReady = isReady;
        Blocks = blocks;
        EvaluatedAt = evaluatedAt;
    }

    /// <summary>
    /// The job this verdict is about. <c>Order.ApplyReadyGate</c> refuses an outcome evaluated for a different job
    /// with <c>OrdersErrors.ReadyGateOutcomeForAnotherJob</c>.
    /// </summary>
    public Guid GarmentJobId { get; }

    /// <summary>True when no predicate blocked.</summary>
    public bool IsReady { get; }

    /// <summary>Every predicate that blocked, in <see cref="ReadyGatePredicate"/> order.</summary>
    public IReadOnlyList<ReadyGateBlock> Blocks { get; }

    /// <summary>When the gate was evaluated, in UTC.</summary>
    public DateTimeOffset EvaluatedAt { get; }

    /// <summary>
    /// Builds a verdict. Internal, and called only by <see cref="ReadyGate"/>.
    /// </summary>
    /// <param name="garmentJobId">The job the gate was evaluated for.</param>
    /// <param name="blocks">Every predicate that blocked, in predicate order. Empty means ready.</param>
    /// <param name="evaluatedAt">The instant, from <c>IClock</c>.</param>
    /// <returns>The verdict.</returns>
    internal static ReadyGateOutcome Of(
        Guid garmentJobId,
        IReadOnlyList<ReadyGateBlock> blocks,
        DateTimeOffset evaluatedAt)
        => new(garmentJobId, blocks.Count == 0, blocks, evaluatedAt);
}
