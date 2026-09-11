using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The order's derived status.
/// </summary>
/// <remarks>
/// <para>
/// Everything asserted here is the <strong>interim position of SQ-02</strong>
/// (<c>docs/prd/state-transitions.md</c> section 10, proposed and to be confirmed): in production once a
/// garment has entered production, ready when the job set the branch dispatch policy requires is ready,
/// delivered when every non-cancelled garment is delivered. Which job set the policy requires is the half
/// SQ-02 leaves open, so it arrives as a <see cref="ReadyAggregation"/> and is never guessed here.
/// </para>
/// <para>
/// Two statuses are asserted to be unreachable rather than reached: <see cref="OrderStatus.Closed"/>
/// because <strong>SQ-01</strong> has not settled what closes a delivered order, and
/// <see cref="OrderStatus.Cancelled"/> because <strong>SQ-04</strong>'s interim position is that
/// cancelling every garment does not cancel the order.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OrderStatusAggregationTests
{
    private static readonly DateTimeOffset Later = OrdersTestData.Now.AddHours(3);

    /* Confirmed and in production ---------------------------------------------------------------- */

    [Fact]
    public void AnOrderWithEveryGarmentStillConfirmedIsConfirmed()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        order.RecomputeStatus(ReadyAggregation.EveryDeliverableJob, Later).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.ProductionStartedAt.ShouldBeNull();
    }

    [Fact]
    public void AnOrderIsInProductionOnceAnyGarmentHasEnteredProduction()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(1));

        order.Status.ShouldBe(OrderStatus.InProduction);
        order.ProductionStartedAt.ShouldBe(OrdersTestData.Now);
    }

    [Fact]
    public void TheMomentProductionStartedIsRecordedOnceAndNeverMoved()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(1));

        order.StartProduction(
            OrdersTestData.GarmentId(2),
            OrdersTestData.WorkflowVersion,
            Array.Empty<Guid>(),
            ReadyAggregation.EveryDeliverableJob,
            Later,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        order.ProductionStartedAt.ShouldBe(OrdersTestData.Now);
    }

    /* Ready, and who is allowed to write it ------------------------------------------------------ */

    [Fact]
    public void UnderWholeOrderDispatchOneOutstandingGarmentKeepsTheOrderOutOfReady()
    {
        // The conservative reading: the customer collects one parcel, so one outstanding garment keeps the
        // whole order out of the delivery queue.
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        OrdersTestData.Ready(order, OrdersTestData.GarmentId(1));

        order.Status.ShouldBe(OrderStatus.InProduction);
    }

    [Fact]
    public void UnderWholeOrderDispatchTheOrderIsReadyOnlyWhenEveryOutstandingGarmentIs()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        OrdersTestData.Ready(order, OrdersTestData.GarmentId(1));
        OrdersTestData.Ready(order, OrdersTestData.GarmentId(2));

        order.Status.ShouldBe(OrderStatus.Ready);
    }

    [Fact]
    public void UnderPerJobDispatchOneReadyGarmentIsEnough()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        OrdersTestData.Ready(order, OrdersTestData.GarmentId(1), ReadyAggregation.AnyDeliverableJob);

        order.Status.ShouldBe(OrderStatus.Ready);
    }

    [Fact]
    public void AGarmentAlreadyHandedOverDoesNotKeepTheOrderOutOfReady()
    {
        // "Every deliverable job" is every job the order still owes the customer, and a garment already at
        // the customer's door is not one of them.
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(1));
        OrdersTestData.Ready(order, OrdersTestData.GarmentId(2));

        order.Status.ShouldBe(OrderStatus.Ready);
    }

    [Fact]
    public void AGarmentStillInProductionKeepsTheOrderOutOfReady()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(1));
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(2));

        var blocked = ReadyGate.Evaluate(
            order,
            OrdersTestData.GarmentId(1),
            OrdersTestData.GateInputs(workflowComplete: false),
            Later);

        blocked.IsSuccess.ShouldBeTrue();
        order.ApplyReadyGate(
            OrdersTestData.GarmentId(1),
            blocked.Value,
            ReadyAggregation.EveryDeliverableJob,
            Later).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.InProduction);
        order.FindJob(OrdersTestData.GarmentId(1))!.IsReadyForDelivery.ShouldBeFalse();
    }

    [Fact]
    public void AGarmentThatStopsBeingReadyTakesTheOrderWithIt()
    {
        // The gate is recomputed on every rework, hold and custody event, and a verdict that closes is as
        // authoritative as one that opens.
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.Ready(order, OrdersTestData.GarmentId(1));
        order.Status.ShouldBe(OrderStatus.Ready);

        var closed = ReadyGate.Evaluate(
            order,
            OrdersTestData.GarmentId(1),
            OrdersTestData.GateInputs(qcPassed: false),
            Later);

        order.ApplyReadyGate(
            OrdersTestData.GarmentId(1),
            closed.Value,
            ReadyAggregation.EveryDeliverableJob,
            Later).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.InProduction);
    }

    [Fact]
    public void AHeldGarmentTakesTheOrderOutOfReady()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.Ready(order, OrdersTestData.GarmentId(1));

        order.Hold(
            OrdersTestData.GarmentId(1),
            "material-awaited",
            "The lining has not arrived from the supplier.",
            OrdersTestData.Approver,
            ReadyAggregation.EveryDeliverableJob,
            Later,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.InProduction);
    }

    [Fact]
    public void ReadyIsWrittenByTheGateAndByNothingElse()
    {
        // INV-JOB-07 and raci.md row 16: no role, however senior, declares a garment ready. The verdict's
        // constructor is private and its factory is internal, so no Application, Api or host code can
        // fabricate one — and the order accepts nothing else.
        typeof(ReadyGateOutcome).GetConstructors().ShouldBeEmpty();

        typeof(ReadyGateOutcome)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ShouldBeEmpty();

        typeof(Order).GetProperty(nameof(Order.Status))!.GetSetMethod().ShouldBeNull();
    }

    [Fact]
    public void AGateVerdictReachedForOneGarmentIsRefusedOnAnother()
    {
        // Applying one garment's verdict to another would make a garment ready on evidence that was never
        // about it.
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(1));
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(2));

        var verdict = ReadyGate.Evaluate(
            order,
            OrdersTestData.GarmentId(1),
            OrdersTestData.GateInputs(),
            Later);

        var refused = order.ApplyReadyGate(
            OrdersTestData.GarmentId(2),
            verdict.Value,
            ReadyAggregation.EveryDeliverableJob,
            Later);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.ready-gate-outcome-for-another-job");
        order.Status.ShouldBe(OrderStatus.InProduction);
    }

    /// <summary>
    /// SQ-02's open half arrives as an argument on every command that can move the order's status, so it
    /// crosses a public boundary eight times and a deserialiser can put any number a cast produces behind
    /// it. An unnamed value is not a weaker answer but an uninterpretable one, and reading it as the
    /// conservative rule would give a branch whose dispatch policy nobody recognised a rule it was never
    /// told about.
    /// </summary>
    [Fact]
    public void ADispatchPolicyThatIsNeitherOfTheTwoIsRefusedRatherThanReadAsTheConservativeOne()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        var refused = order.RecomputeStatus((ReadyAggregation)99, Later);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-not-understood");
        refused.Error.Target.ShouldBe("aggregation");
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    /// <summary>
    /// Asked at the top of the command rather than at the recomputation it ends with, so an unrecognised
    /// policy is a refusal and not a garment moved with the order's status left unanswered.
    /// </summary>
    [Fact]
    public void ACommandCarryingADispatchPolicyNobodyNamesChangesNothing()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        var refused = order.StartProduction(
            OrdersTestData.GarmentId(1),
            OrdersTestData.WorkflowVersion,
            [],
            (ReadyAggregation)99,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-not-understood");
        refused.Error.Target.ShouldBe("aggregation");
        order.FindJob(OrdersTestData.GarmentId(1))!.Status.ShouldBe(GarmentJobStatus.Confirmed);
        order.FindJob(OrdersTestData.GarmentId(1))!.WorkflowVersionId.ShouldBeNull();
        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.ProductionStartedAt.ShouldBeNull();
    }

    /// <summary>
    /// The recomputation asks its two questions in the order the eight commands that end with it ask theirs: a
    /// terminal order is left where it stands, and only then is the dispatch policy read. There is nothing for
    /// the policy to decide about an order at the end of its lifecycle — section 2.1 has no un-cancel — so
    /// refusing a value nothing was going to read would be one type answering one question two ways, the
    /// command refusing and the recomputation it ends with succeeding.
    /// </summary>
    [Fact]
    public void ATerminalOrderIsLeftWhereItStandsBeforeTheDispatchPolicyIsRead()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        order.Cancel(
            "customer-withdrew",
            "The customer withdrew the order at the counter.",
            [],
            Later,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var recomputed = order.RecomputeStatus((ReadyAggregation)99, Later);

        var refused = order.CancelJob(
            OrdersTestData.GarmentId(1),
            "customer-withdrew",
            "The customer withdrew this garment at the counter.",
            [],
            (ReadyAggregation)99,
            Later,
            OrdersTestData.Actor);

        recomputed.IsSuccess.ShouldBeTrue();
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.status-transition-not-allowed");
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    /* Delivered, and what never follows it ------------------------------------------------------- */

    [Fact]
    public void AnOrderIsDeliveredWhenEveryGarmentStillOwedHasBeenHandedOver()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(1));
        order.Status.ShouldBe(OrderStatus.InProduction);

        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(2));
        order.Status.ShouldBe(OrderStatus.Delivered);
    }

    /// <summary>
    /// <strong>A handover is taken against the garment's status and never against the order's.</strong>
    /// <c>Order.ConfirmDelivery</c> asks only that this garment stands at <see cref="GarmentJobStatus.Ready"/>,
    /// so under a branch that releases a garment as it finishes one can be handed over while the order is
    /// still <see cref="OrderStatus.InProduction"/> — and the order stays there, because the garment that
    /// kept it out of ready is still outstanding afterwards. That is the row section 2.1 was missing.
    /// </summary>
    [Fact]
    public void AGarmentIsHandedOverWhileTheOrderIsStillInProductionAndTheOrderStaysThere()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        OrdersTestData.Ready(order, OrdersTestData.GarmentId(1));
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(2));
        order.Status.ShouldBe(OrderStatus.InProduction);

        var handedOver = order.ConfirmDelivery(
            OrdersTestData.GarmentId(1),
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: true,
            Later,
            OrdersTestData.Actor);

        handedOver.IsSuccess.ShouldBeTrue();
        order.FindJob(OrdersTestData.GarmentId(1))!.Status.ShouldBe(GarmentJobStatus.Delivered);
        order.Status.ShouldBe(OrderStatus.InProduction);
        order.DeliveredAt.ShouldBeNull();
    }

    [Fact]
    public void ACancelledGarmentIsNotCountedWhenTheRestAreDelivered()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        OrdersTestData.CancelledJob(order, OrdersTestData.GarmentId(2));
        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(1));

        order.Status.ShouldBe(OrderStatus.Delivered);
    }

    [Fact]
    public void TheDateTheCustomerReceivedTheirGarmentsIsRecordedOnceAndNeverMoved()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(1));
        var received = order.DeliveredAt;

        order.RecomputeStatus(ReadyAggregation.EveryDeliverableJob, Later.AddDays(30)).IsSuccess.ShouldBeTrue();

        order.DeliveredAt.ShouldBe(received);
    }

    [Fact]
    public void ADeliveredOrderIsNeverClosedAutomatically()
    {
        // SQ-01 has not settled what closes a delivered order, and until it does, delivered is the last
        // automatic state. This is the interim position and must not be read as settled.
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(1));

        order.RecomputeStatus(ReadyAggregation.EveryDeliverableJob, Later.AddYears(1)).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Delivered);
        order.Status.ShouldNotBe(OrderStatus.Closed);
    }

    /* What the aggregation never writes ---------------------------------------------------------- */

    [Fact]
    public void CancellingEveryGarmentLeavesTheOrderExactlyWhereItWas()
    {
        // SQ-04's interim position: order cancellation stays an explicit, separately authorised and
        // reasoned command, because its financial consequences differ.
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        OrdersTestData.CancelledJob(order, OrdersTestData.GarmentId(1));
        OrdersTestData.CancelledJob(order, OrdersTestData.GarmentId(2));

        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.Status.ShouldNotBe(OrderStatus.Cancelled);
        order.CancelledAt.ShouldBeNull();
        order.CancellationReasonCode.ShouldBeNull();

        // Nothing was ever made, so nothing records a moment at which it was.
        order.ProductionStartedAt.ShouldBeNull();
    }

    /// <summary>
    /// SQ-02's interim position is "in production once <strong>any</strong> job has entered production",
    /// and 'non-cancelled' qualifies its delivered clause alone. Cancelling the garment that started
    /// therefore does not take the order backwards: section 2's order diagram draws no edge from in
    /// production to confirmed and section 2.1's table has no row producing one.
    /// </summary>
    [Fact]
    public void CancellingTheOnlyGarmentThatStartedLeavesTheOrderInProduction()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(1));
        OrdersTestData.CancelledJob(order, OrdersTestData.GarmentId(1));

        order.Status.ShouldBe(OrderStatus.InProduction);
        order.Status.ShouldNotBe(OrderStatus.Confirmed);
        order.ProductionStartedAt.ShouldBe(OrdersTestData.Now);
        order.HasEnteredProduction.ShouldBeTrue();

        // And the status and the refusal now say the same thing: the order reads as started, and a
        // revision is refused for having started. They used to disagree — confirmed on the screen, work
        // has started when somebody tried to act on it.
        var refused = order.Revise(
            OrdersTestData.Id("revision-2"),
            "Re-priced after the first garment was withdrawn.",
            OrdersTestData.Price(1200m),
            OrdersTestData.DueDate,
            Array.Empty<GarmentJobRevision>(),
            supersededEstimateId: null,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.revision-refused-after-production");
    }

    /// <summary>
    /// The mirror case, and the one the old reading got wrong in the other direction: a garment cancelled
    /// straight from confirmed was never started, so nothing about the order entered production and no
    /// production start date is recorded. Section 2.1 records the moment "the first garment job starts
    /// production" and no other.
    /// </summary>
    [Fact]
    public void AGarmentCancelledBeforeItWasEverStartedLeavesTheOrderConfirmedAndUnstarted()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        OrdersTestData.CancelledJob(order, OrdersTestData.GarmentId(1));

        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.HasEnteredProduction.ShouldBeFalse();
        order.ProductionStartedAt.ShouldBeNull();
    }

    [Fact]
    public void RecomputingACancelledOrderChangesNothing()
    {
        // A status read back from the database is never silently recomputed away.
        var order = OrdersTestData.ConfirmedOrder();
        order.Cancel(
            "customer-withdrew",
            "The customer withdrew the order at the counter.",
            Array.Empty<string>(),
            Later,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        order.RecomputeStatus(ReadyAggregation.EveryDeliverableJob, Later.AddHours(1)).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.ProductionStartedAt.ShouldBeNull();
    }

    [Fact]
    public void AnOrderNeverReturnsToDraft()
    {
        // No Order instance is ever in the draft position: that belongs to the OrderDraft aggregate, and
        // an order row exists only from confirmation onwards.
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        order.Status.ShouldNotBe(OrderStatus.Draft);

        OrdersTestData.Ready(order, OrdersTestData.GarmentId(1));
        OrdersTestData.Ready(order, OrdersTestData.GarmentId(2));
        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(1));
        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(2));
        order.RecomputeStatus(ReadyAggregation.AnyDeliverableJob, Later).IsSuccess.ShouldBeTrue();

        order.Status.ShouldNotBe(OrderStatus.Draft);
    }
}
