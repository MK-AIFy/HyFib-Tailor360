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
        IReadOnlyList<Guid> boundWith,
        DateTimeOffset evaluatedAt)
    {
        GarmentJobId = garmentJobId;
        IsReady = isReady;
        Blocks = blocks;
        BoundWith = boundWith;
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

    /// <summary>
    /// The other garments of the <c>deliver_together</c> parcel this verdict was reached inside, in job-number
    /// order. Empty for a garment bound to nothing, and empty where the branch policy permits partial delivery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is INV-JOB-09 carried from the evaluating side to the applying side.</strong> The gate judges
    /// the whole parcel in one evaluation (<see cref="ReadyGate.EvaluateSet"/>); without a record of what the
    /// parcel was, <c>Order.ApplyReadyGate</c> could take one member's verdict and leave its partner where it
    /// stood, which is half a parcel on the delivery queue bound to a garment still being made. The order refuses
    /// a verdict of ready with <c>OrdersErrors.ReadyGateWouldSplitParcel</c> unless every garment named here
    /// will stand at ready once the command is written — applied in the same command, or already there. A
    /// verdict that is <em>not</em> ready is recorded whatever its partners are doing: taking a garment off the
    /// delivery queue leaves no half parcel on it.
    /// </para>
    /// <para>
    /// A garment that is no longer deliverable is not named, because it is not in the parcel (SQ-08).
    /// </para>
    /// </remarks>
    public IReadOnlyList<Guid> BoundWith { get; }

    /// <summary>When the gate was evaluated, in UTC.</summary>
    public DateTimeOffset EvaluatedAt { get; }

    /// <summary>
    /// Builds a verdict. Internal, and called only by <see cref="ReadyGate"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The parcel is copied, and copied into something that cannot be emptied again.</strong> A verdict
    /// whose <see cref="BoundWith"/> is the very <c>List&lt;Guid&gt;</c> the gate built is one a single cast
    /// outside this assembly can clear — and an emptied parcel is not a fabricated verdict but a disarmed guard,
    /// which is worse, because <c>Order.ApplyReadyGate</c> would then find nothing to refuse. The whole premise of
    /// this type is that what leaves the gate is what reaches the order, so the copy is taken here rather than
    /// trusted to every producer, exactly as the snapshots this module freezes take theirs.
    /// </para>
    /// <para>
    /// <strong>And the reasons on the same terms.</strong> They cost less than the parcel does — a cleared block
    /// list loses the reasons a queue screen shows rather than a guard, because <see cref="IsReady"/> is read
    /// from the count at construction and does not change when the list behind it does — but a verdict whose two
    /// lists are published one way and held another is one a reader has to check twice. Both are copied, and
    /// both are published as something that cannot be written to.
    /// </para>
    /// </remarks>
    /// <param name="garmentJobId">The job the gate was evaluated for.</param>
    /// <param name="blocks">Every predicate that blocked, in predicate order. Empty means ready.</param>
    /// <param name="boundWith">The rest of the parcel this verdict has to be applied with (INV-JOB-09).</param>
    /// <param name="evaluatedAt">The instant, from <c>IClock</c>.</param>
    /// <returns>The verdict.</returns>
    internal static ReadyGateOutcome Of(
        Guid garmentJobId,
        IReadOnlyList<ReadyGateBlock> blocks,
        IReadOnlyList<Guid> boundWith,
        DateTimeOffset evaluatedAt)
    {
        List<ReadyGateBlock> reasons = [.. blocks];
        List<Guid> parcel = [.. boundWith];

        return new ReadyGateOutcome(
            garmentJobId,
            reasons.Count == 0,
            reasons.AsReadOnly(),
            parcel.AsReadOnly(),
            evaluatedAt);
    }
}
