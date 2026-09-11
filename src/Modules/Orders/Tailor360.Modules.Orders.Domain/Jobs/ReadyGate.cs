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
/// <para>
/// <strong>The unit of evaluation is the bound set, not the job</strong> (INV-JOB-09) — see
/// <see cref="EvaluateSet"/> for why, and <see cref="Evaluate"/> for the one-garment case it collapses to.
/// </para>
/// </remarks>
public static class ReadyGate
{
    /// <summary>
    /// Evaluates one garment job, gathering no facts about its <c>deliver_together</c> siblings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The overwhelmingly common case — a garment bound to nothing — and there it is exactly
    /// <see cref="EvaluateSet"/> over a set of one. Where the garment <em>is</em> bound, a sibling whose facts
    /// were not gathered cannot be shown to have passed its own predicates, so it blocks: the gate fails closed
    /// here as it does on unknown custody. A caller that wants the set to be able to go together has to gather
    /// the set's facts and call <see cref="EvaluateSet"/>.
    /// </para>
    /// </remarks>
    /// <param name="order">The order the job belongs to, which also answers the dependency predicate.</param>
    /// <param name="garmentJobId">The job to evaluate.</param>
    /// <param name="inputs">The facts the Orders domain does not itself hold, for this job.</param>
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
        ArgumentNullException.ThrowIfNull(inputs);

        var outcomes = EvaluateSet(
            order,
            garmentJobId,
            new Dictionary<Guid, ReadyGateInputs> { [garmentJobId] = inputs },
            now);

