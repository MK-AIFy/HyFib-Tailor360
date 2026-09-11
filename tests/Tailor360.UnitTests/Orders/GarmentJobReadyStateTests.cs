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

    /* Applying a verdict ------------------------------------------------------------------------ */

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
