using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// Ready state and its single writer: <c>docs/prd/state-transitions.md</c> section 9.1 and INV-JOB-07.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/prd/raci.md</c> row 16 is explicit that no role, however senior, can declare a garment ready.
/// The domain carries that in the type system rather than in a review comment: the only argument that
/// moves ready state is a <see cref="ReadyGateOutcome"/>, whose constructor is private and whose only
/// factory is internal to <see cref="ReadyGate"/>. <see cref="AReadyVerdictCannotBeFabricated"/> is what
/// fails the day either of those is opened up.
/// </para>
/// <para>
/// Each predicate is tested on its own as well as together, because a gate that closes for the wrong
/// reason is a queue screen telling a tailor to fix something that is not broken.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class GarmentJobReadyStateTests
{
    private static readonly Guid Blouse = OrdersTestData.GarmentId(1);

    private static readonly Guid Skirt = OrdersTestData.GarmentId(2);

    private static readonly Guid Trousers = OrdersTestData.GarmentId(3);

    /* The single writer ------------------------------------------------------------------------- */

    /// <summary>
    /// No Application, Api or host code can make a verdict of ready, so no caller can promote a garment
    /// however many permissions they hold.
    /// </summary>
    [Fact]
    public void AReadyVerdictCannotBeFabricated()
    {
        typeof(ReadyGateOutcome)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty();

        typeof(ReadyGateOutcome)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ShouldBeEmpty();
    }

    [Fact]
    public void TheGatePromotesAGarmentFromProductionWhenEveryPredicatePasses()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);

        var applied = RunGate(order, Blouse);

        var job = order.Jobs.Single();
        applied.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.Ready);
        job.IsReadyForDelivery.ShouldBeTrue();
        job.ReadyStateBlocks.ShouldBeEmpty();
        job.ReadyStateComputedAt.ShouldBe(OrdersTestData.Now);
    }

    /// <summary>
    /// A confirmed garment has no pinned workflow version, so section 9.1's <c>WorkflowComplete</c> — "every
    /// non-skippable phase of <strong>the pinned workflow version</strong> is complete" — cannot be satisfied
    /// however confident the caller's facts are. The status and the materialised state are both asserted,
    /// because the materialised state is what the delivery-team receive scan reads (section 4.1) and it is the
    /// one that used to come out true with no reason shown.
    /// </summary>
    [Fact]
    public void AReadyVerdictNeverPromotesAGarmentThatHasNotStartedProduction()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var applied = RunGate(order, Blouse);

        var job = order.Jobs.Single();
        applied.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.Confirmed);
        job.IsReadyForDelivery.ShouldBeFalse();
        job.ReadyStateBlocks.Select(block => block.Predicate).ShouldBe([ReadyGatePredicate.WorkflowComplete]);
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    /// <summary>
    /// The same thing said at the gate rather than at the job: a garment nobody has begun cutting is not
    /// given a verdict of ready in the first place, whatever the application gathered.
    /// </summary>
    [Fact]
    public void TheGateRefusesToCallAGarmentReadyBeforeAWorkflowVersionIsPinned()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(), OrdersTestData.Now);

        order.FindJob(Blouse)!.WorkflowVersionId.ShouldBeNull();
        outcome.Value.IsReady.ShouldBeFalse();
        outcome.Value.Blocks.ShouldHaveSingleItem().Predicate.ShouldBe(ReadyGatePredicate.WorkflowComplete);
    }

    /// <summary>
    /// The facts reach the gate from four other modules, so a garment can be held between the gathering and
    /// the applying. A verdict of ready that arrives at a held garment is refused outright rather than
    /// written: it would otherwise materialise ready state with an empty reason list over the top of the
    /// hold's own block.
    /// </summary>
    [Fact]
    public void AVerdictReachedBeforeAHoldIsRefusedWhenItArrivesAfterOne()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        var gathered = ReadyGate.Evaluate(order, Blouse, Inputs(), OrdersTestData.Now);
        gathered.Value.IsReady.ShouldBeTrue();

        Hold(order, Blouse);

        var applied = order.ApplyReadyGate(
            Blouse,
            gathered.Value,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now.AddMinutes(1));

        var job = order.Jobs.Single();
        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-outcome-stale");
        job.Status.ShouldBe(GarmentJobStatus.OnHold);
        job.IsReadyForDelivery.ShouldBeFalse();
        job.ReadyStateBlocks.ShouldHaveSingleItem().Predicate.ShouldBe(ReadyGatePredicate.NoOpenHold);
    }

    /// <summary>
    /// <c>Order.Cancel</c> deliberately does not cascade into the garments (SQ-04), so a cancelled order
    /// still holds jobs in production — and section 2.1 says there is no un-cancel while section 3.2 draws no
    /// row that makes such a garment ready. The gate is therefore refused at the order, because a promotion
    /// here is a delivery queue entry and a parcel leaving the shop for an order nobody is making.
    /// </summary>
    [Fact]
    public void TheGateCannotPromoteAGarmentOfACancelledOrder()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        order.Cancel(
            "customer-withdrew",
            "The customer withdrew the order at the counter.",
            [],
            OrdersTestData.Now,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var applied = RunGate(order, Blouse);

        var job = order.Jobs.Single();
        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.status-transition-not-allowed");
        job.Status.ShouldBe(GarmentJobStatus.InProduction);
        job.IsReadyForDelivery.ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    /// <summary>
    /// An order-scoped refusal names the order's own status and never a garment's: section 2 is explicit
    /// that <c>on_hold</c> is a garment job status and that an order is never in it.
    /// </summary>
    [Fact]
    public void AnOrderScopedRefusalNeverNamesAGarmentStatus()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        order.Cancel(
            "customer-withdrew",
            "The customer withdrew the order at the counter.",
            [],
            OrdersTestData.Now,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var refused = Hold(order, Blouse);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Message.ShouldBe("This order is cancelled, so nothing further can be recorded against it.");
        refused.Error.Message.ShouldNotContain("OnHold");
    }

    /// <summary>
    /// The order's status follows its garments and is never written by hand either: a garment that was
    /// promoted by the gate is what makes the order ready (SQ-02, interim).
    /// </summary>
    [Fact]
    public void TheOrdersOwnStatusFollowsTheGateRatherThanACaller()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        order.Status.ShouldBe(OrderStatus.InProduction);

        RunGate(order, Blouse);

        order.Status.ShouldBe(OrderStatus.Ready);
    }

    /// <summary>
    /// Ready is not a one-way door while the garment is still in the shop: a later recomputation that
    /// fails takes the garment back to production and the queue entry with it.
    /// </summary>
    [Fact]
    public void AGarmentThatStopsPassingTheGateGoesBackToProduction()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.Ready(order, Blouse);

        var applied = RunGate(order, Blouse, OrdersTestData.GateInputs(qcPassed: false));

        var job = order.Jobs.Single();
        applied.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.InProduction);
        job.IsReadyForDelivery.ShouldBeFalse();
        job.ReadyStateBlocks.Select(block => block.Predicate).ShouldBe([ReadyGatePredicate.QcPassed]);
    }

    /* The six predicates ------------------------------------------------------------------------ */

    [Fact]
    public void AnIncompleteWorkflowClosesTheGateAndNamesThePhase()
    {
        var blocks = Evaluate(Inputs(workflowComplete: false));

        var block = blocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.WorkflowComplete);
        block.Reference.ShouldBe("finishing");
    }

    [Fact]
    public void AFailedQualityControlResultClosesTheGateAndNamesTheResult()
    {
        var blocks = Evaluate(Inputs(qcPassed: false));

        var block = blocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.QcPassed);
        block.Reference.ShouldBe("QC-000114");
    }

    /// <summary>
    /// INV-JOB-06: a pass with an open rework is not a pass. The gate stays closed until a <em>new</em>
    /// result passes, because a rework does not inherit the verdict the work it undid was given.
    /// </summary>
    [Fact]
    public void APassedResultWithAnOpenReworkIsNotAPass()
    {
        var blocks = Evaluate(Inputs(qcPassed: true, reworkOpen: true));

        blocks.ShouldHaveSingleItem().Predicate.ShouldBe(ReadyGatePredicate.QcPassed);
    }

    [Fact]
    public void MissingEvidenceClosesTheGateAndNamesWhatIsMissing()
    {
        var blocks = Evaluate(Inputs(documentationComplete: false));

        var block = blocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.DocumentationComplete);
        block.Reference.ShouldBe("finishing-photograph");
    }

    /// <summary>
    /// The hold is read from the garment's own status and not from the inputs, so an application that
    /// gathered its facts before the hold was taken cannot gate a held garment ready.
    /// </summary>
    [Fact]
    public void AnOpenHoldClosesTheGateEvenWhenTheInputsSayEverythingPassed()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        order.Hold(
            Blouse,
            "awaiting-material",
            "The lining has not arrived.",
            OrdersTestData.Approver,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(), OrdersTestData.Now);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Value.IsReady.ShouldBeFalse();
        var block = outcome.Value.Blocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.NoOpenHold);
        block.Reference.ShouldBe("awaiting-material");
    }

    /// <summary>
    /// INV-JOB-09. The two garments go to the customer together, so one of them being ready on its own
    /// is not a garment that may be queued.
    /// </summary>
    [Fact]
    public void ASiblingThatGoesToTheCustomerTogetherClosesTheGateAndNamesItsNumber()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);

        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(partialDeliveryPermitted: false), OrdersTestData.Now);

        var block = outcome.Value.Blocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.DependenciesMet);
        block.Reference.ShouldBe(order.FindJob(Skirt)!.JobNumber.Value);
    }

    [Fact]
    public void ABranchThatPermitsPartialDeliveryIsNotBlockedByItsSiblings()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);

        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(partialDeliveryPermitted: true), OrdersTestData.Now);

        outcome.Value.IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// The relationship is symmetric even though the row is not: "this garment goes with that one" is the
    /// same promise read from either end, so the garment that was named is blocked as well as the one
    /// that declared it.
    /// </summary>
    [Fact]
    public void TheSiblingThatWasNamedIsBlockedJustAsTheOneThatNamedItIs()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Skirt);

        var outcome = ReadyGate.Evaluate(order, Skirt, Inputs(partialDeliveryPermitted: false), OrdersTestData.Now);

        var block = outcome.Value.Blocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.DependenciesMet);
        block.Reference.ShouldBe(order.FindJob(Blouse)!.JobNumber.Value);
    }

    /// <summary>
    /// A sibling that has reached ready is what the promise asked for, so it stops blocking the moment it
    /// does — the pair can now go to the customer together.
    /// </summary>
    [Fact]
    public void ASiblingThatHasReachedReadyStopsBlockingItsPartner()
    {
        var order = BoundPair();
        OrdersTestData.Ready(order, Skirt);
        OrdersTestData.InProduction(order, Blouse);

        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(partialDeliveryPermitted: false), OrdersTestData.Now);

        outcome.Value.IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// A cancelled sibling is not waited for. Nobody is making that garment, so binding the queue to it
    /// would leave the one that is finished unable to reach the customer at all.
    /// </summary>
    [Fact]
    public void ACancelledSiblingDoesNotHoldItsPartnerBack()
    {
        var order = BoundPair();
        order.CancelJob(
            Skirt,
            "customer-changed-mind",
            "The customer withdrew this garment.",
            [],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);
        OrdersTestData.InProduction(order, Blouse);

        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(partialDeliveryPermitted: false), OrdersTestData.Now);

        outcome.Value.IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// Section 9.1: custody state that cannot be established counts as blocked while the custody gate is
    /// enabled. <strong>The gate fails closed</strong>, because the alternative is putting a garment
    /// nobody can locate on the delivery queue.
    /// </summary>
    [Theory]
    [InlineData(CustodyReconciliation.NotReconciled)]
    [InlineData(CustodyReconciliation.Unknown)]
    public void CustodyThatIsNotReconciledOrIsSimplyUnknownClosesTheGate(CustodyReconciliation custody)
    {
        var blocks = Evaluate(Inputs(custodyGateEnabled: true, custody: custody));

        var block = blocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.CustodyReconciled);
        block.Reference.ShouldBe("CASE-000021");
    }

    [Fact]
    public void CustodyIsNotAskedAboutAtABranchWhereTheCustodyGateIsOff()
    {
        var blocks = Evaluate(Inputs(custodyGateEnabled: false, custody: CustodyReconciliation.Unknown));

        blocks.ShouldBeEmpty();
    }

    /// <summary>
    /// Every predicate returns its own reason code, and they are reported in
    /// <see cref="ReadyGatePredicate"/> order so a queue screen reads the same way every time.
    /// </summary>
    [Fact]
    public void EveryFailingPredicateIsReportedAndTheyComeInPredicateOrder()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        order.Hold(
            Blouse,
            "awaiting-material",
            "The lining has not arrived.",
            OrdersTestData.Approver,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        var outcome = ReadyGate.Evaluate(
            order,
            Blouse,
            Inputs(
                workflowComplete: false,
                qcPassed: false,
                documentationComplete: false,
                partialDeliveryPermitted: false,
                custodyGateEnabled: true,
                custody: CustodyReconciliation.Unknown),
            OrdersTestData.Now);

        outcome.Value.IsReady.ShouldBeFalse();
        outcome.Value.Blocks.Select(block => block.Predicate).ShouldBe(
        [
            ReadyGatePredicate.WorkflowComplete,
            ReadyGatePredicate.QcPassed,
            ReadyGatePredicate.DocumentationComplete,
            ReadyGatePredicate.NoOpenHold,
            ReadyGatePredicate.DependenciesMet,
            ReadyGatePredicate.CustodyReconciled,
        ]);
    }

    /* The bound set ----------------------------------------------------------------------------- */

    /// <summary>
    /// INV-JOB-09 binds <c>deliver_together</c> garments at the ready gate, and binding means the set becomes
    /// ready <em>together</em>. Asked of each garment on its own, against the sibling's status, the promise
    /// deadlocks: each of two garments in production blocks on the other, the gate is the only writer of ready
    /// (INV-JOB-07), so neither can go first and the parcel can never be dispatched. This is the test that
    /// fails the day the predicate goes back to reading a sibling's status.
    /// </summary>
    [Fact]
    public void TwoGarmentsBoundTogetherReachReadyTogetherRatherThanWaitingForEachOther()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);

        var outcomes = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);

        outcomes.IsSuccess.ShouldBeTrue();
        outcomes.Value.Count.ShouldBe(2);
        outcomes.Value.ShouldAllBe(outcome => outcome.IsReady);

        var applied = order.ApplyReadyGate(
            outcomes.Value,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsSuccess.ShouldBeTrue();
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.Ready);
        order.Jobs.ShouldAllBe(job => job.IsReadyForDelivery);
        order.Status.ShouldBe(OrderStatus.Ready);
    }

    /// <summary>
    /// What replaced "the sibling is ready": a sibling blocks on <em>its own</em> failing predicates. The
    /// reason code is still <c>DependenciesMet</c> naming the sibling's number — section 9.1 has six reason
    /// codes and this did not make a seventh — and the sibling reports the predicate it is actually failing,
    /// so the queue screen sends the Tailor Master to the garment that needs work.
    /// </summary>
    [Fact]
    public void ASiblingBlocksOnItsOwnFailingPredicatesRatherThanOnItsStatus()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);

        var outcomes = ReadyGate.EvaluateSet(
            order,
            Blouse,
            BoundFacts(skirt: Inputs(qcPassed: false, partialDeliveryPermitted: false)),
            OrdersTestData.Now);

        var forBlouse = outcomes.Value.Single(outcome => outcome.GarmentJobId == Blouse);
        var forSkirt = outcomes.Value.Single(outcome => outcome.GarmentJobId == Skirt);

        var waiting = forBlouse.Blocks.ShouldHaveSingleItem();
        waiting.Predicate.ShouldBe(ReadyGatePredicate.DependenciesMet);
        waiting.Reference.ShouldBe(order.FindJob(Skirt)!.JobNumber.Value);
        forSkirt.Blocks.ShouldHaveSingleItem().Predicate.ShouldBe(ReadyGatePredicate.QcPassed);
    }

    /// <summary>
    /// The gate fails closed here as it does on unknown custody: a sibling whose facts nobody gathered cannot
    /// be shown to have passed its own predicates, so it blocks. A caller that wants the parcel to be able to
    /// go has to gather the parcel's facts.
    /// </summary>
    [Fact]
    public void ASiblingWhoseFactsWereNotGatheredBlocksRatherThanBeingAssumedToPass()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);

        var outcome = ReadyGate.Evaluate(
            order,
            Blouse,
            Inputs(partialDeliveryPermitted: false),
            OrdersTestData.Now);

        outcome.Value.IsReady.ShouldBeFalse();
        var block = outcome.Value.Blocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.DependenciesMet);
        block.Reference.ShouldBe(order.FindJob(Skirt)!.JobNumber.Value);
    }

    /// <summary>
    /// "These two go together" said twice over three garments is one promise about three garments. Stopping at
    /// the garments a row names directly would let the first of a chain go while the last was still being made,
    /// which is the split parcel INV-JOB-09 exists to prevent.
    /// </summary>
    [Fact]
    public void AParcelBoundInAChainWaitsForEveryGarmentInIt()
    {
        var order = BoundChain();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        OrdersTestData.InProduction(order, Trousers);

        var outcomes = ReadyGate.EvaluateSet(
            order,
            Blouse,
            new Dictionary<Guid, ReadyGateInputs>
            {
                [Blouse] = Inputs(partialDeliveryPermitted: false),
                [Skirt] = Inputs(partialDeliveryPermitted: false),
                [Trousers] = Inputs(qcPassed: false, partialDeliveryPermitted: false),
            },
            OrdersTestData.Now);

        // The blouse names no dependency on the trousers at all; it reaches them through the skirt.
        var forBlouse = outcomes.Value.Single(outcome => outcome.GarmentJobId == Blouse);
        var block = forBlouse.Blocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.DependenciesMet);
        block.Reference.ShouldBe(order.FindJob(Trousers)!.JobNumber.Value);
    }

    /// <summary>
    /// Applying the verdicts one at a time would hand back the problem evaluating the set together solved: a
    /// refusal on the second garment would leave the first promoted, on the delivery queue, and bound to a
    /// garment that is not coming. Every verdict is checked before any is written.
    /// </summary>
    [Fact]
    public void ABoundSetIsPromotedTogetherOrNotAtAll()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var outcomes = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);

        // The second garment is withdrawn between the evaluation and the applying.
        OrdersTestData.CancelledJob(order, Skirt);

        var applied = order.ApplyReadyGate(
            outcomes.Value,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.InProduction);
        order.FindJob(Blouse)!.IsReadyForDelivery.ShouldBeFalse();
        order.FindJob(Blouse)!.ReadyStateBlocks.ShouldBeEmpty();
    }

    /// <summary>
    /// A set evaluated without the facts of the garment it was asked about has nothing to say about it, so it
    /// is refused rather than answered from the domain's own half of the predicates.
    /// </summary>
    [Fact]
    public void TheSetCannotBeEvaluatedWithoutTheNamedGarmentsOwnFacts()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);

        var outcomes = ReadyGate.EvaluateSet(
            order,
            Blouse,
            new Dictionary<Guid, ReadyGateInputs> { [Skirt] = Inputs() },
            OrdersTestData.Now);

        outcomes.IsFailure.ShouldBeTrue();
        outcomes.Error.Code.ShouldBe("orders.value-required");
    }

    /// <summary>
    /// The other half of INV-JOB-09. Binding means the set goes together, so a parcel with one garment still
    /// in the making promotes none of it: the finished garment blocks on
    /// <see cref="ReadyGatePredicate.DependenciesMet"/> naming the one that is not — section 9.1's fourth
    /// reason code and not a seventh — while the garment that is not done names the predicate it is actually
    /// failing, so the queue screen sends the Tailor Master to the work rather than to the waiting.
    /// </summary>
    [Fact]
    public void ABoundSetWithOneGarmentStillInTheMakingPromotesNeitherOfThem()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var outcomes = ReadyGate.EvaluateSet(
            order,
            Blouse,
            BoundFacts(skirt: Inputs(qcPassed: false, partialDeliveryPermitted: false)),
            OrdersTestData.Now);

        outcomes.Value.Count.ShouldBe(2);
        outcomes.Value.ShouldAllBe(outcome => !outcome.IsReady);

        var applied = order.ApplyReadyGate(
            outcomes.Value,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsSuccess.ShouldBeTrue();
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.InProduction);
        order.Jobs.ShouldAllBe(job => !job.IsReadyForDelivery);
        order.Status.ShouldBe(OrderStatus.InProduction);
        order.FindJob(Blouse)!.ReadyStateBlocks.ShouldHaveSingleItem().Reference
            .ShouldBe(order.FindJob(Skirt)!.JobNumber.Value);
        order.FindJob(Skirt)!.ReadyStateBlocks.ShouldHaveSingleItem().Predicate
            .ShouldBe(ReadyGatePredicate.QcPassed);
    }

    /// <summary>
    /// A cancelled garment is not part of what the order still owes, so it leaves the parcel rather than
    /// binding it: nobody is making it, and waiting for it would leave the finished garment unable to reach
    /// the customer at all. Facts gathered for it are ignored — the set answers for itself — so one verdict
    /// comes back and not two, and the garment that is being made goes to the customer on its own.
    /// </summary>
    [Fact]
    public void ACancelledGarmentLeavesTheParcelRatherThanHoldingItUp()
    {
        var order = BoundPair();
        OrdersTestData.CancelledJob(order, Skirt);
        OrdersTestData.InProduction(order, Blouse);

        var outcomes = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);

        var only = outcomes.Value.ShouldHaveSingleItem();
        only.GarmentJobId.ShouldBe(Blouse);
        only.IsReady.ShouldBeTrue();

        var applied = order.ApplyReadyGate(
            outcomes.Value,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsSuccess.ShouldBeTrue();
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Ready);
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.Cancelled);
        order.Status.ShouldBe(OrderStatus.Ready);
    }

    /// <summary>
    /// <strong>The parcel is never split on the applying side either.</strong> Judging the bound set
    /// together means the gate now hands out an independent verdict per member, so nothing but the order
    /// stops a caller taking one of them and leaving its partner where it stood — a garment promoted, on the
    /// delivery queue and dispatchable, bound to a garment still being made. Before the set evaluation that
    /// was unreachable only because the predicate deadlocked the parcel instead, which is not a guarantee.
    /// The verdict carries the rest of its parcel, and the order refuses to record half of one.
    /// </summary>
    [Fact]
    public void OneGarmentOfABoundParcelIsNotPromotedOnItsOwn()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var outcomes = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);
        outcomes.Value.ShouldAllBe(outcome => outcome.IsReady);

        var applied = order.ApplyReadyGate(
            Blouse,
            outcomes.Value.Single(outcome => outcome.GarmentJobId == Blouse),
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-parcel-split");
        applied.Error.Message.ShouldContain(order.FindJob(Skirt)!.JobNumber.Value);
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.InProduction);
        order.Jobs.ShouldAllBe(job => !job.IsReadyForDelivery);
        order.Status.ShouldBe(OrderStatus.InProduction);
    }

    /// <summary>
    /// <strong>The same rule said in the other direction is not the same rule at all.</strong> Closing a
    /// member's gate can never split a parcel: it takes a garment <em>off</em> the delivery queue, and half a
    /// parcel is a garment left standing <em>on</em> it. Refused symmetrically, the guard inverted — section
    /// 9.1 recomputes the gate on every QC event, and recomputing it for the garment that failed, which is
    /// the only garment a QC result is about, was refused because its partner stood at ready. The garment
    /// that failed QC therefore stayed at <c>ready</c>, materialised, and dispatchable: the guard written to
    /// stop half a parcel leaving was what kept a failed garment on the queue.
    /// </summary>
    [Fact]
    public void AQcFailureTakesTheGarmentThatFailedOffTheQueueThoughItsPartnerStandsReady()
    {
        var order = PromotedPair();
        var later = OrdersTestData.Now.AddHours(1);
        var failed = ReadyGate.Evaluate(
            order,
            Blouse,
            Inputs(qcPassed: false, partialDeliveryPermitted: false),
            later);

        var applied = order.ApplyReadyGate(
            Blouse,
            failed.Value,
            ReadyAggregation.EveryDeliverableJob,
            later);

        failed.Value.BoundWith.ShouldHaveSingleItem().ShouldBe(Skirt);
        applied.IsSuccess.ShouldBeTrue();
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.InProduction);
        order.FindJob(Blouse)!.IsReadyForDelivery.ShouldBeFalse();
        order.FindJob(Blouse)!.ReadyStateBlocks.Select(block => block.Predicate)
            .ShouldBe([ReadyGatePredicate.QcPassed]);
        order.Status.ShouldBe(OrderStatus.InProduction);
    }

    /// <summary>
    /// And what closing one member's gate deliberately does <em>not</em> do. Its partners stay at
    /// <c>ready</c> and on the delivery queue, exactly as they do when the member is held — whether the rest
    /// of a promoted parcel should come off with it is <strong>SQ-09</strong>, which is not settled and is
    /// not a guard's to settle. The promise is kept at the door instead, so the partner is on the queue and
    /// still cannot be handed over.
    /// </summary>
    [Fact]
    public void ClosingOneMembersGateLeavesItsPartnerOnTheQueueAndRefusesItAtTheDoor()
    {
        var order = PromotedPair();
        var later = OrdersTestData.Now.AddHours(1);
        var failed = ReadyGate.Evaluate(
            order,
            Blouse,
            Inputs(qcPassed: false, partialDeliveryPermitted: false),
            later);
        order.ApplyReadyGate(Blouse, failed.Value, ReadyAggregation.EveryDeliverableJob, later)
            .IsSuccess.ShouldBeTrue();

        var handedOver = order.ConfirmDelivery(
            Skirt,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            later,
            OrdersTestData.Actor);

        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.Ready);
        order.FindJob(Skirt)!.IsReadyForDelivery.ShouldBeTrue();
        handedOver.IsFailure.ShouldBeTrue();
        handedOver.Error.Code.ShouldBe("orders.delivery-would-split-parcel");
        handedOver.Error.Message.ShouldContain(order.FindJob(Blouse)!.JobNumber.Value);
        order.FindJob(Skirt)!.DeliveredAt.ShouldBeNull();
    }

    /// <summary>
    /// The two directions in one command, so the asymmetry cannot be read as a licence. A command that
    /// demotes one member of a promoted parcel and re-promotes the other is still refused: the promotion is
    /// the half that would leave a garment standing on the queue alone, and it is refused whichever order the
    /// two verdicts arrive in.
    /// </summary>
    [Fact]
    public void OneCommandStillCannotDemoteOneMemberOfAPromotedParcelAndPromoteTheOther()
    {
        var order = PromotedPair();
        var later = OrdersTestData.Now.AddHours(1);
        var everythingFine = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), later);
        var blouseFailedQc = ReadyGate.EvaluateSet(
            order,
            Blouse,
            BoundFacts(blouse: Inputs(qcPassed: false, partialDeliveryPermitted: false)),
            later);

        var applied = order.ApplyReadyGate(
            [
                blouseFailedQc.Value.Single(outcome => outcome.GarmentJobId == Blouse),
                everythingFine.Value.Single(outcome => outcome.GarmentJobId == Skirt),
            ],
            ReadyAggregation.EveryDeliverableJob,
            later);

        everythingFine.Value.Single(outcome => outcome.GarmentJobId == Skirt).IsReady.ShouldBeTrue();
        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-parcel-split");
        applied.Error.Message.ShouldContain(order.FindJob(Blouse)!.JobNumber.Value);
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.Ready);
        order.Status.ShouldBe(OrderStatus.Ready);
    }

    /// <summary>
    /// <strong>Being named in the command is not being answered for.</strong> The binding was held by asking
    /// that every partner the command did not name already stood where the verdict would put it — which reads
    /// membership of the command as agreement, and membership is only "this command says something about that
    /// garment", never "it says the same thing". One command could therefore carry a verdict of ready for one
    /// garment of a parcel and a verdict of blocked for its partner and record both: the split written in a
    /// single step, through the very overload the refusal recommends. What has to agree is where each member
    /// will stand once the command is written, whether a verdict the command carries decides that or the
    /// garment already does.
    /// </summary>
    [Fact]
    public void OneCommandCannotPromoteOneGarmentOfAParcelAndHoldItsPartnerBack()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var everythingFine = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);
        var skirtUndocumented = ReadyGate.EvaluateSet(
            order,
            Blouse,
            BoundFacts(skirt: Inputs(documentationComplete: false, partialDeliveryPermitted: false)),
            OrdersTestData.Now);

        var applied = order.ApplyReadyGate(
            [
                everythingFine.Value.Single(outcome => outcome.GarmentJobId == Blouse),
                skirtUndocumented.Value.Single(outcome => outcome.GarmentJobId == Skirt),
            ],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-parcel-split");
        applied.Error.Message.ShouldContain(order.FindJob(Skirt)!.JobNumber.Value);
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.InProduction);
        order.Jobs.ShouldAllBe(job => !job.IsReadyForDelivery);
        order.Jobs.ShouldAllBe(job => job.ReadyStateBlocks.Count == 0);
        order.Status.ShouldBe(OrderStatus.InProduction);
    }

    /// <summary>
    /// The same command with the two verdicts the other way round, and then the consequence the binding exists
    /// to prevent: a garment at the customer's door while the garment it was promised to travel with is still
    /// being made. Neither is promoted, so neither can be handed over.
    /// </summary>
    [Fact]
    public void TheGarmentOfAParcelThatPassedIsNotDispatchableWhileItsPartnerIsHeld()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var everythingFine = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);
        var blouseFailedQc = ReadyGate.EvaluateSet(
            order,
            Blouse,
            BoundFacts(blouse: Inputs(qcPassed: false, partialDeliveryPermitted: false)),
            OrdersTestData.Now);

        var applied = order.ApplyReadyGate(
            [
                blouseFailedQc.Value.Single(outcome => outcome.GarmentJobId == Blouse),
                everythingFine.Value.Single(outcome => outcome.GarmentJobId == Skirt),
            ],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        var handedOver = order.ConfirmDelivery(
            Skirt,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-parcel-split");
        handedOver.IsFailure.ShouldBeTrue();
        handedOver.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.InProduction);
        order.Jobs.ShouldAllBe(job => !job.IsReadyForDelivery);
    }

    /// <summary>
    /// A chain of three is one promise about three garments (<strong>SQ-07</strong>, interim), so two thirds of
    /// it cannot be promoted while the third is held — not even when all three are named in the one command.
    /// </summary>
    [Fact]
    public void AParcelOfThreeIsNotPromotedTwoThirdsOfTheWay()
    {
        var order = BoundChain();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        OrdersTestData.InProduction(order, Trousers);
        var everythingFine = ReadyGate.EvaluateSet(order, Blouse, ChainFacts(), OrdersTestData.Now);
        var trousersFailedQc = ReadyGate.EvaluateSet(
            order,
            Blouse,
            ChainFacts(trousers: Inputs(qcPassed: false, partialDeliveryPermitted: false)),
            OrdersTestData.Now);

        var applied = order.ApplyReadyGate(
            [
                everythingFine.Value.Single(outcome => outcome.GarmentJobId == Blouse),
                everythingFine.Value.Single(outcome => outcome.GarmentJobId == Skirt),
                trousersFailedQc.Value.Single(outcome => outcome.GarmentJobId == Trousers),
            ],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-parcel-split");
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.InProduction);
        order.Jobs.ShouldAllBe(job => !job.IsReadyForDelivery);
        order.Status.ShouldBe(OrderStatus.InProduction);
    }

    /// <summary>
    /// Whether partial delivery is permitted is the <em>branch's</em> answer (issue #48) and not a property of
    /// a garment, so one parcel is worked out under one of them. Answered two ways, the parcel came back bound
    /// at one end and loose at the other: the member marked partial carried no binding at all, so its verdict
    /// could then be recorded on its own while its partner stood in production. Neither answer is the domain's
    /// to pick, so the evaluation is refused rather than half honoured.
    /// </summary>
    [Fact]
    public void OneParcelIsWorkedOutUnderOneBranchDeliveryPolicy()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);

        var answeredTwoWays = ReadyGate.EvaluateSet(
            order,
            Blouse,
            BoundFacts(blouse: Inputs(partialDeliveryPermitted: true)),
            OrdersTestData.Now);

        answeredTwoWays.IsFailure.ShouldBeTrue();
        answeredTwoWays.Error.Code.ShouldBe("orders.dispatch-policy-not-shared");
        answeredTwoWays.Error.Target.ShouldBe("partialDeliveryPermitted");
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.InProduction);
        order.Jobs.ShouldAllBe(job => !job.IsReadyForDelivery);
    }

    /// <summary>
    /// <strong>And it is a property of one evaluation, not of the parcel.</strong> The gate holds no memory
    /// between calls and cannot compare one call's facts with another's, so a caller that answers the branch's
    /// policy one way in one evaluation and the other way in the next is not refused, and one garment of a
    /// parcel can be recorded on its own. What then stops it reaching the customer is the branch policy read
    /// again at the door — which is why section 9.1's waiver sentence is written about an evaluation and the
    /// refusal at the door is written about the scan.
    /// </summary>
    [Fact]
    public void TheOnePolicyRuleHoldsWithinOneEvaluationAndNotAcrossTwo()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);

        var waived = ReadyGate.Evaluate(
            order,
            Blouse,
            Inputs(partialDeliveryPermitted: true),
            OrdersTestData.Now);
        var bound = ReadyGate.Evaluate(
            order,
            Skirt,
            Inputs(partialDeliveryPermitted: false),
            OrdersTestData.Now);

        waived.IsSuccess.ShouldBeTrue();
        bound.IsSuccess.ShouldBeTrue();
        waived.Value.BoundWith.ShouldBeEmpty();
        bound.Value.BoundWith.ShouldHaveSingleItem().ShouldBe(Blouse);
        order.ApplyReadyGate(Blouse, waived.Value, ReadyAggregation.AnyDeliverableJob, OrdersTestData.Now)
            .IsSuccess.ShouldBeTrue();

        var atTheDoor = order.ConfirmDelivery(
            Blouse,
            ReadyAggregation.AnyDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Ready);
        atTheDoor.IsFailure.ShouldBeTrue();
        atTheDoor.Error.Code.ShouldBe("orders.delivery-would-split-parcel");
    }

    /// <summary>
    /// And the waiver itself is untouched: one policy, permitting partial delivery for the whole parcel, leaves
    /// every garment of it unbound — which is what issue #48 asks for.
    /// </summary>
    [Fact]
    public void OneBranchPolicyPermittingPartialDeliveryLeavesTheWholeParcelUnbound()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);

        var outcomes = ReadyGate.EvaluateSet(
            order,
            Blouse,
            BoundFacts(
                blouse: Inputs(partialDeliveryPermitted: true),
                skirt: Inputs(partialDeliveryPermitted: true)),
            OrdersTestData.Now);

        outcomes.IsSuccess.ShouldBeTrue();
        outcomes.Value.Count.ShouldBe(2);
        outcomes.Value.ShouldAllBe(outcome => outcome.BoundWith.Count == 0);
    }

    /// <summary>
    /// The binding asks that nothing is left behind, not that everything is rewritten: a partner already
    /// standing where the verdict would put it is already there, so a routine recomputation of one garment
    /// of a parcel that is wholly ready is recorded rather than refused.
    /// </summary>
    [Fact]
    public void APartnerAlreadyStandingWhereTheVerdictWouldPutItSatisfiesTheBinding()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var promoted = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);
        order.ApplyReadyGate(promoted.Value, ReadyAggregation.EveryDeliverableJob, OrdersTestData.Now)
            .IsSuccess.ShouldBeTrue();

        var later = OrdersTestData.Now.AddHours(1);
        var again = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), later);

        var applied = order.ApplyReadyGate(
            Blouse,
            again.Value.Single(outcome => outcome.GarmentJobId == Blouse),
            ReadyAggregation.EveryDeliverableJob,
            later);

        applied.IsSuccess.ShouldBeTrue();
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.Ready);
        order.FindJob(Blouse)!.ReadyStateComputedAt.ShouldBe(later);
        order.FindJob(Skirt)!.ReadyStateComputedAt.ShouldBe(OrdersTestData.Now);
    }

    /// <summary>
    /// A garment bound to nothing is the overwhelmingly common case, and the binding must not cost it
    /// anything: one verdict, one command, no set to gather.
    /// </summary>
    [Fact]
    public void AGarmentBoundToNothingIsStillRecordedOnItsOwn()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);

        var outcome = ReadyGate.Evaluate(
            order,
            Blouse,
            Inputs(partialDeliveryPermitted: false),
            OrdersTestData.Now);

        var applied = order.ApplyReadyGate(
            Blouse,
            outcome.Value,
            ReadyAggregation.AnyDeliverableJob,
            OrdersTestData.Now);

        outcome.Value.BoundWith.ShouldBeEmpty();
        applied.IsSuccess.ShouldBeTrue();
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Ready);
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    /// <summary>
    /// The waiver is a waiver of the whole promise, not only of the reason code: where the branch policy
    /// permits partial delivery the garments do not travel together, so one of them is recorded on its own
    /// (issue #48).
    /// </summary>
    [Fact]
    public void ABranchThatPermitsPartialDeliveryRecordsOneGarmentOfAParcelOnItsOwn()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);

        var outcome = ReadyGate.Evaluate(
            order,
            Blouse,
            Inputs(partialDeliveryPermitted: true),
            OrdersTestData.Now);

        var applied = order.ApplyReadyGate(
            Blouse,
            outcome.Value,
            ReadyAggregation.AnyDeliverableJob,
            OrdersTestData.Now);

        outcome.Value.BoundWith.ShouldBeEmpty();
        applied.IsSuccess.ShouldBeTrue();
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Ready);
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    /// <summary>
    /// <strong>The parcel is the same parcel whichever garment the recomputation starts from.</strong> The
    /// gate is recomputed on every custody event, and a garment handed over yesterday is still recomputed
    /// today — so a garment that has left must not become a member of a parcel simply because it is the one
    /// the caller named. It did: filtered out of its siblings' parcel but added to its own unconditionally, a
    /// delivered garment could raise <c>DependenciesMet</c> against the garments still in the shop, taking a
    /// finished garment off the delivery queue and the order's status backwards on the strength of one that
    /// had already gone. Membership is symmetric now, and the seed obeys the rule it applies to everybody
    /// else (SQ-08, interim).
    /// </summary>
    [Fact]
    public void AParcelIsTheSameOneWhicheverGarmentTheRecomputationStartsFrom()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var promoted = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);
        order.ApplyReadyGate(promoted.Value, ReadyAggregation.EveryDeliverableJob, OrdersTestData.Now)
            .IsSuccess.ShouldBeTrue();
        order.ConfirmDelivery(
            Skirt,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var later = OrdersTestData.Now.AddHours(2);
        var facts = new Dictionary<Guid, ReadyGateInputs>
        {
            [Blouse] = Inputs(partialDeliveryPermitted: false),
            [Skirt] = Inputs(
                partialDeliveryPermitted: false,
                custodyGateEnabled: true,
                custody: CustodyReconciliation.Unknown),
        };

        var seededAtTheGarmentThatHasGone = ReadyGate.EvaluateSet(order, Skirt, facts, later);

        // The garment that has left answers for itself and reaches nothing: no verdict about the blouse.
        seededAtTheGarmentThatHasGone.Value.ShouldHaveSingleItem().GarmentJobId.ShouldBe(Skirt);

        order.ApplyReadyGate(seededAtTheGarmentThatHasGone.Value, ReadyAggregation.EveryDeliverableJob, later)
            .IsSuccess.ShouldBeTrue();

        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Ready);
        order.FindJob(Blouse)!.IsReadyForDelivery.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Ready);

        // And the same facts read from the garment still in the shop say exactly the same thing.
        var seededAtTheGarmentStillHere = ReadyGate.EvaluateSet(order, Blouse, facts, later);

        seededAtTheGarmentStillHere.Value.ShouldHaveSingleItem().IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// The cancelled half of the same rule. As the seed of its own parcel a cancelled garment named the live
    /// garment as waiting for it, and the whole evaluation was then refused at the order — so a recomputation
    /// triggered on the cancelled garment could never promote its sibling. It reaches nothing now, and it holds
    /// no verdict of its own either: the applying side refuses one about a garment nobody is making, so the
    /// gate reaches none.
    /// </summary>
    [Fact]
    public void ACancelledGarmentHasNoParcelOfItsOwnToHoldItsLiveSiblingWith()
    {
        var order = BoundPair();
        OrdersTestData.CancelledJob(order, Skirt);
        OrdersTestData.InProduction(order, Blouse);

        var seededAtTheCancelledGarment = ReadyGate.EvaluateSet(order, Skirt, BoundFacts(), OrdersTestData.Now);

        seededAtTheCancelledGarment.IsSuccess.ShouldBeTrue();
        seededAtTheCancelledGarment.Value.ShouldBeEmpty();

        // The live garment is judged the same way from either end, and reaches the customer.
        var seededAtTheLiveGarment = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);

        seededAtTheLiveGarment.Value.ShouldHaveSingleItem().IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// <strong>A garment nobody is making holds no verdict, so the gate reaches none about it.</strong> The
    /// applying side refuses one outright, and the gate is recomputed on every workflow, QC, hold, dependency
    /// and custody event — so a gate that answered for a cancelled garment handed every one of those
    /// recomputations a verdict that could only be refused, and an evaluate-then-apply loop failed on every
    /// cancelled garment it was triggered for. Both sides say the same thing now: nothing is worked out about
    /// it, and nothing is applied to it.
    /// </summary>
    [Fact]
    public void AGarmentNobodyIsMakingIsGivenNoVerdictToApply()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.CancelledJob(order, Skirt);

        var outcomes = ReadyGate.EvaluateSet(order, Skirt, BoundFacts(), OrdersTestData.Now);

        outcomes.IsSuccess.ShouldBeTrue();
        outcomes.Value.ShouldBeEmpty();

        var applied = order.ApplyReadyGate(
            outcomes.Value,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsSuccess.ShouldBeTrue();
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.Cancelled);
        order.FindJob(Skirt)!.IsReadyForDelivery.ShouldBeFalse();
        order.FindJob(Skirt)!.ReadyStateBlocks.ShouldBeEmpty();
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    /// <summary>
    /// The one-garment overload answers for exactly one garment and has no way to return no verdict, so it says
    /// why there is none — in the words the applying side would have used — rather than handing back one
    /// nothing could ever accept.
    /// </summary>
    [Fact]
    public void TheOneGarmentOverloadSaysWhyAGarmentNobodyIsMakingHasNoVerdict()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.CancelledJob(order, Blouse);

        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(), OrdersTestData.Now);

        outcome.IsFailure.ShouldBeTrue();
        outcome.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.Single().IsReadyForDelivery.ShouldBeFalse();
    }

    /* The binding at the door ------------------------------------------------------------------- */

    /// <summary>
    /// <strong>INV-JOB-09 binds a parcel at the ready gate <em>and in the delivery queue</em>, and only the
    /// first half was built.</strong> A hold closes the ready state of the garment it is taken on and of no
    /// other, so a parcel the gate promoted together came apart the moment one member was held: its partner
    /// stood at ready, on the queue, with nothing left to refuse the handover — a garment at the customer's
    /// door while the garment it was promised to travel with is still being made. The gate cannot prevent it,
    /// because the split happens after the gate has spoken, so the promise is kept again at the door.
    /// </summary>
    [Fact]
    public void AGarmentIsNotHandedOverWhileTheGarmentItTravelsWithIsHeld()
    {
        var order = PromotedPair();
        Hold(order, Skirt).IsSuccess.ShouldBeTrue();

        var handedOver = order.ConfirmDelivery(
            Blouse,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        handedOver.IsFailure.ShouldBeTrue();
        handedOver.Error.Code.ShouldBe("orders.delivery-would-split-parcel");
        handedOver.Error.Message.ShouldContain(order.FindJob(Skirt)!.JobNumber.Value);
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Ready);
        order.FindJob(Blouse)!.DeliveredAt.ShouldBeNull();
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.OnHold);
    }

    /// <summary>
    /// And one step on. Resuming returns the garment to production rather than to ready — section 3.2 draws
    /// only that edge, and the gate rather than the command decides whether it is ready again — so the parcel
    /// is still apart and the handover is still refused.
    /// </summary>
    [Fact]
    public void AGarmentIsNotHandedOverWhileTheGarmentItTravelsWithIsBackInProduction()
    {
        var order = PromotedPair();
        Hold(order, Skirt).IsSuccess.ShouldBeTrue();
        order.Resume(
            Skirt,
            "The lining arrived.",
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var handedOver = order.ConfirmDelivery(
            Blouse,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        handedOver.IsFailure.ShouldBeTrue();
        handedOver.Error.Code.ShouldBe("orders.delivery-would-split-parcel");
        handedOver.Error.Message.ShouldContain(order.FindJob(Skirt)!.JobNumber.Value);
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.InProduction);
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Ready);
    }

    /// <summary>
    /// A chain of three is one promise about three garments (<strong>SQ-07</strong>, interim) at the door as
    /// well as at the gate: the garment that shares no row with the held one is still refused, because the
    /// parcel is the closure of the relation rather than the row.
    /// </summary>
    [Fact]
    public void TheFarEndOfAChainIsNotHandedOverWhileTheNearEndIsHeld()
    {
        var order = BoundChain();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        OrdersTestData.InProduction(order, Trousers);
        var promoted = ReadyGate.EvaluateSet(order, Blouse, ChainFacts(), OrdersTestData.Now);
        order.ApplyReadyGate(promoted.Value, ReadyAggregation.EveryDeliverableJob, OrdersTestData.Now)
            .IsSuccess.ShouldBeTrue();
        Hold(order, Skirt).IsSuccess.ShouldBeTrue();

        var handedOver = order.ConfirmDelivery(
            Trousers,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        handedOver.IsFailure.ShouldBeTrue();
        handedOver.Error.Code.ShouldBe("orders.delivery-would-split-parcel");
        handedOver.Error.Message.ShouldContain(order.FindJob(Skirt)!.JobNumber.Value);
        order.FindJob(Trousers)!.Status.ShouldBe(GarmentJobStatus.Ready);
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Ready);
    }

    /// <summary>
    /// The waiver is what the waiver is for. Where the branch policy permits partial delivery the garments
    /// were never bound at all — the gate waives <c>DependenciesMet</c> and records no parcel on the verdict
    /// (issue #48) — so the door waives it too, or the promise would be waived at one end and enforced at the
    /// other.
    /// </summary>
    [Fact]
    public void ABranchThatPermitsPartialDeliveryHandsOverAGarmentWhoseParcelIsNotReady()
    {
        var order = PromotedPair();
        Hold(order, Skirt).IsSuccess.ShouldBeTrue();

        var handedOver = order.ConfirmDelivery(
            Blouse,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: true,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        handedOver.IsSuccess.ShouldBeTrue();
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Delivered);
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.OnHold);
    }

    /// <summary>
    /// <c>AnyDeliverableJob</c> is not that waiver. It loosens which garments the <em>order's status</em> waits
    /// for (SQ-02) and says nothing about what one customer was promised together: a branch that hands garments
    /// over as they finish has not thereby waived a <c>deliver_together</c> row somebody wrote on this order.
    /// </summary>
    [Fact]
    public void ABranchThatReleasesEachGarmentAsItFinishesStillKeepsAParcelTogether()
    {
        var order = PromotedPair();
        Hold(order, Skirt).IsSuccess.ShouldBeTrue();

        var handedOver = order.ConfirmDelivery(
            Blouse,
            ReadyAggregation.AnyDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        handedOver.IsFailure.ShouldBeTrue();
        handedOver.Error.Code.ShouldBe("orders.delivery-would-split-parcel");
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Ready);
    }

    /// <summary>
    /// The parcel goes to the customer one garment at a time — Custody authorises one chain of custody — so
    /// what is asked at the door is that every live partner is itself ready, not that they leave in one
    /// command. The first garment leaves the parcel as it goes (<strong>SQ-08</strong>, interim), which is what
    /// lets the second follow it.
    /// </summary>
    [Fact]
    public void AParcelThatIsWhollyReadyIsHandedOverOneGarmentAfterTheOther()
    {
        var order = PromotedPair();

        var first = order.ConfirmDelivery(
            Blouse,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        var second = order.ConfirmDelivery(
            Skirt,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.Delivered);
        order.Status.ShouldBe(OrderStatus.Delivered);
    }

    /// <summary>
    /// A garment nobody is making has left the parcel and is not waited for (<strong>SQ-08</strong>, interim).
    /// Cancelling one garment of a parcel therefore releases the rest — which is the consequence SQ-08 asks the
    /// business owner to confirm, and it is the same reading at the door as at the gate.
    /// </summary>
    [Fact]
    public void ACancelledGarmentDoesNotKeepItsPartnerFromTheCustomer()
    {
        var order = PromotedPair();
        OrdersTestData.CancelledJob(order, Skirt);

        var handedOver = order.ConfirmDelivery(
            Blouse,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        handedOver.IsSuccess.ShouldBeTrue();
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.Delivered);
    }

    /// <summary>
    /// <strong>SQ-08 cuts both ways, and the second way is a handover.</strong> A garment already handed over
    /// is no longer a member of the parcel and no longer a step between two other members, exactly as a
    /// cancelled one is not — so handing the middle garment of a chain over releases the two ends from each
    /// other, and one of them goes to the customer while the other is still being made. That is the second
    /// half of the consequence SQ-08 asks the business owner to confirm, and
    /// <see cref="TheFarEndOfAChainIsNotHandedOverWhileTheNearEndIsHeld"/> is the same chain with the middle
    /// garment still in the shop.
    /// </summary>
    [Fact]
    public void HandingTheMiddleOfAChainOverReleasesTheEndsFromEachOther()
    {
        var order = BoundChain();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        OrdersTestData.InProduction(order, Trousers);
        var promoted = ReadyGate.EvaluateSet(order, Blouse, ChainFacts(), OrdersTestData.Now);
        order.ApplyReadyGate(promoted.Value, ReadyAggregation.EveryDeliverableJob, OrdersTestData.Now)
            .IsSuccess.ShouldBeTrue();
        order.ConfirmDelivery(
            Skirt,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();
        Hold(order, Blouse).IsSuccess.ShouldBeTrue();

        var handedOver = order.ConfirmDelivery(
            Trousers,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        order.DeliverTogetherParcelOf(Trousers).ShouldBeEmpty();
        handedOver.IsSuccess.ShouldBeTrue();
        order.FindJob(Trousers)!.Status.ShouldBe(GarmentJobStatus.Delivered);
        order.FindJob(Blouse)!.Status.ShouldBe(GarmentJobStatus.OnHold);
    }

    /// <summary>
    /// The two refusals come out in the right order: a garment that is itself on hold is refused because it is
    /// on hold, not because of the company it keeps. Naming a partner while the garment in the delivery staff's
    /// hands is the one that cannot go would send them to the wrong garment.
    /// </summary>
    [Fact]
    public void AGarmentThatIsItselfNotReadyIsRefusedForThatReasonAndNotForItsParcel()
    {
        var order = PromotedPair();
        Hold(order, Blouse).IsSuccess.ShouldBeTrue();
        Hold(order, Skirt).IsSuccess.ShouldBeTrue();

        var handedOver = order.ConfirmDelivery(
            Blouse,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        handedOver.IsFailure.ShouldBeTrue();
        handedOver.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
    }

    /// <summary>
    /// <strong>A verdict's parcel cannot be emptied from outside the gate.</strong> The list
    /// <c>Order.ApplyReadyGate</c> reads to refuse half a parcel was the very list the gate built, published
    /// through an <c>IReadOnlyList</c> that a cast undoes — so one <c>Clear()</c> disarmed the guard without
    /// fabricating anything, and nothing has to be fabricated for that to matter: a verdict carrying an empty
    /// parcel is one the order will record on its own.
    /// </summary>
    [Fact]
    public void TheParcelOnAVerdictCannotBeEmptiedByACast()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var outcomes = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);
        var blouse = outcomes.Value.Single(outcome => outcome.GarmentJobId == Blouse);

        blouse.BoundWith.ShouldNotBeEmpty();
        Should.Throw<NotSupportedException>(() => ((ICollection<Guid>)blouse.BoundWith).Clear());
        blouse.BoundWith.Count.ShouldBe(1);

        var applied = order.ApplyReadyGate(
            Blouse,
            blouse,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-parcel-split");
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.InProduction);
    }

    /// <summary>
    /// <strong>And neither can the order's own job list.</strong> It is not only what a caller reads:
    /// <c>Order.DeliverTogetherSiblingsOf</c> and <c>Order.DeliverTogetherParcelOf</c> walk it, and they are
    /// the single definition of "the parcel" that both the gate's refusal and the door's refusal are enforced
    /// against. One <c>Remove</c> through the <c>IReadOnlyCollection</c> would take a garment out of its
    /// parcel without cancelling it and without delivering it — nothing fabricated, both guards left with
    /// nothing to refuse.
    /// </summary>
    [Fact]
    public void TheOrdersJobListCannotBeWrittenToByACast()
    {
        var order = PromotedPair();

        Should.Throw<NotSupportedException>(() => ((ICollection<GarmentJob>)order.Jobs).Clear());
        Should.Throw<NotSupportedException>(
            () => ((ICollection<GarmentJob>)order.Jobs).Remove(order.FindJob(Skirt)!));
        order.Jobs.Count.ShouldBe(2);
        order.DeliverTogetherParcelOf(Blouse).ShouldHaveSingleItem().Id.ShouldBe(Skirt);
    }

    /// <summary>
    /// The reasons on a verdict are published on the same terms as its parcel. They cost less, because
    /// <see cref="ReadyGateOutcome.IsReady"/> is read from the count at construction and does not change when
    /// the list behind it does, so an emptied block list loses the reasons a queue screen shows rather than a
    /// guard — but a verdict whose two lists are published one way and held another is one a reader has to
    /// check twice.
    /// </summary>
    [Fact]
    public void TheReasonsOnAVerdictCannotBeClearedByACast()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);

        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(qcPassed: false), OrdersTestData.Now);

        outcome.Value.Blocks.ShouldNotBeEmpty();
        Should.Throw<NotSupportedException>(() => ((ICollection<ReadyGateBlock>)outcome.Value.Blocks).Clear());
        outcome.Value.Blocks.ShouldHaveSingleItem().Predicate.ShouldBe(ReadyGatePredicate.QcPassed);
        outcome.Value.IsReady.ShouldBeFalse();
    }

    /* Applying a verdict ------------------------------------------------------------------------ */

    /// <summary>
    /// The facts reach the gate from four other modules and two evaluations can finish out of order. A verdict
    /// older than the one standing on the row is refused, because applying it would promote the garment back on
    /// facts a newer evaluation has already contradicted — and the materialised ready state, not the status, is what
    /// the dispatch attempt reads (section 4.1). Ready state must never outlive the facts it was computed from.
    /// </summary>
    [Fact]
    public void AnOlderVerdictNeverOverwritesANewerOne()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        var passed = ReadyGate.Evaluate(order, Blouse, Inputs(), OrdersTestData.Now).Value;
        var failed = ReadyGate.Evaluate(
            order,
            Blouse,
            Inputs(qcPassed: false),
            OrdersTestData.Now.AddMinutes(5)).Value;
        Apply(order, failed, OrdersTestData.Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();

        var applied = Apply(order, passed, OrdersTestData.Now.AddMinutes(6));

        var job = order.Jobs.Single();
        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-outcome-stale");
        job.Status.ShouldBe(GarmentJobStatus.InProduction);
        job.IsReadyForDelivery.ShouldBeFalse();
        job.ReadyStateBlocks.Select(block => block.Predicate).ShouldBe([ReadyGatePredicate.QcPassed]);
        job.ReadyStateComputedAt.ShouldBe(OrdersTestData.Now.AddMinutes(5));
    }

    /// <summary>
    /// The same rule read the other way round: an older failing verdict arriving after a newer passing one does
    /// not take a genuinely ready garment off the queue either. Both directions are the one defect.
    /// </summary>
    [Fact]
    public void AnOlderFailingVerdictNeverClearsAValidReadyState()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        var failed = ReadyGate.Evaluate(order, Blouse, Inputs(qcPassed: false), OrdersTestData.Now).Value;
        var passed = ReadyGate.Evaluate(order, Blouse, Inputs(), OrdersTestData.Now.AddMinutes(5)).Value;
        Apply(order, passed, OrdersTestData.Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();

        var applied = Apply(order, failed, OrdersTestData.Now.AddMinutes(6));

        var job = order.Jobs.Single();
        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-outcome-stale");
        job.Status.ShouldBe(GarmentJobStatus.Ready);
        job.IsReadyForDelivery.ShouldBeTrue();
        job.ReadyStateBlocks.ShouldBeEmpty();
        job.ReadyStateComputedAt.ShouldBe(OrdersTestData.Now.AddMinutes(5));
    }

    /// <summary>
    /// The guard is about the order the verdicts were reached in and not about refusing a second one: a
    /// verdict newer than the standing one is exactly what the gate is recomputed for, so it is applied. A
    /// guard that refused it would have frozen every garment on the first result the gate ever reached, which
    /// is the same defect read the other way — ready state that outlives the facts it was computed from.
    /// </summary>
    [Fact]
    public void ANewerVerdictAppliedAfterAnOlderOneIsRecorded()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        var earlier = ReadyGate.Evaluate(order, Blouse, Inputs(workflowComplete: false), OrdersTestData.Now);
        var later = ReadyGate.Evaluate(order, Blouse, Inputs(), OrdersTestData.Now.AddMinutes(5));
        Apply(order, earlier.Value, OrdersTestData.Now).IsSuccess.ShouldBeTrue();

        var applied = Apply(order, later.Value, OrdersTestData.Now.AddMinutes(5));

        var job = order.Jobs.Single();
        applied.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.Ready);
        job.IsReadyForDelivery.ShouldBeTrue();
        job.ReadyStateBlocks.ShouldBeEmpty();
        job.ReadyStateComputedAt.ShouldBe(OrdersTestData.Now.AddMinutes(5));
    }

    /// <summary>
    /// Equal instants are not older. <c>docs/prd/state-transitions.md</c> section 3.2 makes the gate
    /// recomputation the <em>output</em> of a hold, and the two share one clock reading inside the
    /// transaction — so a guard written as "not newer" would refuse the very recomputation the transition
    /// asks for and leave the hold's own block standing as the whole reason list.
    /// </summary>
    [Fact]
    public void AVerdictReachedAtTheInstantTheStandingOneWasIsNotStale()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        Hold(order, Blouse).IsSuccess.ShouldBeTrue();
        order.Jobs.Single().ReadyStateComputedAt.ShouldBe(OrdersTestData.Now);

        var applied = RunGate(order, Blouse, Inputs(workflowComplete: false), OrdersTestData.Now);

        var job = order.Jobs.Single();
        applied.IsSuccess.ShouldBeTrue();
        job.IsReadyForDelivery.ShouldBeFalse();
        job.ReadyStateComputedAt.ShouldBe(OrdersTestData.Now);
        job.ReadyStateBlocks.Select(block => block.Predicate)
            .ShouldBe([ReadyGatePredicate.WorkflowComplete, ReadyGatePredicate.NoOpenHold]);
    }

    /// <summary>
    /// INV-JOB-07: a verdict is about exactly one garment, and applying one garment's verdict to another
    /// would make the single-writer guarantee meaningless.
    /// </summary>
    [Fact]
    public void AVerdictReachedForOneGarmentCannotBeAppliedToAnother()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(), OrdersTestData.Now);

        var applied = order.ApplyReadyGate(
            Skirt,
            outcome.Value,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.ready-gate-outcome-for-another-job");
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.InProduction);
        order.FindJob(Skirt)!.IsReadyForDelivery.ShouldBeFalse();
    }

    /// <summary>
    /// Section 7: a garment that has physically left is never pulled back by a recomputation. Succeeding
    /// without a change is what stops the routine recomputation — the gate runs on every custody event —
    /// from rewriting the ready state a garment was dispatched under.
    /// </summary>
    [Fact]
    public void ARecomputationLeavesADeliveredGarmentExactlyAsItStands()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.Delivered(order, Blouse);
        var computedAt = order.Jobs.Single().ReadyStateComputedAt;

        var applied = RunGate(
            order,
            Blouse,
            OrdersTestData.GateInputs(workflowComplete: false),
            OrdersTestData.Now.AddDays(9));

        var job = order.Jobs.Single();
        applied.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.Delivered);
        job.IsReadyForDelivery.ShouldBeTrue();
        job.ReadyStateBlocks.ShouldBeEmpty();
        job.ReadyStateComputedAt.ShouldBe(computedAt);
    }

    /// <summary>
    /// A verdict about a garment nobody is making says nothing at all, so it is refused rather than
    /// recorded.
    /// </summary>
    [Fact]
    public void ACancelledGarmentRefusesAVerdictOutright()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        var outcome = ReadyGate.Evaluate(order, Blouse, Inputs(), OrdersTestData.Now);
        OrdersTestData.CancelledJob(order, Blouse);

        var applied = order.ApplyReadyGate(
            Blouse,
            outcome.Value,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.Single().IsReadyForDelivery.ShouldBeFalse();
    }

    /// <summary>
    /// The reasons are replaced wholesale rather than appended to, so a screen never shows a predicate
    /// that passed on the run it is displaying.
    /// </summary>
    [Fact]
    public void EachRunReplacesTheReasonsTheLastOneLeft()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);
        RunGate(order, Blouse, OrdersTestData.GateInputs(workflowComplete: false, qcPassed: false));
        order.Jobs.Single().ReadyStateBlocks.Count.ShouldBe(2);

        RunGate(order, Blouse, OrdersTestData.GateInputs(qcPassed: false));

        order.Jobs.Single().ReadyStateBlocks.Select(block => block.Predicate)
            .ShouldBe([ReadyGatePredicate.QcPassed]);
    }

    [Fact]
    public void TheGateCannotBeEvaluatedForAGarmentThatIsNotOnTheOrder()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var outcome = ReadyGate.Evaluate(
            order,
            OrdersTestData.Id("a-garment-of-another-order"),
            Inputs(),
            OrdersTestData.Now);

        outcome.IsFailure.ShouldBeTrue();
        outcome.Error.Code.ShouldBe("orders.garment-job-not-found");
    }

    /* Gate inputs ------------------------------------------------------------------------------- */

    [Fact]
    public void ABlankReferenceIsNoReferenceRatherThanAnEmptyOneOnTheScreen()
    {
        var inputs = ReadyGateInputs.Create(
            workflowComplete: false,
            incompletePhaseCode: "   ",
            qcPassed: true,
            reworkOpen: false,
            failedQcReference: null,
            documentationComplete: true,
            missingEvidenceReference: null,
            partialDeliveryPermitted: true,
            custodyGateEnabled: false,
            CustodyReconciliation.Reconciled,
            openCustodyCaseReference: null);

        inputs.IsSuccess.ShouldBeTrue();
        inputs.Value.IncompletePhaseCode.ShouldBeNull();
    }

    /// <summary>
    /// The three values exist so that "no answer" is a third answer rather than a fold into one of the other
    /// two, and a fourth from a cast would undo that: it is neither reconciled, nor not reconciled, nor the
    /// honest "we could not establish it" the gate fails closed on.
    /// </summary>
    [Fact]
    public void ACustodyAnswerThatIsNoneOfTheThreeIsRefused()
    {
        var inputs = ReadyGateInputs.Create(
            workflowComplete: true,
            incompletePhaseCode: null,
            qcPassed: true,
            reworkOpen: false,
            failedQcReference: null,
            documentationComplete: true,
            missingEvidenceReference: null,
            partialDeliveryPermitted: true,
            custodyGateEnabled: true,
            (CustodyReconciliation)99,
            openCustodyCaseReference: null);

        inputs.IsFailure.ShouldBeTrue();
        inputs.Error.Code.ShouldBe("orders.value-not-understood");
        inputs.Error.Target.ShouldBe("custody");
    }

    [Fact]
    public void AReferenceLongerThanTheColumnHoldsIsRefusedRatherThanTruncated()
    {
        var inputs = ReadyGateInputs.Create(
            workflowComplete: true,
            incompletePhaseCode: null,
            qcPassed: false,
            reworkOpen: false,
            failedQcReference: new string('x', ReadyGateInputs.MaximumReferenceLength + 1),
            documentationComplete: true,
            missingEvidenceReference: null,
            partialDeliveryPermitted: true,
            custodyGateEnabled: false,
            CustodyReconciliation.Reconciled,
            openCustodyCaseReference: null);

        inputs.IsFailure.ShouldBeTrue();
        inputs.Error.Code.ShouldBe("orders.value-too-long");
    }

    /* Gate blocks ------------------------------------------------------------------------------- */

    /// <summary>
    /// A block is a persisted row and a line on a queue screen, and the member name <em>is</em> the reason
    /// code section 9.1 publishes. A seventh reason code from a cast — which is what a deserialiser makes of
    /// a value it did not recognise — is one nobody can act on, so it is refused rather than stored.
    /// </summary>
    /// <summary>
    /// <strong>A block carries one reason code and one reference, never a list of either.</strong> Section
    /// 9.1's blocking-reason column is what this type publishes, so a column that promised "failed criteria
    /// and defect codes" promised a screen something the row cannot hold: whatever the caller gathered
    /// arrives as one bounded string, and it is null when nothing was gathered.
    /// </summary>
    [Fact]
    public void ABlockCarriesOneReasonCodeAndOneReference()
    {
        typeof(ReadyGateBlock)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ShouldBe(["Predicate", "Reference"], ignoreOrder: true);

        var blocks = Evaluate(Inputs(qcPassed: false, documentationComplete: false));

        blocks.Count.ShouldBe(2);
        blocks.Single(block => block.Predicate is ReadyGatePredicate.QcPassed).Reference
            .ShouldBe("QC-000114");
        blocks.Single(block => block.Predicate is ReadyGatePredicate.DocumentationComplete).Reference
            .ShouldBe("finishing-photograph");
    }

    [Fact]
    public void AReasonCodeThatIsNoneOfTheSixIsRefused()
    {
        var block = ReadyGateBlock.Create((ReadyGatePredicate)99, "finishing");

        block.IsFailure.ShouldBeTrue();
        block.Error.Code.ShouldBe("orders.value-not-understood");
        block.Error.Target.ShouldBe("predicate");
    }

    [Fact]
    public void ABlockReferenceLongerThanTheColumnHoldsIsRefusedRatherThanTruncated()
    {
        var block = ReadyGateBlock.Create(
            ReadyGatePredicate.QcPassed,
            new string('x', ReadyGateBlock.MaximumReferenceLength + 1));

        block.IsFailure.ShouldBeTrue();
        block.Error.Code.ShouldBe("orders.value-too-long");
    }

    [Fact]
    public void ABlankBlockReferenceIsNoReferenceRatherThanAnEmptyOneOnTheScreen()
    {
        var block = ReadyGateBlock.Create(ReadyGatePredicate.NoOpenHold, "   ");

        block.IsSuccess.ShouldBeTrue();
        block.Value.Predicate.ShouldBe(ReadyGatePredicate.NoOpenHold);
        block.Value.Reference.ShouldBeNull();
    }

    /// <summary>
    /// A missing argument is a defect in the caller and not a shop-floor situation, so it throws — but it
    /// names the argument the reader is looking at, which means the parameters are checked in the order they
    /// are declared rather than in the order this overload happens to touch them. Pinned on both gate overloads
    /// and on the command that applies what they return, because one overload delegating to another is exactly
    /// how the reported argument came to be the second one.
    /// </summary>
    [Fact]
    public void TheGateNamesTheFirstArgumentThatWasNotSupplied()
    {
        Should.Throw<ArgumentNullException>(
                () => ReadyGate.Evaluate(null!, Blouse, null!, OrdersTestData.Now))
            .ParamName.ShouldBe("order");

        Should.Throw<ArgumentNullException>(
                () => ReadyGate.Evaluate(OrdersTestData.ConfirmedOrder(), Blouse, null!, OrdersTestData.Now))
            .ParamName.ShouldBe("inputs");

        Should.Throw<ArgumentNullException>(
                () => ReadyGate.EvaluateSet(null!, Blouse, null!, OrdersTestData.Now))
            .ParamName.ShouldBe("order");

        Should.Throw<ArgumentNullException>(
                () => ReadyGate.EvaluateSet(OrdersTestData.ConfirmedOrder(), Blouse, null!, OrdersTestData.Now))
            .ParamName.ShouldBe("inputs");

        Should.Throw<ArgumentNullException>(
                () => OrdersTestData.ConfirmedOrder().ApplyReadyGate(
                    Blouse,
                    null!,
                    ReadyAggregation.EveryDeliverableJob,
                    OrdersTestData.Now))
            .ParamName.ShouldBe("outcome");

        Should.Throw<ArgumentNullException>(
                () => OrdersTestData.ConfirmedOrder().ApplyReadyGate(
                    (IReadOnlyCollection<ReadyGateOutcome>)null!,
                    ReadyAggregation.EveryDeliverableJob,
                    OrdersTestData.Now))
            .ParamName.ShouldBe("outcomes");
    }

    /* Helpers ----------------------------------------------------------------------------------- */

    /// <summary>
    /// Two garments of one order that go to the customer together, declared from the second on to the
    /// first so the symmetry of INV-JOB-09 is genuinely being read rather than the row's direction.
    /// </summary>
    private static Order BoundPair()
    {
        var number = OrdersTestData.Number();

        return OrdersTestData.ConfirmedOrderOf(
            [
                OrdersTestData.Garment(number, jobIndex: 1),
                OrdersTestData.Garment(
                    number,
                    jobIndex: 2,
                    dependencies:
                    [
                        new GarmentJobDependencySpecification(
                            OrdersTestData.Id("dependency-1"),
                            Blouse,
                            JobDependencyKind.DeliverTogether,
                            Reason: null),
                    ]),
            ],
            number);
    }

    /// <summary>
    /// Three garments of one order bound into one parcel by a chain rather than a star: the trousers go with
    /// the skirt, the skirt goes with the blouse, and nothing names the blouse and the trousers together.
    /// </summary>
    private static Order BoundChain()
    {
        var number = OrdersTestData.Number();

        return OrdersTestData.ConfirmedOrderOf(
            [
                OrdersTestData.Garment(number, jobIndex: 1),
                OrdersTestData.Garment(
                    number,
                    jobIndex: 2,
                    dependencies:
                    [
                        new GarmentJobDependencySpecification(
                            OrdersTestData.Id("dependency-1"),
                            Blouse,
                            JobDependencyKind.DeliverTogether,
                            Reason: null),
                    ]),
                OrdersTestData.Garment(
                    number,
                    jobIndex: 3,
                    dependencies:
                    [
                        new GarmentJobDependencySpecification(
                            OrdersTestData.Id("dependency-2"),
                            Skirt,
                            JobDependencyKind.DeliverTogether,
                            Reason: null),
                    ]),
            ],
            number);
    }

    /// <summary>
    /// A <see cref="BoundPair"/> taken all the way through the gate together, so both garments stand at ready
    /// and on the delivery queue — the state a hold then takes apart.
    /// </summary>
    /// <remarks>
    /// Promoted through <see cref="ReadyGate.EvaluateSet"/> under one branch policy that refuses partial
    /// delivery, because a pair promoted under the waiver was never bound and would prove nothing about the
    /// binding at the door.
    /// </remarks>
    private static Order PromotedPair()
    {
        var order = BoundPair();
        OrdersTestData.InProduction(order, Blouse);
        OrdersTestData.InProduction(order, Skirt);
        var promoted = ReadyGate.EvaluateSet(order, Blouse, BoundFacts(), OrdersTestData.Now);

        order.ApplyReadyGate(promoted.Value, ReadyAggregation.EveryDeliverableJob, OrdersTestData.Now)
            .IsSuccess.ShouldBeTrue();

        return order;
    }

    /// <summary>
    /// The facts for both garments of a <see cref="BoundPair"/>, with partial delivery refused so the binding
    /// is genuinely being read.
    /// </summary>
    private static Dictionary<Guid, ReadyGateInputs> BoundFacts(
        ReadyGateInputs? blouse = null,
        ReadyGateInputs? skirt = null)
        => new()
        {
            [Blouse] = blouse ?? Inputs(partialDeliveryPermitted: false),
            [Skirt] = skirt ?? Inputs(partialDeliveryPermitted: false),
        };

    /// <summary>
    /// The facts for all three garments of a <see cref="BoundChain"/>, under one branch policy that refuses
    /// partial delivery so the binding is genuinely being read.
    /// </summary>
    private static Dictionary<Guid, ReadyGateInputs> ChainFacts(
        ReadyGateInputs? blouse = null,
        ReadyGateInputs? skirt = null,
        ReadyGateInputs? trousers = null)
        => new()
        {
            [Blouse] = blouse ?? Inputs(partialDeliveryPermitted: false),
            [Skirt] = skirt ?? Inputs(partialDeliveryPermitted: false),
            [Trousers] = trousers ?? Inputs(partialDeliveryPermitted: false),
        };

    /// <summary>Gate facts carrying a reference for every predicate, so a block can be read back.</summary>
    private static ReadyGateInputs Inputs(
        bool workflowComplete = true,
        bool qcPassed = true,
        bool reworkOpen = false,
        bool documentationComplete = true,
        bool partialDeliveryPermitted = true,
        bool custodyGateEnabled = false,
        CustodyReconciliation custody = CustodyReconciliation.Reconciled)
        => ReadyGateInputs.Create(
            workflowComplete,
            "finishing",
            qcPassed,
            reworkOpen,
            "QC-000114",
            documentationComplete,
            "finishing-photograph",
            partialDeliveryPermitted,
            custodyGateEnabled,
            custody,
            "CASE-000021").Value;

    /// <summary>The blocks the gate reports for a single garment that is in production.</summary>
    private static IReadOnlyList<ReadyGateBlock> Evaluate(ReadyGateInputs inputs)
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, Blouse);

        return ReadyGate.Evaluate(order, Blouse, inputs, OrdersTestData.Now).Value.Blocks;
    }

    private static Result Apply(Order order, ReadyGateOutcome outcome, DateTimeOffset at)
        => order.ApplyReadyGate(
            outcome.GarmentJobId,
            outcome,
            ReadyAggregation.EveryDeliverableJob,
            at);

    private static Result Hold(Order order, Guid garmentJobId)
        => order.Hold(
            garmentJobId,
            "awaiting-material",
            "The lining has not arrived.",
            OrdersTestData.Approver,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

    private static Result RunGate(
        Order order,
        Guid garmentJobId,
        ReadyGateInputs? inputs = null,
        DateTimeOffset? now = null)
    {
        var at = now ?? OrdersTestData.Now;
        var outcome = ReadyGate.Evaluate(order, garmentJobId, inputs ?? OrdersTestData.GateInputs(), at);

        return outcome.IsFailure
            ? Result.Failure(outcome.Error)
            : order.ApplyReadyGate(garmentJobId, outcome.Value, ReadyAggregation.EveryDeliverableJob, at);
    }
}