        return outcomes.IsFailure
            ? Result.Failure<ReadyGateOutcome>(outcomes.Error)
            : Result.Success(outcomes.Value.First(outcome => outcome.GarmentJobId == garmentJobId));
    }

    /// <summary>
    /// Evaluates a garment job together with the <c>deliver_together</c> set it is bound into, returning one
    /// verdict per member whose facts were supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Binding means the set becomes ready together, not that each member waits for another to go
    /// first.</strong> INV-JOB-09 says a <c>deliver_together</c> dependency "binds jobs at the ready gate and in
    /// the delivery queue", and section 9.1 words the predicate as "<c>deliver_together</c> siblings are ready".
    /// Read as a test of the sibling's <em>status</em>, and evaluated one job at a time, those two sentences
    /// deadlock: of two bound garments both in production and both passing all six of their own predicates, each
    /// blocks on the other, <c>GarmentJob.ApplyReadyGate</c> is the only writer of ready, so neither can ever go
    /// first and the parcel can never be dispatched. The invariant is the authority over the predicate's wording
    /// (CLAUDE.md section 8), and the reading that satisfies it is that a sibling blocks when <em>the sibling
    /// itself</em> fails its own predicates.
    /// </para>
    /// <para>
    /// <strong>Which is why the set, and not the job, is the unit.</strong> A sibling's own predicates are read
    /// from facts four other modules hold, and <see cref="ReadyGateInputs"/> carries them for one job — so a
    /// one-job-at-a-time gate cannot see them, however it is worded. The set's facts arrive together, every
    /// member is judged on its own six predicates, and a set whose members all pass gets a verdict of ready for
    /// every one of them in the same evaluation. Applying them is <c>Order.ApplyReadyGate</c>'s set overload,
    /// which promotes all or none.
    /// </para>
    /// <para>
    /// <strong>No seventh reason code.</strong> Section 9.1 has six, and a member that is waiting on a sibling
    /// still blocks on <see cref="ReadyGatePredicate.DependenciesMet"/> naming that sibling's job number, exactly
    /// as before. What changed is when it is raised, not what it says.
    /// </para>
    /// <para>
    /// <strong>The set is the connected component, and it is closed under the relation.</strong> A garment bound
    /// to a second which is bound to a third is in one parcel of three: stopping at the direct siblings would let
    /// the first go while the third was still being made, which is the split INV-JOB-09 exists to prevent. A
    /// cancelled garment is not in the parcel and does not carry the promise onward either — nobody is making it
    /// — and a garment already delivered has gone, so neither is a member and neither is traversed through.
    /// </para>
    /// <para>
    /// <strong>A member whose facts were not supplied blocks.</strong> It cannot be shown to have passed its own
    /// predicates, and the gate fails closed; the one exception is a member already standing at
    /// <see cref="GarmentJobStatus.Ready"/>, where the gate itself has already said so and that verdict is the
    /// domain's own record rather than a caller's claim. Facts supplied for a job outside the set are ignored:
    /// the set answers for itself.
    /// </para>
    /// </remarks>
    /// <param name="order">The order the jobs belong to.</param>
    /// <param name="garmentJobId">The job whose bound set is to be evaluated.</param>
    /// <param name="inputs">
    /// The facts the Orders domain does not itself hold, by garment job. Must carry the job named by
    /// <paramref name="garmentJobId"/>; every other member it carries is evaluated too.
    /// </param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>
    /// One verdict per member whose facts were supplied, in job-number order;
    /// <c>OrdersErrors.GarmentJobNotFound</c> when the named job is not on this order, or
    /// <c>OrdersErrors.Required</c> when its own facts were not supplied.
    /// </returns>
    public static Result<IReadOnlyList<ReadyGateOutcome>> EvaluateSet(
        Order order,
        Guid garmentJobId,
        IReadOnlyDictionary<Guid, ReadyGateInputs> inputs,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(inputs);

        var job = order.FindJob(garmentJobId);
        if (job is null)
        {
            return Result.Failure<IReadOnlyList<ReadyGateOutcome>>(OrdersErrors.GarmentJobNotFound);
        }

        if (!inputs.ContainsKey(garmentJobId))
        {
            return Result.Failure<IReadOnlyList<ReadyGateOutcome>>(OrdersErrors.Required("inputs"));
        }

        var members = BoundSet(order, job);

        // Every member's own five predicates first, because each member's DependenciesMet blocks are read off
        // the others' results rather than off their statuses. A member missing from this is one whose facts
        // nobody gathered.
        var own = new Dictionary<Guid, List<ReadyGateBlock>>(members.Count);

        foreach (var member in members)
        {
            if (inputs.TryGetValue(member.Id, out var facts))
            {
                own[member.Id] = OwnBlocks(member, facts);
            }
        }

        var outcomes = new List<ReadyGateOutcome>(own.Count);

        foreach (var member in members)
        {
            if (!own.TryGetValue(member.Id, out var blocks))
            {
                continue;
            }

            // DependenciesMet is predicate 4 of 6, so the sibling blocks sit between the hold and custody and
            // the whole list still reads in ReadyGatePredicate order, as INV-JOB-07 asks.
            var reported = new List<ReadyGateBlock>(blocks.Count + members.Count);
            reported.AddRange(blocks.Where(block => block.Predicate < ReadyGatePredicate.DependenciesMet));
            reported.AddRange(SiblingBlocks(member, members, own, inputs[member.Id]));
            reported.AddRange(blocks.Where(block => block.Predicate > ReadyGatePredicate.DependenciesMet));

            outcomes.Add(ReadyGateOutcome.Of(member.Id, reported, now));
        }

        return Result.Success<IReadOnlyList<ReadyGateOutcome>>(outcomes);
    }

    /// <summary>
    /// The five predicates one garment answers for itself, in <see cref="ReadyGatePredicate"/> order.
    /// </summary>
    /// <remarks>
    /// Everything except <see cref="ReadyGatePredicate.DependenciesMet"/>, which is the only predicate that is
    /// about somebody else. Separating them is what lets a sibling be judged on its own merits rather than on
    /// whether it has already been promoted.
    /// </remarks>
    private static List<ReadyGateBlock> OwnBlocks(GarmentJob job, ReadyGateInputs inputs)
    {
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

        // Unknown blocks. state-transitions.md section 9.1 is explicit that custody state which cannot be
        // established counts as blocked while the custody gate is enabled: the gate fails closed, because the
        // alternative is putting a garment nobody can locate on the delivery queue.
        if (inputs.CustodyGateEnabled && inputs.Custody is not CustodyReconciliation.Reconciled)
        {
            blocks.Add(new ReadyGateBlock(
                ReadyGatePredicate.CustodyReconciled,
                inputs.OpenCustodyCaseReference));
        }

        return blocks;
    }

    /// <summary>
    /// The <c>DependenciesMet</c> blocks: one per member of the bound set that has not satisfied its own
    /// predicates, in job-number order.
    /// </summary>
    /// <remarks>
    /// One block per blocking sibling rather than one for the predicate, so the queue screen names all of them
    /// and the branch can see the whole parcel it is waiting on (INV-JOB-09). The predicate is waived entirely
    /// where the branch policy permits partial delivery (issue #48), and it asks about the sibling's own
    /// predicates rather than about its status — asking about the status is what deadlocked a bound pair, since
    /// the status it asked about is written by this gate and by nothing else (INV-JOB-07).
    /// </remarks>
    private static IEnumerable<ReadyGateBlock> SiblingBlocks(
        GarmentJob job,
        List<GarmentJob> members,
        Dictionary<Guid, List<ReadyGateBlock>> own,
        ReadyGateInputs inputs)
    {
        if (inputs.PartialDeliveryPermitted)
        {
            yield break;
        }

        foreach (var sibling in members)
        {
            if (sibling.Id != job.Id && !IsSatisfied(sibling, own))
            {
                yield return new ReadyGateBlock(ReadyGatePredicate.DependenciesMet, sibling.JobNumber.Value);
            }
        }
    }

    /// <summary>
    /// Whether one member of the bound set has met everything that is asked of it alone.
    /// </summary>
    /// <remarks>
    /// A member whose facts arrived is judged on them. A member whose facts did not is blocked — the gate fails
    /// closed — unless it already stands at <see cref="GarmentJobStatus.Ready"/>, which is this gate's own last
    /// verdict on it and not a caller's claim (INV-JOB-07).
    /// </remarks>
    private static bool IsSatisfied(GarmentJob member, Dictionary<Guid, List<ReadyGateBlock>> own)
        => own.TryGetValue(member.Id, out var blocks)
            ? blocks.Count == 0
            : member.Status is GarmentJobStatus.Ready;

    /// <summary>
    /// The garments bound to this one into a single parcel: the connected component of the
    /// <c>deliver_together</c> relation, in job-number order, the named job included.
    /// </summary>
    /// <remarks>
    /// Closed under the relation rather than one step of it, because "these two go together" said twice over
    /// three garments is one promise about three garments. A garment that is no longer deliverable — cancelled,
    /// or already handed over — is neither a member nor a step on the way to one: it is not part of what the
    /// order still owes, so binding the parcel to it would leave the finished garments unable to reach the
    /// customer at all.
    /// </remarks>
    private static List<GarmentJob> BoundSet(Order order, GarmentJob job)
    {
        var members = new List<GarmentJob> { job };
        var seen = new HashSet<Guid> { job.Id };
        var frontier = new Queue<GarmentJob>();
        frontier.Enqueue(job);

        while (frontier.Count > 0)
        {
            foreach (var sibling in order.DeliverTogetherSiblingsOf(frontier.Dequeue().Id))
            {
                if (sibling.IsDeliverable && seen.Add(sibling.Id))
                {
                    members.Add(sibling);
                    frontier.Enqueue(sibling);
                }
            }
        }

        members.Sort(static (left, right)
            => string.CompareOrdinal(left.JobNumber.Value, right.JobNumber.Value));

        return members;
    }
}
