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
    /// The verdict; <c>OrdersErrors.GarmentJobNotFound</c> when the job is not on this order, or
    /// <c>OrdersErrors.JobStatusTransitionNotAllowed</c> when it is one the gate can reach no verdict about.
    /// </returns>
    public static Result<ReadyGateOutcome> Evaluate(
        Order order,
        Guid garmentJobId,
        ReadyGateInputs inputs,
        DateTimeOffset now)
    {
        // Checked in the order the parameters are declared, so the argument a caller is told about is the first
        // one that was wrong rather than the first one this overload happened to touch.
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(inputs);

        var outcomes = EvaluateSet(
            order,
            garmentJobId,
            new Dictionary<Guid, ReadyGateInputs> { [garmentJobId] = inputs },
            now);

        if (outcomes.IsFailure)
        {
            return Result.Failure<ReadyGateOutcome>(outcomes.Error);
        }

        // Asked rather than indexed: this method's contract is a Result, and a contract that is kept only by
        // construction is one a later change breaks into an InvalidOperationException.
        var outcome = outcomes.Value.FirstOrDefault(candidate => candidate.GarmentJobId == garmentJobId);
        if (outcome is not null)
        {
            return Result.Success(outcome);
        }

        // EvaluateSet has already found the garment on this order and been given its facts, so the one thing
        // left that can leave it without a verdict is that it cannot hold one. This overload answers for exactly
        // one garment and has no way to return no verdict, so it says why there is none — in the words the
        // applying side would have used — rather than handing back one nothing could ever accept.
        var named = order.FindJob(garmentJobId);

        return named is null
            ? Result.Failure<ReadyGateOutcome>(OrdersErrors.GarmentJobNotFound)
            : Result.Failure<ReadyGateOutcome>(
                OrdersErrors.JobStatusTransitionNotAllowed(named.Status, GarmentJobStatus.Ready));
    }

    /// <summary>
    /// Evaluates a garment job together with the <c>deliver_together</c> set it is bound into, returning one
    /// verdict per member whose facts were supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Binding means the set becomes ready together, not that each member waits for another to go
    /// first.</strong> INV-JOB-09 says a <c>deliver_together</c> dependency "binds jobs at the ready gate and in
    /// the delivery queue". Section 9.1 used to word the predicate as "<c>deliver_together</c> siblings are
    /// ready"; read as a test of the sibling's <em>status</em>, and evaluated one job at a time, those two
    /// sentences deadlock: of two bound garments both in production and both passing all six of their own
    /// predicates, each blocks on the other, <c>GarmentJob.ApplyReadyGate</c> is the only writer of ready, so
    /// neither can ever go first and the parcel can never be dispatched. Section 9.1 now reads "every other
    /// garment of the <c>deliver_together</c> parcel has met its own predicates in the same evaluation", which is
    /// what this method implements.
    /// </para>
    /// <para>
    /// <strong>Which is why the set, and not the job, is the unit.</strong> A sibling's own predicates are read
    /// from facts four other modules hold, and <see cref="ReadyGateInputs"/> carries them for one job — so a
    /// one-job-at-a-time gate cannot see them, however it is worded. The set's facts arrive together, every
    /// member is judged on its own six predicates, and a set whose members all pass gets a verdict of ready for
    /// every one of them in the same evaluation. Applying them is <c>Order.ApplyReadyGate</c>'s set overload,
    /// which promotes all or none; each verdict carries the rest of its parcel in
    /// <see cref="ReadyGateOutcome.BoundWith"/> so that the order can refuse to apply half of one.
    /// </para>
    /// <para>
    /// <strong>No seventh reason code.</strong> Section 9.1 has six, and a member that is waiting on a sibling
    /// still blocks on <see cref="ReadyGatePredicate.DependenciesMet"/> naming that sibling's job number, exactly
    /// as before. What changed is when it is raised, not what it says.
    /// </para>
    /// <para>
    /// <strong>The set is the connected component, and it is closed under the relation</strong> — the interim
    /// position of <strong>SQ-07</strong>, which is not settled and must not be presented as though it were. A
    /// garment bound to a second which is bound to a third is treated as one parcel of three: stopping at the
    /// direct siblings would let the first go while the third was still being made, which is the split
    /// INV-JOB-09 exists to prevent.
    /// </para>
    /// <para>
    /// <strong>A garment that is no longer deliverable is not in the parcel</strong> — the interim position of
    /// <strong>SQ-08</strong>, equally unsettled. A cancelled garment is one nobody is making and a delivered one
    /// has gone, so neither is a member, neither is a step on the way to one, and neither has a parcel of its own
    /// — see <see cref="BoundSet"/>.
    /// </para>
    /// <para>
    /// <strong>A member whose facts were not supplied blocks.</strong> It cannot be shown to have passed its own
    /// predicates, and the gate fails closed; the one exception is a member already standing at
    /// <see cref="GarmentJobStatus.Ready"/>, where the gate itself has already said so and that verdict is the
    /// domain's own record rather than a caller's claim. Facts supplied for a job outside the set are ignored:
    /// the set answers for itself.
    /// </para>
    /// <para>
    /// <strong>One parcel is evaluated under one branch dispatch policy</strong> — see
    /// <see cref="SharedDispatchPolicy"/>. Whether partial delivery is permitted is the branch's answer and not
    /// the garment's (issue #48), and a parcel answered two ways at once is refused rather than half bound.
    /// </para>
    /// <para>
    /// <strong>And a garment nobody is making gets no verdict at all</strong> — see
    /// <see cref="HoldsAVerdict"/>. Seeded at a cancelled garment this returns no verdicts rather than one the
    /// applying side could never accept, so a recomputation triggered on it applies nothing and refuses nothing.
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
    /// One verdict per member that can hold one and whose facts were supplied, in job-number order;
    /// <c>OrdersErrors.GarmentJobNotFound</c> when the named job is not on this order,
    /// <c>OrdersErrors.Required</c> when its own facts were not supplied, or
    /// <c>OrdersErrors.DispatchPolicyNotShared</c> when the parcel's facts carry two branch policies.
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

        var policy = SharedDispatchPolicy(members, inputs);
        if (policy.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ReadyGateOutcome>>(policy.Error);
        }

        // Every member's own five predicates first, because each member's DependenciesMet blocks are read off
        // the others' results rather than off their statuses. A member missing from this is one whose facts
        // nobody gathered, or one the gate can reach no verdict about at all.
        var own = new Dictionary<Guid, List<ReadyGateBlock>>(members.Count);

        foreach (var member in members)
        {
            if (HoldsAVerdict(member) && inputs.TryGetValue(member.Id, out var facts))
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

            outcomes.Add(ReadyGateOutcome.Of(
                member.Id,
                reported,
                BoundWith(member, members, inputs[member.Id]),
                now));
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
            blocks.Add(ReadyGateBlock.Of(ReadyGatePredicate.WorkflowComplete, inputs.IncompletePhaseCode));
        }

        // A pass with an open rework is not a pass. INV-JOB-06: the gate stays closed until a NEW result passes,
        // because a rework does not inherit the verdict the work it undid was given.
        if (!inputs.QcPassed || inputs.ReworkOpen)
        {
            blocks.Add(ReadyGateBlock.Of(ReadyGatePredicate.QcPassed, inputs.FailedQcReference));
        }

        if (!inputs.DocumentationComplete)
        {
            blocks.Add(ReadyGateBlock.Of(
                ReadyGatePredicate.DocumentationComplete,
                inputs.MissingEvidenceReference));
        }

        // Read from the job's own status rather than from the inputs, so a held job cannot be gated ready by an
        // application that gathered its facts before the hold was taken.
        if (job.Status is GarmentJobStatus.OnHold)
        {
            blocks.Add(ReadyGateBlock.Of(ReadyGatePredicate.NoOpenHold, job.HoldReasonCode));
        }

        // Unknown blocks. state-transitions.md section 9.1 is explicit that custody state which cannot be
        // established counts as blocked while the custody gate is enabled: the gate fails closed, because the
        // alternative is putting a garment nobody can locate on the delivery queue.
        if (inputs.CustodyGateEnabled && inputs.Custody is not CustodyReconciliation.Reconciled)
        {
            blocks.Add(ReadyGateBlock.Of(
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
                yield return ReadyGateBlock.Of(ReadyGatePredicate.DependenciesMet, sibling.JobNumber.Value);
            }
        }
    }

    /// <summary>
    /// The rest of the parcel this member's verdict has to be applied with, in job-number order.
    /// </summary>
    /// <remarks>
    /// Recorded on the verdict so that INV-JOB-09 binds on the applying side as well as on the evaluating one:
    /// <c>Order.ApplyReadyGate</c> refuses a verdict whose partners are neither applied with it nor already
    /// standing where it would put them. Empty where the branch policy permits partial delivery, which is the
    /// same waiver <see cref="SiblingBlocks"/> reads and read from the same member's own facts — a parcel is only
    /// bound at the gate while the policy says its garments travel together (issue #48).
    /// </remarks>
    private static List<Guid> BoundWith(GarmentJob job, List<GarmentJob> members, ReadyGateInputs inputs)
        => inputs.PartialDeliveryPermitted
            ? []
            : [.. members.Where(member => member.Id != job.Id).Select(member => member.Id)];

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
    /// Whether one branch dispatch policy answers for every member of the parcel whose facts were supplied.
    /// </summary>
    /// <remarks>
    /// Whether partial delivery is permitted is the <em>branch's</em> answer — <c>whole_order</c>,
    /// <c>per_job</c> or <c>exception</c>, issue #48 — and not a property of a garment, so it is the same answer
    /// for every garment of one parcel. It arrives per garment only because <see cref="ReadyGateInputs"/> carries
    /// the facts for one garment at a time, and a caller that answered it two ways got a parcel bound at one end
    /// and loose at the other: <see cref="BoundWith"/> reads the waiver from each member's own facts, so the
    /// member marked partial carried no binding at all and its verdict could then be recorded on its own while
    /// its partner stood in production. Refused rather than resolved — neither answer is the domain's to pick,
    /// and picking one would be the gate deciding a branch's dispatch policy for it.
    /// </remarks>
    private static Result SharedDispatchPolicy(
        List<GarmentJob> members,
        IReadOnlyDictionary<Guid, ReadyGateInputs> inputs)
    {
        bool? permitted = null;

        foreach (var member in members)
        {
            if (!inputs.TryGetValue(member.Id, out var facts))
            {
                continue;
            }

            permitted ??= facts.PartialDeliveryPermitted;

            if (permitted != facts.PartialDeliveryPermitted)
            {
                return Result.Failure(OrdersErrors.DispatchPolicyNotShared);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Whether the gate can reach a verdict about this garment at all.
    /// </summary>
    /// <remarks>
    /// A cancelled garment is one nobody is making, and <c>GarmentJob.CheckReadyGate</c> refuses a verdict about
    /// one outright. Reaching one here would hand the caller something the applying side can never accept: the
    /// gate is recomputed on every workflow, QC, hold, dependency and custody event (section 9.1), and an
    /// evaluate-then-apply recomputation therefore failed on every cancelled garment it was triggered for. The
    /// two sides say the same thing instead — no verdict is reached about it, and none is applied to it — and a
    /// verdict reached before the cancellation is still refused at the job, because by then it describes a
    /// garment that has moved.
    /// </remarks>
    private static bool HoldsAVerdict(GarmentJob job) => job.Status is not GarmentJobStatus.Cancelled;

    /// <summary>
    /// The garments bound to this one into a single parcel: the connected component of the
    /// <c>deliver_together</c> relation over the garments the order still owes, in job-number order, the named
    /// job included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closed under the relation rather than one step of it, because "these two go together" said twice over
    /// three garments is read as one promise about three garments (<strong>SQ-07</strong>, interim).
    /// </para>
    /// <para>
    /// <strong>Membership is symmetric, and the seed is a member on the same terms as anybody else.</strong> A
    /// garment that is no longer deliverable — cancelled, or already handed over — is not part of what the order
    /// still owes, so it is neither a member of a parcel nor a step on the way to one (<strong>SQ-08</strong>,
    /// interim). That has to hold of the seed too: adding the seed unconditionally while filtering its siblings
    /// made the parcel depend on which garment the caller happened to ask about, so a delivered garment was
    /// excluded when a live sibling seeded and a full member when it seeded itself — and could then raise
    /// <see cref="ReadyGatePredicate.DependenciesMet"/> against garments still in the shop, taking a finished
    /// garment off the delivery queue on the strength of one that had already left. A dead garment is therefore
    /// still returned — the caller asked about it and is owed a verdict — but it reaches nothing and nothing
    /// reaches it.
    /// </para>
    /// <para>
    /// <strong>The walk itself is the order's</strong> (<c>Order.DeliverTogetherParcelOf</c>). INV-JOB-09 binds a
    /// parcel at this gate <em>and</em> in the delivery queue, and <c>Order.ConfirmDelivery</c> asks the same
    /// question at the door; one traversal answers both, so SQ-07's and SQ-08's interim readings cannot come to
    /// mean one thing here and another there. All this adds is the seed, which the order leaves out and the gate
    /// owes a verdict to.
    /// </para>
    /// </remarks>
    private static List<GarmentJob> BoundSet(Order order, GarmentJob job)
    {
        var members = new List<GarmentJob> { job };
        members.AddRange(order.DeliverTogetherParcelOf(job.Id));

        members.Sort(static (left, right)
            => string.CompareOrdinal(left.JobNumber.Value, right.JobNumber.Value));

        return members;
    }
}
