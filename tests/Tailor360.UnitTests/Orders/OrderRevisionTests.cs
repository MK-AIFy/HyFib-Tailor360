using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// Revising a confirmed order before production — the append-only history, and the window it closes in.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OrderRevisionTests
{
    private static readonly DateTimeOffset Later = OrdersTestData.Now.AddHours(1);

    /* What a revision records -------------------------------------------------------------------- */

    [Fact]
    public void ARevisionAppendsAPositionRatherThanRewritingThePreviousOne()
    {
        // state-transitions.md section 2.1: a revision never rewrites the previous snapshot.
        var order = OrdersTestData.ConfirmedOrder();
        var first = order.Revisions.ShouldHaveSingleItem();
        var wasPricedAt = first.Totals;

        var revised = Revise(order, grandTotal: 3200m);

        revised.IsSuccess.ShouldBeTrue();
        order.Revisions.Count.ShouldBe(2);
        order.Revisions.First().Totals.ShouldBe(wasPricedAt);
        order.Revisions.First().DueDate.ShouldBe(OrdersTestData.DueDate);
        order.Revisions.Last().RevisionNumber.ShouldBe(2);
        order.Revisions.Last().Totals.GrandTotal.Amount.ShouldBe(3200m);
    }

    [Fact]
    public void ARevisionMovesTheOrdersOwnTotalsAndPromisedDate()
    {
        var order = OrdersTestData.ConfirmedOrder();

        Revise(order, grandTotal: 3200m, dueDate: OrdersTestData.DueDate.AddDays(7)).IsSuccess.ShouldBeTrue();

        order.RevisionNumber.ShouldBe(2);
        order.Totals.GrandTotal.Amount.ShouldBe(3200m);
        order.DueDate.ShouldBe(OrdersTestData.DueDate.AddDays(7));
        order.UpdatedAt.ShouldBe(Later);
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public void ARevisionNeverChangesWhichOrderThisIs()
    {
        // INV-ORD-03: a display number is allocated once and never re-pointed.
        var order = OrdersTestData.ConfirmedOrder();
        var number = order.OrderNumber;
        var id = order.Id;

        Revise(order).IsSuccess.ShouldBeTrue();

        order.Id.ShouldBe(id);
        order.OrderNumber.ShouldBe(number);
        order.OrderDraftId.ShouldBe(OrdersTestData.Id("draft"));
    }

    [Fact]
    public void ARevisionReSnapshotsOnlyTheGarmentsItNames()
    {
        // Re-pricing one garment is an ordinary revision; the order's own totals move regardless, because
        // Billing computed them and this module never adds up its own money (INV-ORD-07).
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        var untouched = order.FindJob(OrdersTestData.GarmentId(2))!.Price;

        var revised = Revise(order, garments: [GarmentRevision(OrdersTestData.GarmentId(1), 900m)]);

        revised.IsSuccess.ShouldBeTrue();
        order.FindJob(OrdersTestData.GarmentId(1))!.Price.GrandTotal.Amount.ShouldBe(900m);
        order.FindJob(OrdersTestData.GarmentId(2))!.Price.ShouldBe(untouched);
    }

    [Fact]
    public void ARevisionNamingNoGarmentStillRepricesTheOrder()
    {
        var order = OrdersTestData.ConfirmedOrder();

        Revise(order, grandTotal: 5000m).IsSuccess.ShouldBeTrue();

        order.Totals.GrandTotal.Amount.ShouldBe(5000m);
        order.Revisions.Count.ShouldBe(2);
    }

    /// <summary>
    /// INV-ORD-02 is a statement about the order, and the revision is where it is easiest to break: the
    /// totals are replaced wholesale from the caller while the garments nobody re-priced keep the versions
    /// they were confirmed under. A re-price against a newly published catalogue therefore has to carry
    /// every garment with it or be refused — and refused before anything is applied, so a half re-priced
    /// order is never left behind.
    /// </summary>
    [Fact]
    public void ARevisionThatRepricesUnderANewCatalogueWithoutCarryingEveryGarmentIsRefused()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        var frozen = order.FindJob(OrdersTestData.GarmentId(2))!.Price;
        var republished = OrdersTestData.Id("a-newer-catalogue-version");

        var refused = order.Revise(
            OrdersTestData.Id("revision-2"),
            "Re-priced against the catalogue published this morning.",
            RepricedUnder(republished),
            OrdersTestData.DueDate,
            [
                GarmentJobRevision.Create(
                    OrdersTestData.GarmentId(1),
                    OrdersTestData.Measurements(),
                    DesignUnder(republished),
                    RepricedUnder(republished),
                    OrdersTestData.DueDate).Value,
            ],
            supersededEstimateId: null,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.configuration-version-not-shared");
        refused.Error.Target.ShouldBe("catalogVersionId");
        order.RevisionNumber.ShouldBe(Order.FirstRevisionNumber);
        order.FindJob(OrdersTestData.GarmentId(1))!.Price.ShouldBe(frozen);
        order.FindJob(OrdersTestData.GarmentId(2))!.Price.ShouldBe(frozen);
    }

    [Fact]
    public void ARevisionRecordsTheEstimateItRetires()
    {
        var order = OrdersTestData.ConfirmedOrder();
        var outstanding = OrdersTestData.Id("estimate-2");

        Revise(order, supersededEstimateId: outstanding).IsSuccess.ShouldBeTrue();

        order.Revisions.Last().SupersededEstimateId.ShouldBe(outstanding);
        order.Revisions.First().SupersededEstimateId.ShouldBeNull();
    }

    /* The window a revision closes in ------------------------------------------------------------ */

    [Fact]
    public void ARevisionIsRefusedOnceAJobHasEnteredProduction()
    {
        // INV-ORD-05 and INV-JOB-02: the workflow version is pinned at start of production and the order
        // stops being re-priceable from that moment. The only route afterwards is an alteration request.
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(1));

        var refused = Revise(order);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.revision-refused-after-production");
    }

    [Fact]
    public void ARevisionIsRefusedWhileAGarmentIsHeld()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(1));
        order.Hold(
            OrdersTestData.GarmentId(1),
            "material-awaited",
            "The lining has not arrived from the supplier.",
            OrdersTestData.Approver,
            ReadyAggregation.EveryDeliverableJob,
            Later,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var refused = Revise(order);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.revision-refused-after-production");
    }

    [Fact]
    public void ARevisionIsRefusedWhileACancelledGarmentStandsEvenThoughEveryOtherOneIsStillConfirmed()
    {
        // The literal reading of INV-ORD-05, and it is a choice worth seeing: "every job still confirmed"
        // is not "every job that is left". Somebody has already acted on the cancelled garment.
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        OrdersTestData.CancelledJob(order, OrdersTestData.GarmentId(2));

        order.Status.ShouldBe(OrderStatus.Confirmed);

        var refused = Revise(order);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.revision-refused-after-production");
    }

    [Fact]
    public void ARevisionIsRefusedOnADeliveredOrder()
    {
        var order = OrdersTestData.ConfirmedOrder();
        OrdersTestData.Delivered(order, OrdersTestData.GarmentId(1));

        var refused = Revise(order);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.revision-refused-after-production");
    }

    [Fact]
    public void ARevisionIsRefusedOnACancelledOrder()
    {
        // Refused for being cancelled rather than for having started: every garment on it is still
        // confirmed, and there is no alteration to raise against an order nobody is making.
        var order = OrdersTestData.ConfirmedOrder();
        Cancel(order).IsSuccess.ShouldBeTrue();

        var refused = Revise(order);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.status-transition-not-allowed");
    }

    [Fact]
    public void ARefusedRevisionLeavesEveryFrozenSnapshotAlone()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        var before = order.FindJob(OrdersTestData.GarmentId(2))!.Price;
        OrdersTestData.InProduction(order, OrdersTestData.GarmentId(1));

        Revise(order, garments: [GarmentRevision(OrdersTestData.GarmentId(2), 900m)]).IsFailure.ShouldBeTrue();

        order.RevisionNumber.ShouldBe(Order.FirstRevisionNumber);
        order.Revisions.Count.ShouldBe(1);
        order.FindJob(OrdersTestData.GarmentId(2))!.Price.ShouldBe(before);
    }

    /* What a revision refuses -------------------------------------------------------------------- */

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ARevisionWithoutAReasonIsRefused(string? reason)
    {
        // state-transitions.md section 8: the reason is never optional and never defaulted by the client.
        var refused = Revise(OrdersTestData.ConfirmedOrder(), reason);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.reason-required");
    }

    [Fact]
    public void AReasonLongerThanTheColumnIsRefused()
    {
        var refused = Revise(OrdersTestData.ConfirmedOrder(), new string('x', Order.MaximumReasonLength + 1));

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-too-long");
        refused.Error.Target.ShouldBe("reason");
    }

    [Fact]
    public void ARevisionWithoutAnIdentityOfItsOwnIsRefused()
    {
        var refused = Revise(OrdersTestData.ConfirmedOrder(), revisionId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("revisionId");
    }

    [Fact]
    public void AnEmptySupersededEstimateIdentifierIsRefusedRatherThanReadAsNoEstimate()
    {
        var refused = Revise(OrdersTestData.ConfirmedOrder(), supersededEstimateId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("supersededEstimateId");
    }

    [Fact]
    public void ARevisionNamingAGarmentThatIsNotOnTheOrderLeavesTheOrderExactlyAsItWas()
    {
        // Resolved before anything is applied, so a revision naming an unknown garment leaves the order as
        // it was rather than half re-priced.
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        var wasPricedAt = order.Totals;

        var refused = Revise(
            order,
            garments:
            [
                GarmentRevision(OrdersTestData.GarmentId(1), 900m),
                GarmentRevision(OrdersTestData.Id("a-garment-of-another-order"), 900m),
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.garment-job-not-found");
        order.RevisionNumber.ShouldBe(Order.FirstRevisionNumber);
        order.Totals.ShouldBe(wasPricedAt);
        order.FindJob(OrdersTestData.GarmentId(1))!.Price.ShouldBe(wasPricedAt);
    }

    /// <summary>
    /// INV-ORD-01 wants the whole set to move or none of it, so <c>GarmentJob.CheckRevision</c> is asked of
    /// every garment before any is applied — the category and service type included. A revision whose second
    /// garment carries a design copy answering a different category is therefore refused whole, and the first
    /// garment keeps the snapshots it was confirmed with rather than being left re-priced against a revision
    /// that never happened.
    /// </summary>
    [Fact]
    public void ARevisionIsRefusedWholeWhenOneGarmentSwapsInAnotherGarmentsDesign()
    {
        var order = OrdersTestData.ConfirmedOrder(garments: 2);
        var frozenPrice = order.FindJob(OrdersTestData.GarmentId(1))!.Price;
        var frozenDesign = order.FindJob(OrdersTestData.GarmentId(1))!.Design;

        var refused = Revise(
            order,
            garments:
            [
                GarmentRevision(OrdersTestData.GarmentId(1), 900m),
                GarmentJobRevision.Create(
                    OrdersTestData.GarmentId(2),
                    OrdersTestData.Measurements(),
                    OrdersTestData.Design("square", categoryKey: "shirt"),
                    OrdersTestData.Price(900m),
                    OrdersTestData.DueDate).Value,
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.design-snapshot-not-for-this-garment");
        refused.Error.Target.ShouldBe("categoryKey");
        order.RevisionNumber.ShouldBe(Order.FirstRevisionNumber);
        order.FindJob(OrdersTestData.GarmentId(1))!.Price.ShouldBe(frozenPrice);
        order.FindJob(OrdersTestData.GarmentId(1))!.Design.ShouldBeSameAs(frozenDesign);
        order.FindJob(OrdersTestData.GarmentId(2))!.CategoryKey.ShouldBe("blouse");
    }

    [Fact]
    public void ARevisionNamingOneGarmentTwiceIsRefused()
    {
        var order = OrdersTestData.ConfirmedOrder();

        var refused = Revise(
            order,
            garments:
            [
                GarmentRevision(OrdersTestData.GarmentId(1), 900m),
                GarmentRevision(OrdersTestData.GarmentId(1), 1200m),
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.duplicate-garment-job");
        refused.Error.Target.ShouldBe("garmentJobId");
    }

    [Fact]
    public void AMissingGarmentInARevisionIsRefusedRatherThanSkipped()
    {
        var refused = Revise(OrdersTestData.ConfirmedOrder(), garments: [null!]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("garments");
    }

    [Fact]
    public void ARevisionOfNothingIsADefectRatherThanARefusal()
    {
        var order = OrdersTestData.ConfirmedOrder();

        Should.Throw<ArgumentNullException>(() => order.Revise(
            OrdersTestData.Id("revision-2"),
            "Re-priced at the customer's request.",
            null!,
            OrdersTestData.DueDate,
            [],
            supersededEstimateId: null,
            Later,
            OrdersTestData.Actor));

        Should.Throw<ArgumentNullException>(() => order.Revise(
            OrdersTestData.Id("revision-2"),
            "Re-priced at the customer's request.",
            OrdersTestData.Price(),
            OrdersTestData.DueDate,
            null!,
            supersededEstimateId: null,
            Later,
            OrdersTestData.Actor));
    }

    [Fact]
    public void AGarmentRevisionWithoutAGarmentIsRefused()
    {
        var refused = GarmentJobRevision.Create(
            Guid.Empty,
            OrdersTestData.Measurements(),
            OrdersTestData.Design(),
            OrdersTestData.Price(),
            OrdersTestData.DueDate);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("garmentJobId");
    }

    /* Append-only, and immutable once taken ------------------------------------------------------ */

    [Fact]
    public void TheHistoryOfWhatAnOrderWasPricedAtHasNoMutator()
    {
        // A mistaken revision is corrected by a further revision, with its own reason — never by editing
        // the row that is already there.
        typeof(OrderRevision).GetConstructors().ShouldBeEmpty();

        typeof(OrderRevision)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldAllBe(property => property.GetSetMethod() == null);

        typeof(OrderRevision)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ShouldBeEmpty();
    }

    [Fact]
    public void OnlyTheOrderItselfMayWriteAPricedPositionIntoTheHistory()
    {
        // The factory is internal, so a revision row written by anything but the aggregate is impossible
        // rather than merely discouraged.
        typeof(OrderRevision)
            .GetMethod("Record", BindingFlags.Public | BindingFlags.Static)
            .ShouldBeNull();
    }

    [Fact]
    public void APricedPositionCannotBeMovedAwayFromTheVersionsItWasCalculatedUnder()
    {
        // A snapshot is immutable once taken (INV-ORD-02). The constructor is private and every property
        // is get-only rather than `init`, which is what closes `with` as a second way past the factory.
        typeof(PriceSnapshot).GetConstructors().ShouldBeEmpty();

        typeof(PriceSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldAllBe(property => property.GetSetMethod() == null);
    }

    /// <summary>A priced result worked out against a catalogue version other than the order's own.</summary>
    private static PriceSnapshot RepricedUnder(Guid catalogVersionId)
        => PriceSnapshot.Create(
            catalogVersionId,
            OrdersTestData.PriceListVersion,
            OrdersTestData.TaxConfigurationVersion,
            Money.Rupees(3200m),
            Money.Zero,
            Money.Rupees(3200m),
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Rupees(3200m),
            Later).Value;

    /// <summary>A design copy validated against a catalogue version other than the order's own.</summary>
    private static DesignSnapshot DesignUnder(Guid catalogVersionId)
        => DesignSnapshot.Create(
            catalogVersionId,
            "blouse",
            "Blouse",
            "stitch-new",
            "Stitch a new garment",
            [],
            garmentInstructions: null,
            conditionalNotes: null,
            frozenAt: Later).Value;

    private static GarmentJobRevision GarmentRevision(Guid garmentJobId, decimal grandTotal)
        => GarmentJobRevision.Create(
            garmentJobId,
            OrdersTestData.Measurements(),
            OrdersTestData.Design("square"),
            OrdersTestData.Price(grandTotal),
            OrdersTestData.DueDate.AddDays(7)).Value;

    private static Result Revise(
        Order order,
        string? reason = "The customer chose a different neckline before any work started.",
        IReadOnlyCollection<GarmentJobRevision>? garments = null,
        Guid? revisionId = null,
        Guid? supersededEstimateId = null,
        decimal grandTotal = 3200m,
        DateOnly? dueDate = null)
        => order.Revise(
            revisionId ?? OrdersTestData.Id("revision-2"),
            reason,
            OrdersTestData.Price(grandTotal),
            dueDate ?? OrdersTestData.DueDate,
            garments ?? Array.Empty<GarmentJobRevision>(),
            supersededEstimateId,
            Later,
            OrdersTestData.Actor);

    private static Result Cancel(Order order)
        => order.Cancel(
            "customer-withdrew",
            "The customer withdrew the order at the counter.",
            Array.Empty<string>(),
            Later,
            OrdersTestData.Actor);
}
