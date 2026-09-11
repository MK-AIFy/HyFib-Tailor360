using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The garment job lifecycle of <c>docs/prd/state-transitions.md</c> section 3.2: every legal move, and
/// every move that is refused from the state it was attempted in.
/// </summary>
/// <remarks>
/// <para>
/// Every transition is driven through <see cref="Order"/> because that is the only route there is: the
/// job's factory and all of its mutators are <see langword="internal" />, so an application handler
/// cannot move a garment and leave the order saying something its jobs no longer support (SQ-02).
/// <see cref="AGarmentJobExposesNoPublicMutator"/> is the test that fails the day one is made public.
/// </para>
/// <para>
/// The negative cases are the point. A lifecycle test that only walks the happy path proves that the
/// legal moves work and says nothing at all about the illegal ones — and it is the illegal ones that a
/// screen, a replayed request or an offline queue will eventually attempt.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class GarmentJobTests
{
    private static readonly Guid Shirt = OrdersTestData.GarmentId(1);

    /* Creation ---------------------------------------------------------------------------------- */

    [Fact]
    public void AConfirmedOrderCreatesItsGarmentAlreadyConfirmedAndAlreadyFrozen()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var job = order.Jobs.ShouldHaveSingleItem();
        job.Status.ShouldBe(GarmentJobStatus.Confirmed);
        job.ConfirmedAt.ShouldBe(OrdersTestData.Now);
        job.ConfirmedBy.ShouldBe(OrdersTestData.Actor);
        job.WorkflowDefinitionId.ShouldBe(OrdersTestData.WorkflowDefinition);
        job.Measurements.ShouldNotBeNull();
        job.Design.ShouldNotBeNull();
        job.Price.ShouldNotBeNull();
    }

    /// <summary>
    /// The workflow <em>version</em> is resolved at start of production and not at confirmation, because
    /// a garment confirmed today and started next week runs the version published when the work begins
    /// (INV-JOB-02).
    /// </summary>
    [Fact]
    public void AConfirmedGarmentHasNoPinnedWorkflowVersionYet()
    {
        var job = OrdersTestData.ConfirmedOrder().Jobs.Single();

        job.WorkflowVersionId.ShouldBeNull();
        job.ProductionStartedAt.ShouldBeNull();
        job.HasEnteredProduction.ShouldBeFalse();
        job.IsDeliverable.ShouldBeTrue();
    }

    /// <summary>
    /// The gate has never run, so the garment is not ready and carries no reasons — which is not the
    /// same thing as a gate that ran and found nothing wrong.
    /// </summary>
    [Fact]
    public void AConfirmedGarmentIsNotReadyAndCarriesNoGateReasons()
    {
        var job = OrdersTestData.ConfirmedOrder().Jobs.Single();

        job.IsReadyForDelivery.ShouldBeFalse();
        job.ReadyStateComputedAt.ShouldBeNull();
        job.ReadyStateBlocks.ShouldBeEmpty();
    }

    [Fact]
    public void GarmentsAreHeldInJobIndexOrderWhateverOrderTheyWereSentIn()
    {
        // The job card set and the workboard read the same way whichever order the intake screen sent
        // the garments in.
        var number = OrdersTestData.Number();

        var order = OrdersTestData.ConfirmedOrderOf(
            [
                OrdersTestData.Garment(number, jobIndex: 3),
                OrdersTestData.Garment(number, jobIndex: 1),
                OrdersTestData.Garment(number, jobIndex: 2),
            ],
            number);

        order.Jobs.Select(job => job.JobIndex).ShouldBe([1, 2, 3]);
    }

    /// <summary>
    /// INV-JOB-01. The snapshot is a copy, so a list the caller goes on to edit is not the list the
    /// garment was confirmed against.
    /// </summary>
    [Fact]
    public void TheReferenceImagesAreCopiedRatherThanShared()
    {
        var supplied = new List<Guid> { OrdersTestData.Id("front"), OrdersTestData.Id("back") };
        var order = OrdersTestData.ConfirmedOrderOf([GarmentWithImages(supplied)]);

        supplied.Add(OrdersTestData.Id("added-after-confirmation"));

        order.Jobs.Single().ReferenceMediaIds.Count.ShouldBe(2);
    }

    [Fact]
    public void ARepeatedOrBlankReferenceImageIsDroppedAndTheRestKeepTheirOrder()
    {
        // The order the images arrive in is the order the job card prints them in, and the tailor reads
        // the first as the main reference.
        var front = OrdersTestData.Id("front");
        var back = OrdersTestData.Id("back");

        var order = OrdersTestData.ConfirmedOrderOf(
            [GarmentWithImages([front, Guid.Empty, back, front])]);

        order.Jobs.Single().ReferenceMediaIds.ShouldBe([front, back]);
    }

    /// <summary>
    /// The row says what a workboard filters and reports group by; the design copy is what the job card
    /// renders from. A garment whose row says <c>blouse</c> while its frozen design copy says <c>shirt</c>
    /// is a disagreement INV-JOB-01 makes permanent — the snapshot is immutable after confirmation and
    /// republishing the catalogue never reaches it — so no downstream reader could ever resolve it.
    /// </summary>
    [Theory]
    [InlineData("categoryKey")]
    [InlineData("serviceTypeKey")]
    public void AGarmentWhoseRowAndDesignCopyDisagreeAboutWhatItIsIsRefused(string field)
    {
        var specification = GarmentJobSpecification.Create(
            Shirt,
            OrdersTestData.JobNumber(OrdersTestData.Number(), 1),
            1,
            field == "categoryKey" ? "shirt" : "blouse",
            field == "serviceTypeKey" ? "alteration" : "stitch-new",
            OrdersTestData.WorkflowDefinition,
            OrdersTestData.Measurements(),
            OrdersTestData.Design(),
            OrdersTestData.Price(),
            OrdersTestData.DueDate,
            referenceMediaIds: null,
            dependencies: null);

        specification.IsFailure.ShouldBeTrue();
        specification.Error.Code.ShouldBe("orders.design-snapshot-not-for-this-garment");
        specification.Error.Target.ShouldBe(field);
    }

    /* Start production -------------------------------------------------------------------------- */

    [Fact]
    public void StartingProductionPinsTheWorkflowVersionAndEntersProduction()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var started = StartProduction(order, Shirt);

        var job = order.Jobs.Single();
        started.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.InProduction);
        job.WorkflowVersionId.ShouldBe(OrdersTestData.WorkflowVersion);
        job.ProductionStartedAt.ShouldBe(OrdersTestData.Now);
        job.ProductionStartedBy.ShouldBe(OrdersTestData.Actor);
        job.HasEnteredProduction.ShouldBeTrue();
    }

    [Theory]
    [InlineData(GarmentJobStatus.InProduction)]
    [InlineData(GarmentJobStatus.OnHold)]
    [InlineData(GarmentJobStatus.Ready)]
    [InlineData(GarmentJobStatus.Delivered)]
    [InlineData(GarmentJobStatus.Cancelled)]
    public void ProductionCanOnlyBeStartedOnAConfirmedGarment(GarmentJobStatus status)
    {
        var order = InStatus(status);

        var started = StartProduction(order, Shirt);

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.Single().Status.ShouldBe(status);
    }

    /// <summary>
    /// INV-JOB-02: a garment half made under two process versions is one nobody can say was finished.
    /// </summary>
    [Fact]
    public void ThePinnedWorkflowVersionSurvivesASecondAttemptToStartProduction()
    {
        var order = InStatus(GarmentJobStatus.InProduction);

        var again = order.StartProduction(
            Shirt,
            OrdersTestData.Id("a-newer-workflow-version"),
            [],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now.AddHours(1),
            OrdersTestData.Actor);

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.Single().WorkflowVersionId.ShouldBe(OrdersTestData.WorkflowVersion);
    }

    [Fact]
    public void ProductionCannotStartWithoutAWorkflowVersionToPin()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var started = order.StartProduction(
            Shirt,
            Guid.Empty,
            [],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe("orders.value-required");
        order.Jobs.Single().Status.ShouldBe(GarmentJobStatus.Confirmed);
    }

    [Fact]
    public void AGarmentThatIsNotOnThisOrderCannotBeStarted()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var started = StartProduction(order, OrdersTestData.Id("a-garment-of-another-order"));

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe("orders.garment-job-not-found");
    }

    /* Hold -------------------------------------------------------------------------------------- */

    [Theory]
    [InlineData(GarmentJobStatus.InProduction)]
    [InlineData(GarmentJobStatus.Ready)]
    public void AGarmentIsHeldFromProductionOrFromReady(GarmentJobStatus status)
    {
        var order = InStatus(status);

        var held = Hold(order, Shirt);

        var job = order.Jobs.Single();
        held.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.OnHold);
        job.HoldReasonCode.ShouldBe("awaiting-material");
        job.HeldAt.ShouldBe(OrdersTestData.Now);
        job.HeldBy.ShouldBe(OrdersTestData.Actor);
    }

    /// <summary>
    /// Section 8 makes the reason mandatory so that it can be read back: the code is what a report groups
    /// by and the sentence is what the next person to open the garment reads. A mandatory value that was
    /// validated and then discarded asked the question for nothing.
    /// </summary>
    [Fact]
    public void AHoldKeepsTheSentenceAsWellAsTheCodeAndRecordsWhoApprovedIt()
    {
        var order = InStatus(GarmentJobStatus.InProduction);

        Hold(order, Shirt).IsSuccess.ShouldBeTrue();

        var job = order.Jobs.Single();
        job.HoldReasonCode.ShouldBe("awaiting-material");
        job.HoldReason.ShouldBe("The lining has not arrived.");
        job.HoldApprovedBy.ShouldBe(OrdersTestData.Approver);
    }

    [Theory]
    [InlineData(GarmentJobStatus.Confirmed)]
    [InlineData(GarmentJobStatus.OnHold)]
    [InlineData(GarmentJobStatus.Delivered)]
    [InlineData(GarmentJobStatus.Cancelled)]
    public void AGarmentCannotBeHeldFromAnyOtherState(GarmentJobStatus status)
    {
        var order = InStatus(status);

        var held = Hold(order, Shirt);

        held.IsFailure.ShouldBeTrue();
        held.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.Single().Status.ShouldBe(status);
    }

    /// <summary>
    /// Section 8 makes the reason mandatory, and the two failures are separate because they ask for
    /// different things: the code is what a report groups by, and the sentence is what the next person
    /// to open the garment reads. Neither substitutes for the other.
    /// </summary>
    [Theory]
    [InlineData(null, "The lining has not arrived.", "orders.reason-code-required")]
    [InlineData("   ", "The lining has not arrived.", "orders.reason-code-required")]
    [InlineData("awaiting-material", null, "orders.reason-required")]
    [InlineData("awaiting-material", "   ", "orders.reason-required")]
    public void AHoldIsRefusedWithoutBothAReasonCodeAndAReason(
        string? reasonCode,
        string? reason,
        string expected)
    {
        var order = InStatus(GarmentJobStatus.InProduction);

        var held = order.Hold(
            Shirt,
            reasonCode,
            reason,
            OrdersTestData.Approver,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        var job = order.Jobs.Single();
        held.IsFailure.ShouldBeTrue();
        held.Error.Code.ShouldBe(expected);
        job.Status.ShouldBe(GarmentJobStatus.InProduction);
        job.HoldReasonCode.ShouldBeNull();
    }

    [Fact]
    public void AReasonLongerThanTheColumnHoldsIsRefusedRatherThanTruncated()
    {
        var order = InStatus(GarmentJobStatus.InProduction);

        var held = order.Hold(
            Shirt,
            "awaiting-material",
            new string('x', GarmentJob.MaximumReasonLength + 1),
            OrdersTestData.Approver,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        held.IsFailure.ShouldBeTrue();
        held.Error.Code.ShouldBe("orders.value-too-long");
        order.Jobs.Single().Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    /// <summary>
    /// Section 3.2's Hold row lists "the ready gate closes" among the transition's <strong>outputs</strong>,
    /// so the hold closes the materialised state itself rather than leaving it to whenever the application
    /// next recomputes. Nothing in the domain can require that recomputation to happen in the same
    /// transaction, and <c>job_ready_state</c> — not the status — is what the delivery-team receive scan
    /// reads (section 4.1): a held garment advertising itself as ready with no reason shown is the drift
    /// CI-03 names. What is written is the gate's own <c>NoOpenHold</c> reason code, which is exactly what
    /// the recomputation then produces.
    /// </summary>
    [Fact]
    public void HoldingAReadyGarmentClosesItsReadyStateAtTheHoldAndTheGateAgrees()
    {
        var order = InStatus(GarmentJobStatus.Ready);
        order.Jobs.Single().IsReadyForDelivery.ShouldBeTrue();

        Hold(order, Shirt).IsSuccess.ShouldBeTrue();

        var held = order.Jobs.Single();
        held.Status.ShouldBe(GarmentJobStatus.OnHold);
        held.IsReadyForDelivery.ShouldBeFalse();
        var block = held.ReadyStateBlocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.NoOpenHold);
        block.Reference.ShouldBe("awaiting-material");

        RunGate(order, Shirt).IsSuccess.ShouldBeTrue();

        var recomputed = order.Jobs.Single();
        recomputed.IsReadyForDelivery.ShouldBeFalse();
        recomputed.ReadyStateBlocks.Select(reason => reason.Predicate)
            .ShouldContain(ReadyGatePredicate.NoOpenHold);
        recomputed.Status.ShouldBe(GarmentJobStatus.OnHold);
    }

    /// <summary>
    /// A garment nobody is making must never appear on the delivery queue either, and
    /// <c>ApplyReadyGate</c> refuses a cancelled garment outright — so a verdict left standing at the
    /// cancellation would be one nothing could ever close.
    /// </summary>
    [Fact]
    public void CancellingAReadyGarmentClosesItsReadyStateToo()
    {
        var order = InStatus(GarmentJobStatus.Ready);
        order.Jobs.Single().IsReadyForDelivery.ShouldBeTrue();

        Cancel(order, Shirt).IsSuccess.ShouldBeTrue();

        var job = order.Jobs.Single();
        job.Status.ShouldBe(GarmentJobStatus.Cancelled);
        job.IsReadyForDelivery.ShouldBeFalse();
        job.ReadyStateBlocks.ShouldBeEmpty();
    }

    /* Resume ------------------------------------------------------------------------------------ */

    [Fact]
    public void ResumingReturnsAHeldGarmentToProductionAndClearsTheHold()
    {
        var order = InStatus(GarmentJobStatus.OnHold);

        var resumed = Resume(order, Shirt);

        var job = order.Jobs.Single();
        resumed.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.InProduction);
        job.HoldReasonCode.ShouldBeNull();
        job.HoldReason.ShouldBeNull();
        job.HoldApprovedBy.ShouldBeNull();
        job.HeldAt.ShouldBeNull();
        job.HeldBy.ShouldBeNull();

        // The hold's block goes with the hold: a resumed garment that still named a lifted hold on the
        // queue screen would send somebody to resolve something that is already resolved. It is not ready
        // either — the gate, not this command, decides that (section 3.2).
        job.IsReadyForDelivery.ShouldBeFalse();
        job.ReadyStateBlocks.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 3.2 sends a resumed garment to in production whatever it was held from, because the gate
    /// and not this command decides whether the garment is ready again.
    /// </summary>
    [Fact]
    public void AGarmentHeldFromReadyResumesIntoProductionAndNotBackIntoReady()
    {
        var order = InStatus(GarmentJobStatus.Ready);
        Hold(order, Shirt);

        Resume(order, Shirt).IsSuccess.ShouldBeTrue();

        order.Jobs.Single().Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    [Theory]
    [InlineData(GarmentJobStatus.Confirmed)]
    [InlineData(GarmentJobStatus.InProduction)]
    [InlineData(GarmentJobStatus.Ready)]
    [InlineData(GarmentJobStatus.Delivered)]
    [InlineData(GarmentJobStatus.Cancelled)]
    public void OnlyAHeldGarmentCanBeResumed(GarmentJobStatus status)
    {
        var order = InStatus(status);

        var resumed = Resume(order, Shirt);

        resumed.IsFailure.ShouldBeTrue();
        resumed.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.Single().Status.ShouldBe(status);
    }

    [Fact]
    public void ResumingIsRefusedWithoutAReason()
    {
        var order = InStatus(GarmentJobStatus.OnHold);

        var resumed = order.Resume(
            Shirt,
            reason: "  ",
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        resumed.IsFailure.ShouldBeTrue();
        resumed.Error.Code.ShouldBe("orders.reason-required");
        order.Jobs.Single().Status.ShouldBe(GarmentJobStatus.OnHold);
    }

    /* Reschedule -------------------------------------------------------------------------------- */

    [Theory]
    [InlineData(GarmentJobStatus.InProduction)]
    [InlineData(GarmentJobStatus.OnHold)]
    public void ThePromisedDateMovesFromProductionOrFromAHoldAndTheStatusDoesNot(GarmentJobStatus status)
    {
        var order = InStatus(status);
        var moved = OrdersTestData.DueDate.AddDays(4);

        var rescheduled = order.Reschedule(
            Shirt,
            moved,
            "The customer asked for the following week.",
            OrdersTestData.Now.AddDays(1),
            OrdersTestData.Actor);

        var job = order.Jobs.Single();
        rescheduled.IsSuccess.ShouldBeTrue();
        job.DueDate.ShouldBe(moved);
        job.Status.ShouldBe(status);
    }

    /// <summary>
    /// Resuming never moves the promised date. A new date is a separate reschedule with its own reason
    /// and its own customer communication, so a date is never quietly moved behind the customer's back.
    /// </summary>
    [Fact]
    public void ResumingLeavesThePromisedDateExactlyWhereItWas()
    {
        var order = InStatus(GarmentJobStatus.OnHold);

        Resume(order, Shirt);

        order.Jobs.Single().DueDate.ShouldBe(OrdersTestData.DueDate);
    }

    [Theory]
    [InlineData(GarmentJobStatus.Confirmed)]
    [InlineData(GarmentJobStatus.Ready)]
    [InlineData(GarmentJobStatus.Delivered)]
    [InlineData(GarmentJobStatus.Cancelled)]
    public void AGarmentCannotBeRescheduledFromAnyOtherState(GarmentJobStatus status)
    {
        var order = InStatus(status);

        var rescheduled = order.Reschedule(
            Shirt,
            OrdersTestData.DueDate.AddDays(4),
            "The customer asked for the following week.",
            OrdersTestData.Now.AddDays(1),
            OrdersTestData.Actor);

        rescheduled.IsFailure.ShouldBeTrue();
        rescheduled.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.Single().DueDate.ShouldBe(OrdersTestData.DueDate);
    }

    [Fact]
    public void ReschedulingIsRefusedWithoutAReason()
    {
        var order = InStatus(GarmentJobStatus.InProduction);

        var rescheduled = order.Reschedule(
            Shirt,
            OrdersTestData.DueDate.AddDays(4),
            reason: null,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        rescheduled.IsFailure.ShouldBeTrue();
        rescheduled.Error.Code.ShouldBe("orders.reason-required");
        order.Jobs.Single().DueDate.ShouldBe(OrdersTestData.DueDate);
    }

    /* Delivery ---------------------------------------------------------------------------------- */

    [Fact]
    public void AReadyGarmentIsHandedOverAtTheDoor()
    {
        var order = InStatus(GarmentJobStatus.Ready);

        var delivered = order.ConfirmDelivery(
            Shirt,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now.AddDays(2),
            OrdersTestData.Actor);

        var job = order.Jobs.Single();
        delivered.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.Delivered);
        job.DeliveredAt.ShouldBe(OrdersTestData.Now.AddDays(2));
        job.DeliveredBy.ShouldBe(OrdersTestData.Actor);
        job.IsDeliverable.ShouldBeFalse();
    }

    [Theory]
    [InlineData(GarmentJobStatus.Confirmed)]
    [InlineData(GarmentJobStatus.InProduction)]
    [InlineData(GarmentJobStatus.OnHold)]
    [InlineData(GarmentJobStatus.Delivered)]
    [InlineData(GarmentJobStatus.Cancelled)]
    public void OnlyAReadyGarmentCanBeHandedOver(GarmentJobStatus status)
    {
        var order = InStatus(status);

        var delivered = order.ConfirmDelivery(
            Shirt,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now.AddDays(2),
            OrdersTestData.Actor);

        delivered.IsFailure.ShouldBeTrue();
        delivered.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.Single().Status.ShouldBe(status);
    }

    /// <summary>
    /// Section 7: a garment that has physically left cannot be pulled back. The handover is recorded
    /// once, and the second attempt is a refusal rather than a second handover with a second custodian
    /// written over the first.
    /// </summary>
    [Fact]
    public void ASecondHandoverOfTheSameGarmentIsRefusedAndTheFirstOneStands()
    {
        var order = InStatus(GarmentJobStatus.Delivered);
        var first = order.Jobs.Single().DeliveredAt;

        var again = order.ConfirmDelivery(
            Shirt,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now.AddDays(3),
            OrdersTestData.Id("another-delivery-person"));

        var job = order.Jobs.Single();
        again.IsFailure.ShouldBeTrue();
        job.DeliveredAt.ShouldBe(first);
        job.DeliveredBy.ShouldBe(OrdersTestData.Actor);
    }

    /* Cancellation ------------------------------------------------------------------------------ */

    [Theory]
    [InlineData(GarmentJobStatus.Confirmed)]
    [InlineData(GarmentJobStatus.InProduction)]
    [InlineData(GarmentJobStatus.OnHold)]
    [InlineData(GarmentJobStatus.Ready)]
    public void AGarmentIsCancelledFromEveryStateBeforeItHasLeft(GarmentJobStatus status)
    {
        var order = InStatus(status);

        var cancelled = Cancel(order, Shirt);

        var job = order.Jobs.Single();
        cancelled.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(GarmentJobStatus.Cancelled);
        job.CancelledAt.ShouldBe(OrdersTestData.Now);
        job.CancelledBy.ShouldBe(OrdersTestData.Actor);
        job.CancellationReasonCode.ShouldBe("customer-changed-mind");
        job.IsDeliverable.ShouldBeFalse();
    }

    [Theory]
    [InlineData(GarmentJobStatus.Delivered)]
    [InlineData(GarmentJobStatus.Cancelled)]
    public void AGarmentThatHasLeftOrIsAlreadyCancelledCannotBeCancelled(GarmentJobStatus status)
    {
        var order = InStatus(status);

        var cancelled = Cancel(order, Shirt);

        cancelled.IsFailure.ShouldBeTrue();
        cancelled.Error.Code.ShouldBe("orders.job-status-transition-not-allowed");
        order.Jobs.Single().Status.ShouldBe(status);
    }

    /// <summary>
    /// INV-ORD-06 and <c>docs/prd/exceptions.md</c> EX-08: cancellation is blocked, not forced. The
    /// compensating flows run first, and the refusal names the blocking state's code — which is
    /// operational configuration and never a value.
    /// </summary>
    [Fact]
    public void CancellingAGarmentIsBlockedWhileAProhibitedStateStands()
    {
        var order = InStatus(GarmentJobStatus.InProduction);

        var cancelled = order.CancelJob(
            Shirt,
            "customer-changed-mind",
            "The customer no longer wants this garment.",
            ["unreturned-customer-material"],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        cancelled.IsFailure.ShouldBeTrue();
        cancelled.Error.Code.ShouldBe("orders.job-cancellation-blocked");
        cancelled.Error.Message.ShouldContain("unreturned-customer-material");
        order.Jobs.Single().Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    /// <summary>
    /// A refusal that cannot name what is blocking leaves the person at the counter with nothing to act
    /// on, so a blank entry is a defect in the caller rather than a state of the garment.
    /// </summary>
    [Fact]
    public void ABlockingStateWithNoNameIsRefusedRatherThanReportedAsTheBlocker()
    {
        var order = InStatus(GarmentJobStatus.InProduction);

        var cancelled = order.CancelJob(
            Shirt,
            "customer-changed-mind",
            "The customer no longer wants this garment.",
            ["   "],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        cancelled.IsFailure.ShouldBeTrue();
        cancelled.Error.Code.ShouldBe("orders.value-required");
        order.Jobs.Single().Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    [Theory]
    [InlineData(null, "The customer no longer wants this garment.", "orders.reason-code-required")]
    [InlineData("customer-changed-mind", "", "orders.reason-required")]
    public void CancellingAGarmentIsRefusedWithoutBothAReasonCodeAndAReason(
        string? reasonCode,
        string? reason,
        string expected)
    {
        var order = InStatus(GarmentJobStatus.InProduction);

        var cancelled = order.CancelJob(
            Shirt,
            reasonCode,
            reason,
            [],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        cancelled.IsFailure.ShouldBeTrue();
        cancelled.Error.Code.ShouldBe(expected);
        order.Jobs.Single().Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    /// <summary>
    /// G-5: nothing is removed. An invoice line, a stock reservation and a custody chain all name the
    /// garment, and how long it waited is part of why it was cancelled — so the hold it was cancelled
    /// from is left standing rather than tidied away.
    /// </summary>
    [Fact]
    public void CancellingKeepsTheSnapshotsTheDependenciesAndTheHoldItWasCancelledFrom()
    {
        var number = OrdersTestData.Number();
        var trousers = OrdersTestData.GarmentId(2);
        var order = OrdersTestData.ConfirmedOrderOf(
            [
                OrdersTestData.Garment(number, jobIndex: 1),
                OrdersTestData.Garment(
                    number,
                    jobIndex: 2,
                    dependencies:
                    [
                        new GarmentJobDependencySpecification(
                            OrdersTestData.Id("dependency-1"),
                            Shirt,
                            JobDependencyKind.DeliverTogether,
                            "They go to the customer together."),
                    ]),
            ],
            number);

        StartProduction(order, trousers);
        Hold(order, trousers);
        Cancel(order, trousers).IsSuccess.ShouldBeTrue();

        var job = order.FindJob(trousers)!;
        job.Status.ShouldBe(GarmentJobStatus.Cancelled);
        job.Measurements.ShouldNotBeNull();
        job.Design.ShouldNotBeNull();
        job.Price.ShouldNotBeNull();
        job.Dependencies.ShouldHaveSingleItem();
        job.HoldReasonCode.ShouldBe("awaiting-material");
        job.HeldAt.ShouldBe(OrdersTestData.Now);
    }

    /* Revision ---------------------------------------------------------------------------------- */

    /// <summary>
    /// INV-ORD-05 read literally: a garment that has left confirmed — into production, on hold, ready,
    /// delivered or cancelled — closes the revision window for the whole order. "Every job still
    /// confirmed" is not "every job that is left", and the route afterwards is an alteration request.
    /// </summary>
    [Theory]
    [InlineData(GarmentJobStatus.InProduction)]
    [InlineData(GarmentJobStatus.OnHold)]
    [InlineData(GarmentJobStatus.Ready)]
    [InlineData(GarmentJobStatus.Delivered)]
    [InlineData(GarmentJobStatus.Cancelled)]
    public void ARevisionIsRefusedOnceAJobHasLeftConfirmed(GarmentJobStatus status)
    {
        var order = InStatus(status);
        var frozen = order.Jobs.Single().Design;

        var revised = Revise(order, Shirt);

        revised.IsFailure.ShouldBeTrue();
        revised.Error.Code.ShouldBe("orders.revision-refused-after-production");
        order.Jobs.Single().Design.ShouldBeSameAs(frozen);
        order.RevisionNumber.ShouldBe(1);
    }

    /// <summary>
    /// One garment in production closes the window for every garment on the order, including the ones
    /// nobody has touched.
    /// </summary>
    [Fact]
    public void OneGarmentInProductionClosesTheRevisionWindowForItsSiblingsToo()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        StartProduction(order, Shirt);

        var revised = Revise(order, OrdersTestData.GarmentId(2));

        revised.IsFailure.ShouldBeTrue();
        revised.Error.Code.ShouldBe("orders.revision-refused-after-production");
    }

    [Fact]
    public void AGarmentStillConfirmedIsReSnapshottedByARevision()
    {
        var order = OrdersTestData.ConfirmedOrder();
        var moved = OrdersTestData.DueDate.AddDays(3);

        var revised = order.Revise(
            OrdersTestData.Id("revision-2"),
            "The customer changed the neckline.",
            OrdersTestData.Price(2600m),
            moved,
            [
                GarmentJobRevision.Create(
                    Shirt,
                    OrdersTestData.Measurements(),
                    OrdersTestData.Design("square"),
                    OrdersTestData.Price(2600m),
                    moved).Value,
            ],
            supersededEstimateId: null,
            OrdersTestData.Now.AddHours(2),
            OrdersTestData.Actor);

        var job = order.Jobs.Single();
        revised.IsSuccess.ShouldBeTrue();
        job.Design.Selections.ShouldHaveSingleItem().OptionCode.ShouldBe("square");
        job.DueDate.ShouldBe(moved);
        job.Status.ShouldBe(GarmentJobStatus.Confirmed);
        order.RevisionNumber.ShouldBe(2);
    }

    /// <summary>
    /// A revision naming a garment that is not on the order leaves the order exactly as it was rather
    /// than half re-priced, because the whole set is resolved before anything is applied.
    /// </summary>
    [Fact]
    public void ARevisionNamingAnUnknownGarmentChangesNothingAtAll()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        var frozen = order.FindJob(Shirt)!.Design;

        var revised = order.Revise(
            OrdersTestData.Id("revision-2"),
            "The customer changed the neckline.",
            OrdersTestData.Price(2600m),
            OrdersTestData.DueDate,
            [
                GarmentJobRevision.Create(
                    Shirt,
                    OrdersTestData.Measurements(),
                    OrdersTestData.Design("square"),
                    OrdersTestData.Price(2600m),
                    OrdersTestData.DueDate).Value,
                GarmentJobRevision.Create(
                    OrdersTestData.Id("a-garment-of-another-order"),
                    OrdersTestData.Measurements(),
                    OrdersTestData.Design("square"),
                    OrdersTestData.Price(2600m),
                    OrdersTestData.DueDate).Value,
            ],
            supersededEstimateId: null,
            OrdersTestData.Now.AddHours(2),
            OrdersTestData.Actor);

        revised.IsFailure.ShouldBeTrue();
        revised.Error.Code.ShouldBe("orders.garment-job-not-found");
        order.FindJob(Shirt)!.Design.ShouldBeSameAs(frozen);
        order.RevisionNumber.ShouldBe(1);
    }

    /* Shape ------------------------------------------------------------------------------------- */

    /// <summary>
    /// The moment a mutator here becomes public, an application handler can move a garment and leave the
    /// order saying something its jobs no longer support — and the SQ-02 aggregation, which every
    /// command on <see cref="Order"/> performs before it returns, becomes skippable.
    /// </summary>
    [Fact]
    public void AGarmentJobExposesNoPublicMutator()
    {
        var methods = typeof(GarmentJob)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToList();

        methods.ShouldBe([nameof(GarmentJob.PrerequisiteJobIds)]);
    }

    [Fact]
    public void AGarmentJobCannotBeConstructedOutsideItsOwnOrder()
    {
        typeof(GarmentJob)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// <strong>SQ-01 has not settled what closes a delivered garment</strong>
    /// (<c>docs/prd/state-transitions.md</c> section 10, proposed and to be confirmed). Until it is
    /// decided, delivered is the last automatic state, so no command in this module writes
    /// <see cref="GarmentJobStatus.Closed"/> — and this is the test that fails the day one quietly
    /// starts to.
    /// </summary>
    [Fact]
    public void NothingClosesADeliveredGarmentWhileSq01IsOpen()
    {
        var order = InStatus(GarmentJobStatus.Delivered);

        RunGate(order, Shirt);
        order.RecomputeStatus(ReadyAggregation.EveryDeliverableJob, OrdersTestData.Now.AddMonths(6));

        order.Jobs.Single().Status.ShouldBe(GarmentJobStatus.Delivered);
        order.Status.ShouldNotBe(OrderStatus.Closed);
    }

    /* Helpers ----------------------------------------------------------------------------------- */

    /// <summary>
    /// A single-garment order whose job stands in the state named, reached the only way the lifecycle
    /// allows — which is why <see cref="GarmentJobStatus.Closed"/> cannot be asked for.
    /// </summary>
    private static Order InStatus(GarmentJobStatus status)
    {
        var order = OrdersTestData.ConfirmedOrder();

        switch (status)
        {
            case GarmentJobStatus.Confirmed:
                break;
            case GarmentJobStatus.InProduction:
                OrdersTestData.InProduction(order, Shirt);
                break;
            case GarmentJobStatus.OnHold:
                OrdersTestData.InProduction(order, Shirt);
                Hold(order, Shirt);
                break;
            case GarmentJobStatus.Ready:
                OrdersTestData.Ready(order, Shirt);
                break;
            case GarmentJobStatus.Delivered:
                OrdersTestData.Delivered(order, Shirt);
                break;
            case GarmentJobStatus.Cancelled:
                Cancel(order, Shirt);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(status),
                    status,
                    "No command in this module reaches that status.");
        }

        order.Jobs.Single().Status.ShouldBe(status);

        return order;
    }

    private static GarmentJobSpecification GarmentWithImages(IReadOnlyCollection<Guid> referenceMediaIds)
        => GarmentJobSpecification.Create(
            Shirt,
            OrdersTestData.JobNumber(OrdersTestData.Number(), 1),
            1,
            "blouse",
            "stitch-new",
            OrdersTestData.WorkflowDefinition,
            OrdersTestData.Measurements(),
            OrdersTestData.Design(),
            OrdersTestData.Price(),
            OrdersTestData.DueDate,
            referenceMediaIds,
            dependencies: null).Value;

    private static Result StartProduction(Order order, Guid garmentJobId)
        => order.StartProduction(
            garmentJobId,
            OrdersTestData.WorkflowVersion,
            [],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

    private static Result RunGate(Order order, Guid garmentJobId)
    {
        var outcome = ReadyGate.Evaluate(order, garmentJobId, OrdersTestData.GateInputs(), OrdersTestData.Now);

        return outcome.IsFailure
            ? Result.Failure(outcome.Error)
            : order.ApplyReadyGate(
                garmentJobId,
                outcome.Value,
                ReadyAggregation.EveryDeliverableJob,
                OrdersTestData.Now);
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

    private static Result Resume(Order order, Guid garmentJobId)
        => order.Resume(
            garmentJobId,
            "The lining arrived.",
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now.AddDays(1),
            OrdersTestData.Actor);

    private static Result Cancel(Order order, Guid garmentJobId)
        => order.CancelJob(
            garmentJobId,
            "customer-changed-mind",
            "The customer no longer wants this garment.",
            [],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

    private static Result Revise(Order order, Guid garmentJobId)
        => order.Revise(
            OrdersTestData.Id("revision-2"),
            "The customer changed the neckline.",
            OrdersTestData.Price(2600m),
            OrdersTestData.DueDate,
            [
                GarmentJobRevision.Create(
                    garmentJobId,
                    OrdersTestData.Measurements(),
                    OrdersTestData.Design("square"),
                    OrdersTestData.Price(2600m),
                    OrdersTestData.DueDate).Value,
            ],
            supersededEstimateId: null,
            OrdersTestData.Now.AddHours(2),
            OrdersTestData.Actor);
}
