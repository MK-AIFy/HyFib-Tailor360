using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// Confirming a draft into an order — the single factory, and the checks only the whole set can make.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OrderConfirmationTests
{
    /* What a confirmation produces --------------------------------------------------------------- */

    [Fact]
    public void AConfirmedOrderStartsConfirmedAtRevisionOne()
    {
        var order = OrdersTestData.ConfirmedOrder();

        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.RevisionNumber.ShouldBe(Order.FirstRevisionNumber);
        order.ConfirmedAt.ShouldBe(OrdersTestData.Now);
        order.ConfirmedBy.ShouldBe(OrdersTestData.Actor);
        order.ProductionStartedAt.ShouldBeNull();
        order.DeliveredAt.ShouldBeNull();
        order.CancelledAt.ShouldBeNull();
    }

    [Fact]
    public void ConfirmationIsTheOnlyWayAnOrderComesIntoExistence()
    {
        // Both constructors are private, so there is no partly built order for a transaction to persist
        // (INV-ORD-01) and no route to one that skips the set checks below.
        typeof(Order).GetConstructors().ShouldBeEmpty();
    }

    [Fact]
    public void ConfirmationWritesRevisionOneSoTheHistoryIsComplete()
    {
        // There is no priced position the order has ever held that is not a row.
        var order = OrdersTestData.ConfirmedOrder();

        var revision = order.Revisions.ShouldHaveSingleItem();
        revision.RevisionNumber.ShouldBe(Order.FirstRevisionNumber);
        revision.OrderId.ShouldBe(order.Id);
        revision.Totals.ShouldBe(order.Totals);
        revision.DueDate.ShouldBe(order.DueDate);
        revision.RecordedAt.ShouldBe(OrdersTestData.Now);
        revision.RecordedBy.ShouldBe(OrdersTestData.Actor);
    }

    [Fact]
    public void RevisionOneCarriesNoReasonBecauseAConfirmationIsNotARevision()
    {
        var revision = OrdersTestData.ConfirmedOrder().Revisions.ShouldHaveSingleItem();

        revision.Reason.ShouldBeNull();
        revision.SupersededEstimateId.ShouldBeNull();
    }

    [Fact]
    public void EveryGarmentIsCreatedConfirmedWithItsSnapshotsFrozen()
    {
        var number = OrdersTestData.Number();
        var specification = OrdersTestData.Garment(number);

        var order = OrdersTestData.ConfirmedOrderOf([specification], number);

        var job = order.Jobs.ShouldHaveSingleItem();
        job.Status.ShouldBe(GarmentJobStatus.Confirmed);
        job.OrderId.ShouldBe(order.Id);
        job.BranchId.ShouldBe(OrdersTestData.Branch);
        job.WorkflowVersionId.ShouldBeNull();
        job.Measurements.ShouldBe(specification.Measurements);
        job.Design.ShouldBe(specification.Design);
        job.Price.ShouldBe(specification.Price);
    }

    [Fact]
    public void GarmentsAreHeldInPositionOrderWhateverOrderTheyArrivedIn()
    {
        // A job-card set and a workboard read the same way whatever order the intake screen sent.
        var number = OrdersTestData.Number();

        var order = OrdersTestData.ConfirmedOrderOf(
            [
                OrdersTestData.Garment(number, 3),
                OrdersTestData.Garment(number, 1),
                OrdersTestData.Garment(number, 2),
            ],
            number);

        order.Jobs.Select(job => job.JobIndex).ShouldBe(Enumerable.Range(1, 3));
    }

    [Fact]
    public void AnOrderHoldsTheCustomerAsAnIdentifierAndNeverAsAPerson()
    {
        // Security rule 8: no name, no telephone number, no address anywhere on the aggregate.
        var personal = new[] { "name", "phone", "telephone", "email", "address", "measurement" };

        var leaked = typeof(Order)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => personal.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        leaked.ShouldBeEmpty();
        OrdersTestData.ConfirmedOrder().CustomerId.ShouldBe(OrdersTestData.Customer);
    }

    /* What a confirmation refuses ---------------------------------------------------------------- */

    [Fact]
    public void AnOrderCannotBeConfirmedWithoutAnIdentity()
    {
        var refused = Confirm(id: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("id");
    }

    [Fact]
    public void AnOrderCannotBeConfirmedOutsideAnOrganisation()
    {
        var refused = Confirm(organisationId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("organisationId");
    }

    [Fact]
    public void AnOrderCannotBeConfirmedOutsideABranch()
    {
        // Branch scope is evaluated, never inferred: an order with no branch has no sequence its display
        // number could honestly have come from.
        var refused = Confirm(branchId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.no-branch-in-context");
    }

    [Fact]
    public void AnOrderCannotBeConfirmedWithoutACustomer()
    {
        var refused = Confirm(customerId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("customerId");
    }

    [Fact]
    public void AnOrderCannotBeConfirmedWithoutTheDraftItCameFrom()
    {
        var refused = Confirm(orderDraftId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("orderDraftId");
    }

    [Fact]
    public void AnEmptyEstimateIdentifierIsRefusedRatherThanReadAsNoEstimate()
    {
        // A caller that passed `default` where it meant `null` would otherwise file the order against an
        // estimate that does not exist.
        var refused = Confirm(estimateId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("estimateId");
    }

    [Fact]
    public void AnOrderCannotBeConfirmedWithoutAnIdentityForRevisionOne()
    {
        var refused = Confirm(initialRevisionId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("initialRevisionId");
    }

    [Fact]
    public void AnOrderCannotBeConfirmedWithoutAGarment()
    {
        // An order is a commitment about garments, and a confirmation carrying none is a commitment to
        // nothing, with nothing to make a job card from.
        var refused = Confirm(garments: []);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.order-has-no-garment-jobs");
    }

    [Fact]
    public void AMissingGarmentInTheSetIsRefusedRatherThanSkipped()
    {
        var refused = Confirm(garments: [null!]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("garments");
    }

    [Fact]
    public void NotesLongerThanTheColumnAreRefused()
    {
        var refused = Confirm(notes: new string('x', Order.MaximumNotesLength + 1));

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-too-long");
        refused.Error.Target.ShouldBe("notes");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotesNobodyWroteAreStoredAsNothing(string? notes)
    {
        var order = Confirm(notes: notes);

        order.IsSuccess.ShouldBeTrue();
        order.Value.Notes.ShouldBeNull();
    }

    [Fact]
    public void NotesAreStoredWithoutTheWhitespaceAroundThem()
    {
        var order = Confirm(notes: "  Collect after six.  ");

        order.IsSuccess.ShouldBeTrue();
        order.Value.Notes.ShouldBe("Collect after six.");
    }

    [Fact]
    public void AGarmentNumberedForAnotherOrderIsRefused()
    {
        // Structural rather than conventional: a garment number carries the order number it was minted
        // from, so a job card printed for one order can never be filed against another.
        var number = OrdersTestData.Number(sequence: 1);
        var elsewhere = OrdersTestData.Garment(OrdersTestData.Number(sequence: 2));

        var refused = Confirm(orderNumber: number, garments: [elsewhere]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.garment-job-number-not-of-this-order");
    }

    [Fact]
    public void TwoGarmentsWithTheSameIdentityAreRefused()
    {
        var number = OrdersTestData.Number();
        var twice = OrdersTestData.GarmentId(1);

        var refused = Confirm(
            orderNumber: number,
            garments:
            [
                OrdersTestData.Garment(number, 1, twice),
                OrdersTestData.Garment(number, 2, twice),
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.duplicate-garment-job");
        refused.Error.Target.ShouldBe("garmentJobId");
    }

    [Fact]
    public void TwoGarmentsAtTheSamePositionAreRefused()
    {
        // Two garments at position one would print the same number on two job cards.
        var number = OrdersTestData.Number();

        var refused = Confirm(
            orderNumber: number,
            garments:
            [
                OrdersTestData.Garment(number, 1, OrdersTestData.Id("first")),
                OrdersTestData.Garment(number, 1, OrdersTestData.Id("second")),
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.duplicate-garment-job");
        refused.Error.Target.ShouldBe("jobIndex");
    }

    [Fact]
    public void ADependencyOnAGarmentOutsideTheConfirmationIsRefused()
    {
        // INV-JOB-09 binds garments of one order. A dependency reaching outside it is a promise the
        // delivery queue could never resolve.
        var number = OrdersTestData.Number();

        var dependent = OrdersTestData.Garment(
            number,
            1,
            dependencies:
            [
                new GarmentJobDependencySpecification(
                    OrdersTestData.Id("dependency"),
                    OrdersTestData.Id("a-garment-of-another-order"),
                    JobDependencyKind.FinishBefore,
                    Reason: null),
            ]);

        var refused = Confirm(orderNumber: number, garments: [dependent]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.dependency-not-in-same-order");
    }

    [Fact]
    public void ADependencyOnAGarmentLaterInTheSameConfirmationIsAccepted()
    {
        // The whole set is only known once every garment has been read, which is why dependencies are
        // checked in a second pass rather than as each garment arrives.
        var number = OrdersTestData.Number();

        var confirmed = Confirm(
            orderNumber: number,
            garments:
            [
                OrdersTestData.Garment(
                    number,
                    1,
                    dependencies:
                    [
                        new GarmentJobDependencySpecification(
                            OrdersTestData.Id("dependency"),
                            OrdersTestData.GarmentId(2),
                            JobDependencyKind.FinishBefore,
                            Reason: null),
                    ]),
                OrdersTestData.Garment(number, 2),
            ]);

        confirmed.IsSuccess.ShouldBeTrue();

        var job = confirmed.Value.FindJob(OrdersTestData.GarmentId(1));
        job.ShouldNotBeNull();
        job.PrerequisiteJobIds(JobDependencyKind.FinishBefore)
            .ShouldHaveSingleItem()
            .ShouldBe(OrdersTestData.GarmentId(2));
    }

    /// <summary>
    /// INV-JOB-09 makes <c>finish_before</c> an ordering, so a circle is a set of garments none of which can
    /// ever start: each names the other as an unfinished prerequisite for ever. Confirmation is irreversible
    /// (section 7) and the only remedy afterwards is a cancellation and a new order, which is exactly the
    /// reasoning the one-garment case was already refused on.
    /// </summary>
    [Fact]
    public void TwoGarmentsThatWaitForEachOtherAreRefusedBeforeTheOrderExists()
    {
        var number = OrdersTestData.Number();

        var refused = Confirm(
            orderNumber: number,
            garments:
            [
                Waiting(number, 1, OrdersTestData.GarmentId(2), "dependency-1"),
                Waiting(number, 2, OrdersTestData.GarmentId(1), "dependency-2"),
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.dependency-cycle");
    }

    /// <summary>A circle through a third garment is the same defect and is found the same way.</summary>
    [Fact]
    public void ACircleThatRunsThroughAThirdGarmentIsRefusedToo()
    {
        var number = OrdersTestData.Number();

        var refused = Confirm(
            orderNumber: number,
            garments:
            [
                Waiting(number, 1, OrdersTestData.GarmentId(2), "dependency-1"),
                Waiting(number, 2, OrdersTestData.GarmentId(3), "dependency-2"),
                Waiting(number, 3, OrdersTestData.GarmentId(1), "dependency-3"),
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.dependency-cycle");
    }

    /// <summary>
    /// The symmetric kind is not a circle. "This garment goes with that one" is the same promise read from
    /// either end (INV-JOB-09), so a pair naming each other under <c>deliver_together</c> is the ordinary
    /// case and must not be refused.
    /// </summary>
    [Fact]
    public void TwoGarmentsDeliveredWithEachOtherAreNotACircle()
    {
        var number = OrdersTestData.Number();

        var confirmed = Confirm(
            orderNumber: number,
            garments:
            [
                Waiting(number, 1, OrdersTestData.GarmentId(2), "dependency-1", JobDependencyKind.DeliverTogether),
                Waiting(number, 2, OrdersTestData.GarmentId(1), "dependency-2", JobDependencyKind.DeliverTogether),
            ]);

        confirmed.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Two dependency rows with one primary key. Only the order can see the collision, and left to the
    /// transaction it arrives as a persistence error from inside the confirmation rather than as the field
    /// error the counter can act on (security rule 3).
    /// </summary>
    [Fact]
    public void TwoGarmentsHandedTheSameDependencyIdentityAreRefused()
    {
        var number = OrdersTestData.Number();

        var refused = Confirm(
            orderNumber: number,
            garments:
            [
                Waiting(number, 1, OrdersTestData.GarmentId(3), "dependency-shared"),
                Waiting(number, 2, OrdersTestData.GarmentId(3), "dependency-shared"),
                OrdersTestData.Garment(number, 3),
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.duplicate-garment-job");
        refused.Error.Target.ShouldBe("dependencyId");
    }

    /// <summary>
    /// The index is the garment's position within its order and it is printed on the job card, so a
    /// two-garment order numbered <c>-05</c> and <c>-09</c> prints a set nobody can hand over against.
    /// Distinct was never enough.
    /// </summary>
    [Fact]
    public void GarmentsNumberedWithAGapAreRefused()
    {
        var number = OrdersTestData.Number();

        var refused = Confirm(
            orderNumber: number,
            garments:
            [
                OrdersTestData.Garment(number, 5, garmentJobId: OrdersTestData.GarmentId(5)),
                OrdersTestData.Garment(number, 9, garmentJobId: OrdersTestData.GarmentId(9)),
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.garment-job-indices-not-contiguous");
        refused.Error.Target.ShouldBe("jobIndex");
    }

    /// <summary>
    /// INV-ORD-02: a confirmed order records <strong>exactly one</strong> catalogue version, one price-list
    /// version and one tax configuration version. <c>PriceSnapshot.Create</c> can only promise that each
    /// figure names <em>a</em> version; that the whole order names <em>one</em> is a statement only the
    /// order can make, and the confirmation is where it has to be made because it is irreversible.
    /// </summary>
    [Theory]
    [InlineData("catalogVersionId")]
    [InlineData("priceListVersionId")]
    [InlineData("taxConfigurationVersionId")]
    public void AGarmentPricedUnderADifferentConfigurationVersionIsRefused(string field)
    {
        var number = OrdersTestData.Number();

        var refused = Confirm(
            orderNumber: number,
            garments:
            [
                OrdersTestData.Garment(number, 1),
                GarmentPricedUnder(number, 2, field),
            ]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.configuration-version-not-shared");
        refused.Error.Target.ShouldBe(field);
    }

    /// <summary>
    /// The disagreement inside one garment, which is fully visible to the domain: the choices were
    /// validated against one published catalogue and the money worked out under another, and INV-JOB-01
    /// freezes both copies permanently.
    /// </summary>
    [Fact]
    public void AGarmentWhoseDesignAndPriceNameDifferentCataloguesIsRefused()
    {
        var number = OrdersTestData.Number();

        var refused = Confirm(
            orderNumber: number,
            garments: [GarmentPricedUnder(number, 1, "designCatalogVersionId")]);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.configuration-version-not-shared");
        refused.Error.Target.ShouldBe("catalogVersionId");
    }

    [Fact]
    public void AConfirmationMissingSomethingTheTypeSystemSaidCouldNotBeNullIsADefect()
    {
        // A null here is a caller defect rather than a shop-floor situation, so it throws where a refused
        // transition returns.
        var number = OrdersTestData.Number();
        var garments = new[] { OrdersTestData.Garment(number) };

        Should.Throw<ArgumentNullException>(() => Raw(null, OrdersTestData.Price(), garments));
        Should.Throw<ArgumentNullException>(() => Raw(number, null, garments));
        Should.Throw<ArgumentNullException>(() => Raw(number, OrdersTestData.Price(), null));
    }

    /// <summary>One garment that waits for, or goes with, another garment of the same order.</summary>
    private static GarmentJobSpecification Waiting(
        OrderNumber orderNumber,
        int jobIndex,
        Guid prerequisiteGarmentJobId,
        string dependencyName,
        JobDependencyKind kind = JobDependencyKind.FinishBefore)
        => OrdersTestData.Garment(
            orderNumber,
            jobIndex,
            dependencies:
            [
                new GarmentJobDependencySpecification(
                    OrdersTestData.Id(dependencyName),
                    prerequisiteGarmentJobId,
                    kind,
                    Reason: null),
            ]);

    /// <summary>
    /// One garment whose priced or validated position names a different version from the order's own.
    /// </summary>
    private static GarmentJobSpecification GarmentPricedUnder(
        OrderNumber orderNumber,
        int jobIndex,
        string field)
    {
        var other = OrdersTestData.Id($"a-later-{field}");

        var price = PriceSnapshot.Create(
            field == "catalogVersionId" ? other : OrdersTestData.CatalogVersion,
            field == "priceListVersionId" ? other : OrdersTestData.PriceListVersion,
            field == "taxConfigurationVersionId" ? other : OrdersTestData.TaxConfigurationVersion,
            Money.Rupees(2400m),
            Money.Zero,
            Money.Rupees(2400m),
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Rupees(2400m),
            OrdersTestData.Now).Value;

        var design = DesignSnapshot.Create(
            field == "designCatalogVersionId" ? other : OrdersTestData.CatalogVersion,
            "blouse",
            "Blouse",
            "stitch-new",
            "Stitch a new garment",
            [],
            garmentInstructions: null,
            conditionalNotes: null,
            frozenAt: OrdersTestData.Now).Value;

        return GarmentJobSpecification.Create(
            OrdersTestData.GarmentId(jobIndex),
            OrdersTestData.JobNumber(orderNumber, jobIndex),
            jobIndex,
            "blouse",
            "stitch-new",
            OrdersTestData.WorkflowDefinition,
            OrdersTestData.Measurements(),
            design,
            price,
            OrdersTestData.DueDate,
            referenceMediaIds: null,
            dependencies: null).Value;
    }

    private static Result<Order> Confirm(
        Guid? id = null,
        Guid? organisationId = null,
        Guid? branchId = null,
        Guid? customerId = null,
        OrderNumber? orderNumber = null,
        Guid? orderDraftId = null,
        Guid? estimateId = null,
        string? notes = null,
        IReadOnlyCollection<GarmentJobSpecification>? garments = null,
        Guid? initialRevisionId = null)
    {
        var number = orderNumber ?? OrdersTestData.Number();

        return Order.Confirm(
            id ?? OrdersTestData.Id("order"),
            organisationId ?? OrdersTestData.Organisation,
            branchId ?? OrdersTestData.Branch,
            customerId ?? OrdersTestData.Customer,
            number,
            orderDraftId ?? OrdersTestData.Id("draft"),
            estimateId,
            OrdersTestData.DueDate,
            notes,
            OrdersTestData.Price(),
            garments ?? new[] { OrdersTestData.Garment(number) },
            initialRevisionId ?? OrdersTestData.Id("revision-1"),
            OrdersTestData.Now,
            OrdersTestData.Actor);
    }

    /// <summary>
    /// The factory with the three reference arguments passed straight through, so a test can hand it the
    /// null that <see cref="Confirm"/> would otherwise replace with a default.
    /// </summary>
    private static Result<Order> Raw(
        OrderNumber? orderNumber,
        PriceSnapshot? totals,
        IReadOnlyCollection<GarmentJobSpecification>? garments)
        => Order.Confirm(
            OrdersTestData.Id("order"),
            OrdersTestData.Organisation,
            OrdersTestData.Branch,
            OrdersTestData.Customer,
            orderNumber!,
            OrdersTestData.Id("draft"),
            estimateId: null,
            OrdersTestData.DueDate,
            notes: null,
            totals!,
            garments!,
            OrdersTestData.Id("revision-1"),
            OrdersTestData.Now,
            OrdersTestData.Actor);
}
