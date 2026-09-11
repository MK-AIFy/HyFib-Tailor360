using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// The ready-for-delivery gate: the only producer of a <see cref="ReadyGateOutcome"/>, and therefore the only
/// writer of a garment job's ready state.
/// </summary>
/// <remarks>
/// <para>
/// <strong>INV-JOB-07 lives here.</strong> <c>docs/prd/state-transitions.md</c> section 9.1 says the gate is the
/// only writer of <c>garment_jobs.ready_state</c>, and <c>docs/prd/raci.md</c> row 16 says no role, however
/// senior, can declare a garment ready. Both are enforced by the type system rather than by review:
/// <see cref="ReadyGateOutcome"/> has a private constructor and an <see langword="internal" /> factory that only
/// this class calls, <c>GarmentJob.ApplyReadyGate</c> is itself internal, and <c>Order.ApplyReadyGate</c> accepts
/// nothing but an outcome.
/// </para>
/// <para>
/// <strong>A static class rather than a method on the job</strong>, because the verdict is a function of the job,
/// its <c>deliver_together</c> siblings and facts from four other modules — and because keeping it outside the
/// aggregate is what stops a job method quietly becoming a second writer. A method here cannot mutate anything:
/// it returns a verdict, and the order decides what to do with it.
/// </para>
/// <para>
/// <strong>Every predicate returns its own reason code</strong>, in <see cref="ReadyGatePredicate"/> order, and
/// every one is evaluated even after the first has blocked. A gate that stopped at the first failure would send a
/// Tailor Master to finish a phase, then back for the QC, then back again for the hold.
/// </para>
/// <para>
/// <strong>It fails closed, and the facts it can see itself it reads itself.</strong> Section 9.1's predicates are
/// written over a job that is in production — <c>WorkflowComplete</c> is defined over "the pinned workflow
/// version", and section 3.2 draws exactly one edge into ready, from in production. The job's pinned version and
/// its hold are therefore read off the job rather than taken from <see cref="ReadyGateInputs"/>: the application
/// gathers those facts from other modules before it calls, and a garment can move in between.
/// </para>
/// </remarks>
public static class ReadyGate
{
    /// <summary>
    /// Evaluates the six predicates for one garment job of an order.
    /// </summary>
    /// <param name="order">The order the job belongs to, which also answers the dependency predicate.</param>
    /// <param name="garmentJobId">The job to evaluate.</param>
    /// <param name="inputs">The facts the Orders domain does not itself hold.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>
    /// The verdict, or <c>OrdersErrors.GarmentJobNotFound</c> when the job is not on this order.
    /// </returns>
    public static Result<ReadyGateOutcome> Evaluate(
        Order order,
        Guid garmentJobId,
        ReadyGateInputs inputs,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(inputs);

        var job = order.FindJob(garmentJobId);
        if (job is null)
        {
            return Result.Failure<ReadyGateOutcome>(OrdersErrors.GarmentJobNotFound);
        }

        var blocks = new List<ReadyGateBlock>();

        // Section 9.1 defines this predicate over "every non-skippable phase of **the pinned workflow version**",
        // and a job that has not started production has no pinned version and no phases at all — so it cannot
        // satisfy the predicate however confident the caller's facts are. Read from the job rather than trusted
        // to the inputs for the same reason NoOpenHold is: without it, a caller that passed
        // `workflowComplete: true` for a garment nobody had begun cutting got a verdict of ready, and section
        // 4.1's receive scan reads exactly that verdict.
        if (!job.HasEnteredProduction || job.WorkflowVersionId is null || !inputs.WorkflowComplete)
        {
            blocks.Add(new ReadyGateBlock(ReadyGatePredicate.WorkflowComplete, inputs.IncompletePhaseCode));
        }

        // A pass with an open rework is not a pass. INV-JOB-06: the gate stays closed until a NEW result passes,
        // because a rework does not inherit the verdict the work it undid was given.
        if (!inputs.QcPassed || inputs.ReworkOpen)
        {
            blocks.Add(new ReadyGateBlock(ReadyGatePredicate.QcPassed, inputs.FailedQcReference));
        }

        if (!inputs.DocumentationComplete)
        {
            blocks.Add(new ReadyGateBlock(
                ReadyGatePredicate.DocumentationComplete,
                inputs.MissingEvidenceReference));
        }

        // Read from the job's own status rather than from the inputs, so a held job cannot be gated ready by an
        // application that gathered its facts before the hold was taken.
        if (job.Status is GarmentJobStatus.OnHold)
        {
            blocks.Add(new ReadyGateBlock(ReadyGatePredicate.NoOpenHold, job.HoldReasonCode));
        }

        blocks.AddRange(SiblingBlocks(order, garmentJobId, inputs));

        // Unknown blocks. state-transitions.md section 9.1 is explicit that custody state which cannot be
        // established counts as blocked while the custody gate is enabled: the gate fails closed, because the
        // alternative is putting a garment nobody can locate on the delivery queue.
        if (inputs.CustodyGateEnabled && inputs.Custody is not CustodyReconciliation.Reconciled)
        {
            blocks.Add(new ReadyGateBlock(
                ReadyGatePredicate.CustodyReconciled,
                inputs.OpenCustodyCaseReference));
        }

        return Result.Success(ReadyGateOutcome.Of(job.Id, blocks, now));
    }

    /// <summary>
    /// The <c>DependenciesMet</c> blocks: one per sibling that is not yet finished, in job-number order.
    /// </summary>
    /// <remarks>
    /// One block per blocking sibling rather than one for the predicate, so the queue screen names all of them and
    /// the branch can see the whole parcel it is waiting on (INV-JOB-09). A cancelled sibling never blocks — it is
    /// no longer part of what the order owes — and the predicate is waived entirely where the branch policy
    /// permits partial delivery (issue #48).
    /// </remarks>
    private static List<ReadyGateBlock> SiblingBlocks(
        Order order,
        Guid garmentJobId,
        ReadyGateInputs inputs)
    {
        if (inputs.PartialDeliveryPermitted)
        {
            return [];
        }

        return order.DeliverTogetherSiblingsOf(garmentJobId)
            .Where(sibling => sibling.Status is not (
                GarmentJobStatus.Ready
                or GarmentJobStatus.Delivered
                or GarmentJobStatus.Closed
                or GarmentJobStatus.Cancelled))
            .OrderBy(sibling => sibling.JobNumber.Value, StringComparer.Ordinal)
            .Select(sibling => new ReadyGateBlock(
                ReadyGatePredicate.DependenciesMet,
                sibling.JobNumber.Value))
            .ToList();
    }
}
