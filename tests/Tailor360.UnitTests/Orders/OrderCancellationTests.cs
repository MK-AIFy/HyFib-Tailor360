using Shouldly;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// Cancelling an order, and cancelling one garment of one — blocked rather than forced, and never a delete.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OrderCancellationTests
{
    private static readonly DateTimeOffset Later = OrdersTestData.Now.AddHours(2);

    /* Cancelling the order ----------------------------------------------------------------------- */

    [Fact]
    public void CancellingAnOrderRecordsWhoCancelledItWhyAndWhen()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var cancelled = Cancel(order);

        cancelled.IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancelledAt.ShouldBe(Later);
        order.CancelledBy.ShouldBe(OrdersTestData.Actor);
        order.CancellationReasonCode.ShouldBe("customer-withdrew");
        order.CancellationReason.ShouldBe("The customer withdrew the order at the counter.");
    }

    [Fact]
    public void TheReasonAndItsCodeAreStoredWithoutTheWhitespaceAroundThem()
    {
        var order = OrdersTestData.ConfirmedOrder();

        Cancel(order, "  customer-withdrew  ", "  Withdrawn at the counter.  ").IsSuccess.ShouldBeTrue();

        order.CancellationReasonCode.ShouldBe("customer-withdrew");
        order.CancellationReason.ShouldBe("Withdrawn at the counter.");
    }

    [Fact]
    public void AConfirmedOrderMayBeCancelled()
    {
        var order = OrdersTestData.ConfirmedOrder();

        Cancel(order).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void AnOrderInProductionMayBeCancelled()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(1));

        Cancel(order).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void AReadyOrderMayBeCancelled()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.Ready(order, OrdersTestData.GarmentId(1));

        Cancel(order).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void ADeliveredOrderCannotBeCancelled()
    {
        // The garments have physically left. The remedy is a failed or returned delivery recorded by
        // Custody, never an undo of the handover.
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(1));

        var refused = Cancel(order);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.status-transition-not-allowed");
        order.Status.ShouldBe(OrderStatus.Delivered);
    }

    [Fact]
    public void ThereIsNoUnCancelAndNoSecondCancellation()
    {
        // A customer who changes their mind again is served by a new order that may reuse the same
        // measurement version.
        var order = OrdersTestData.ConfirmedOrder();
        Cancel(order).IsSuccess.ShouldBeTrue();
        var when = order.CancelledAt;

        var refused = Cancel(order, reason: "Cancelled again by mistake.");

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.status-transition-not-allowed");
        order.CancelledAt.ShouldBe(when);
        order.CancellationReason.ShouldBe("The customer withdrew the order at the counter.");
    }

    [Fact]
    public void CancellationIsBlockedWhileAProhibitedStateStandsRatherThanForcedThrough()
    {
        // INV-ORD-06 and exceptions.md EX-08: the compensating flows — the credit note, the returned
        // material, the custody correction — run first, and nothing is deleted to make room for them.
        var order = OrdersTestData.ConfirmedOrder();

        var refused = Cancel(order, prohibitedStates: ["posted-invoice-with-recognised-value"]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.cancellation-blocked");
        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.CancelledAt.ShouldBeNull();
        order.CancellationReason.ShouldBeNull();
    }

    [Fact]
    public void ABlockingStateNobodyNamedIsADefectInTheCallerRatherThanABlocker()
    {
        // A refusal that cannot say what is blocking leaves the person at the counter with nothing to act
        // on, so a blank entry is refused rather than reported as the blocking state.
        var order = OrdersTestData.ConfirmedOrder();

        var refused = Cancel(order, prohibitedStates: ["   "]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("prohibitedStates");
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CancellingWithoutTheConfiguredReasonCodeIsRefused(string? reasonCode)
    {
        var refused = Cancel(OrdersTestData.ConfirmedOrder(), reasonCode);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.reason-code-required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CancellingWithoutAReasonIsRefused(string? reason)
    {
        // The code is what a report groups by; the sentence is what the next person to open the order
        // reads. Neither substitutes for the other (state-transitions.md section 8).
        var refused = Cancel(OrdersTestData.ConfirmedOrder(), reason: reason);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.reason-required");
    }

    [Fact]
    public void AReasonCodeLongerThanTheColumnIsRefused()
    {
        var tooLong = new string('x', Order.MaximumReasonCodeLength + 1);

        var refused = Cancel(OrdersTestData.ConfirmedOrder(), tooLong);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-too-long");
        refused.Error.Target.ShouldBe("reasonCode");
    }

    [Fact]
    public void AReasonLongerThanTheColumnIsRefused()
    {
        var tooLong = new string('x', Order.MaximumReasonLength + 1);

        var refused = Cancel(OrdersTestData.ConfirmedOrder(), reason: tooLong);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-too-long");
        refused.Error.Target.ShouldBe("reason");
    }

    [Fact]
    public void CancellingAnOrderDeletesNothingAndCascadesIntoNoGarment()
    {
        // G-5 keeps every row. No document says that cancelling an order cancels each of its garments, and
        // a cascade would write a cancellation with no reason code of its own onto rows that each need one.
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        Cancel(order).IsSuccess.ShouldBeTrue();

        order.Jobs.Count.ShouldBe(2);
        order.Jobs.ShouldAllBe(job => job.Status == GarmentJobStatus.Confirmed);
        order.Jobs.ShouldAllBe(job => job.CancellationReasonCode == null);
        order.Revisions.Count.ShouldBe(1);
    }

    [Fact]
    public void ACancelledOrderRefusesEveryFurtherCommand()
    {
        var order = OrdersTestData.ConfirmedOrder();
        Cancel(order).IsSuccess.ShouldBeTrue();
        var garment = OrdersTestData.GarmentId(1);

        var started = order.StartProduction(
            garment,
            OrdersTestData.WorkflowVersion,
            Array.Empty<Guid>(),
            ReadyAggregation.EveryDeliverableJob,
            Later,
            OrdersTestData.Actor);

        // Including the one command that used not to be refused. Order.Cancel deliberately does not
        // cascade (SQ-04), so a cancelled order still holds live garments — and the gate promoted one of
        // them to ready, which is a delivery queue entry for an order nobody is making.
        var evaluated = ReadyGate.Evaluate(order, garment, OrdersTestData.GateInputs(), Later);
        var gated = order.ApplyReadyGate(
            garment,
            evaluated.Value,
            ReadyAggregation.EveryDeliverableJob,
            Later);

        var held = order.Hold(
            garment,
            "material-awaited",
            "The lining has not arrived.",
            OrdersTestData.Approver,
            ReadyAggregation.EveryDeliverableJob,
            Later,
            OrdersTestData.Actor);

        var rescheduled = order.Reschedule(
            garment,
            OrdersTestData.DueDate.AddDays(3),
            "The customer asked for a later date.",
            Later,
            OrdersTestData.Actor);

        var delivered = order.ConfirmDelivery(
            garment,
            ReadyAggregation.EveryDeliverableJob,
            partialDeliveryPermitted: false,
            Later,
            OrdersTestData.Actor);

        foreach (var refused in new[] { started, held, rescheduled, delivered, gated })
        {
            refused.IsFailure.ShouldBeTrue();
            refused.Error.Code.ShouldBe("orders.status-transition-not-allowed");
        }

        order.FindJob(garment)!.Status.ShouldBe(GarmentJobStatus.Confirmed);
        order.FindJob(garment)!.IsReadyForDelivery.ShouldBeFalse();
    }

    [Fact]
    public void AListOfBlockingStatesNobodyGatheredIsADefectRatherThanARefusal()
    {
        var order = OrdersTestData.ConfirmedOrder();

        Should.Throw<ArgumentNullException>(() => order.Cancel(
            "customer-withdrew",
            "The customer withdrew the order at the counter.",
            null!,
            Later,
            OrdersTestData.Actor));
    }

    /* Cancelling one garment --------------------------------------------------------------------- */

    [Fact]
    public void CancellingAGarmentIsBlockedWhileAProhibitedStateStands()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);

        var refused = order.CancelJob(
            OrdersTestData.GarmentId(1),
            "customer-withdrew",
            "The customer withdrew this garment.",
            ["material-in-another-custodians-hands"],
            ReadyAggregation.EveryDeliverableJob,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.job-cancellation-blocked");
        order.FindJob(OrdersTestData.GarmentId(1))!.Status.ShouldBe(GarmentJobStatus.Confirmed);
    }

    [Fact]
    public void CancellingAGarmentThatIsNotOnTheOrderIsRefused()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var refused = order.CancelJob(
            OrdersTestData.Id("a-garment-of-another-order"),
            "customer-withdrew",
            "The customer withdrew this garment.",
            Array.Empty<string>(),
            ReadyAggregation.EveryDeliverableJob,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.garment-job-not-found");
    }

    /// <summary>
    /// The garment is resolved before its own rules are applied, as every other job-scoped command on the
    /// aggregate does. Asking about the blockers first named one against a garment the order does not
    /// have — "this garment cannot be cancelled while unreturned-customer-material stands" about a garment
    /// that is not on the order at all.
    /// </summary>
    [Fact]
    public void AGarmentThatIsNotOnTheOrderIsSaidSoEvenWhileABlockerStands()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var refused = order.CancelJob(
            OrdersTestData.Id("a-garment-of-another-order"),
            "customer-withdrew",
            "The customer withdrew this garment.",
            ["unreturned-customer-material"],
            ReadyAggregation.EveryDeliverableJob,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.garment-job-not-found");
    }

    /// <summary>
    /// The first blocking state is interpolated into a problem detail, so it is bounded like every other
    /// code this module carries. Without the bound an application passing a sentence — or anything derived
    /// from a customer record — put it straight into an RFC 9457 response, which the catalogue's own header
    /// promises never happens.
    /// </summary>
    [Fact]
    public void ABlockingStateLongerThanACodeIsRefusedRatherThanPutIntoTheMessage()
    {
        var order = OrdersTestData.ConfirmedOrder();
        var sentence = new string('x', Order.MaximumReasonCodeLength + 1);

        var refused = Cancel(order, prohibitedStates: [sentence]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-too-long");
        refused.Error.Target.ShouldBe("prohibitedStates");
        refused.Error.Message.ShouldNotContain(sentence);
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public void CancellingAGarmentOnACancelledOrderIsRefused()
    {
        var order = OrdersTestData.ConfirmedOrder();
        Cancel(order).IsSuccess.ShouldBeTrue();

        var refused = order.CancelJob(
            OrdersTestData.GarmentId(1),
            "customer-withdrew",
            "The customer withdrew this garment.",
            Array.Empty<string>(),
            ReadyAggregation.EveryDeliverableJob,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.status-transition-not-allowed");
    }

    [Fact]
    public void CancellingAGarmentKeepsTheRowItsSnapshotsAndItsReason()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        var frozen = order.FindJob(OrdersTestData.GarmentId(1))!.Design;

        OrdersTestData.CancelledJob(order, OrdersTestData.GarmentId(1));

        var job = order.FindJob(OrdersTestData.GarmentId(1));
        job.ShouldNotBeNull();
        job.Status.ShouldBe(GarmentJobStatus.Cancelled);
        job.CancellationReasonCode.ShouldBe("customer-withdrew");
        job.Design.ShouldBe(frozen);
        order.Jobs.Count.ShouldBe(2);
    }

    private static Result Cancel(
        Order order,
        string? reasonCode = "customer-withdrew",
        string? reason = "The customer withdrew the order at the counter.",
        IReadOnlyCollection<string>? prohibitedStates = null)
        => order.Cancel(
            reasonCode,
            reason,
            prohibitedStates ?? Array.Empty<string>(),
            Later,
            OrdersTestData.Actor);
}
