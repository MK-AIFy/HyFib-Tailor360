using Shouldly;
using Tailor360.Modules.Orders.Contracts.Alterations;
using Tailor360.Modules.Orders.Contracts.Orders;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The shapes <c>IOrderSnapshotQuery</c> and <c>IAlterationRequests</c> hand to another module.
/// </summary>
/// <remarks>
/// <para>
/// Nothing implements these contracts yet — Orders' Infrastructure layer arrives with a later issue — so without
/// these tests the records other modules are written against are never once constructed, and the split that
/// keeps money off the state reads is a claim in a comment rather than a fact about the types. Custody, Billing
/// and Reporting all compile against this surface, and a member added or moved here is a breaking change to
/// three modules at once.
/// </para>
/// <para>
/// The rule being pinned is the one #132 established: the three by-identity reads carry no money at all, so a
/// scan screen and a reconciliation run cannot receive a confidential figure they have no use for
/// (<c>docs/nfr/data-classification.md</c> section 2.1 prefers projecting it away to masking it). Price is a
/// fourth read whose answer says whether it included the totals, so a masked answer is distinguishable from an
/// order that has none — the precedent is <c>CustomerSnapshot.ContactIncluded</c>.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OrderSnapshotContractTests
{
    private static readonly DateTimeOffset Now = OrdersTestData.Now;
    private static readonly DateOnly Due = OrdersTestData.DueDate;

    private static Guid Id(string name) => OrdersTestData.Id(name);

    /* The state reads carry no money ---------------------------------------------------------------- */

    [Fact]
    public void AnOrderSnapshotCarriesNoAmountAnywhere()
    {
        // Asserted over the type rather than an instance, so a Money added to any member fails here even if
        // no fixture happens to populate it.
        typeof(OrderSnapshot).GetProperties()
            .Any(property => property.PropertyType == typeof(Money) || property.PropertyType == typeof(Money?))
            .ShouldBeFalse("An order snapshot is a state read; money travels only on the priced read.");
    }

    [Fact]
    public void AGarmentJobSnapshotCarriesNoAmountAnywhere()
        => typeof(GarmentJobSnapshot).GetProperties()
            .Any(property => property.PropertyType == typeof(Money) || property.PropertyType == typeof(Money?))
            .ShouldBeFalse("A garment job snapshot is what a scan screen reads; it must not carry a price.");

    [Fact]
    public void AnOrderSnapshotSaysWhereTheOrderStandsAndWhatItOwes()
    {
        var order = Order();

        order.OrderNumber.ShouldBe("O-CBE01-2627-000001");
        order.State.ShouldBe(OrderState.InProduction);
        order.Jobs.Count.ShouldBe(2);
        order.CancelledAt.ShouldBeNull();
        order.CancellationReasonCode.ShouldBeNull();
    }

    [Fact]
    public void AGarmentJobSnapshotSaysWhatIsBeingMadeWhereItStandsAndWhyItIsNotReady()
    {
        var job = Job();

        job.GarmentJobNumber.ShouldBe("J-CBE01-2627-000001-01");
        job.State.ShouldBe(GarmentJobState.InProduction);
        job.WorkflowVersionId.ShouldBe(Id("workflow-version"));
        job.IsReadyForDelivery.ShouldBeFalse();
        job.ReadyStateBlocks.Single().Reason.ShouldBe(ReadyBlockReason.WorkflowComplete);
        job.ReadyStateBlocks.Single().Reference.ShouldBe("finishing");
    }

    [Fact]
    public void AGarmentJobSnapshotNamesWhatItMustTravelWith()
    {
        var job = Job();

        job.Dependencies.Single().Relation.ShouldBe(JobDependencyRelation.DeliverTogether);
        job.Dependencies.Single().PrerequisiteGarmentJobId.ShouldBe(Id("job-2"));
    }

    [Fact]
    public void AGarmentJobThatHasNotStartedCarriesNoPinnedVersionAndNoStartInstant()
    {
        var confirmed = Job() with
        {
            State = GarmentJobState.Confirmed,
            WorkflowVersionId = null,
            ProductionStartedAt = null,
            ReadyStateBlocks = [],
            Dependencies = [],
        };

        confirmed.WorkflowVersionId.ShouldBeNull();
        confirmed.ProductionStartedAt.ShouldBeNull();
        confirmed.ReadyStateBlocks.ShouldBeEmpty();
    }

    [Fact]
    public void AHeldGarmentCarriesItsReasonCodeAndWhenItWasHeld()
    {
        var held = Job() with
        {
            State = GarmentJobState.OnHold,
            HoldReasonCode = "EXAMPLE-HOLD-REASON-CODE",
            HeldAt = Now,
        };

        held.HoldReasonCode.ShouldBe("EXAMPLE-HOLD-REASON-CODE");
        held.HeldAt.ShouldBe(Now);
    }

    /* The priced read ------------------------------------------------------------------------------- */

    [Fact]
    public void ThePricedReadSaysWhetherItIncludedTheTotals()
    {
        // TotalsIncluded is what lets a caller tell a masked answer from an order that genuinely has none,
        // which is the whole point of the flag surviving until OD-13 settles a pricing permission.
        var priced = Priced();

        priced.TotalsIncluded.ShouldBeTrue();
        priced.Totals.ShouldNotBeNull();
        priced.JobTotals.Count.ShouldBe(1);
        priced.JobTotals.Single().GarmentJobId.ShouldBe(Id("job-1"));
    }

    [Fact]
    public void AMaskedPricedAnswerCarriesNoTotalsAtAllRatherThanZeroes()
    {
        // The shape OD-13 will produce: a zero total would read as a free order rather than a withheld one.
        var masked = Priced() with { TotalsIncluded = false, Totals = null, JobTotals = [] };

        masked.TotalsIncluded.ShouldBeFalse();
        masked.Totals.ShouldBeNull();
        masked.JobTotals.ShouldBeEmpty();
        masked.Order.OrderNumber.ShouldBe("O-CBE01-2627-000001");
    }

    [Fact]
    public void PricedTotalsNameTheThreeConfigurationVersionsThePriceWasComputedUnder()
    {
        // INV-ORD-02: exactly one catalogue, price-list and tax configuration version. Billing recomputes
        // under these rather than re-deriving from Orders, so all three have to travel.
        var totals = Totals();

        totals.CatalogVersionId.ShouldBe(Id("catalog-version"));
        totals.PriceListVersionId.ShouldBe(Id("price-list-version"));
        totals.TaxConfigurationVersionId.ShouldBe(Id("tax-version"));
        totals.CalculatedAt.ShouldBe(Now);
    }

    [Fact]
    public void PricedTotalsCarryOneTaxSchemeAndTheirGrandTotal()
    {
        var totals = Totals();

        totals.GrandTotal.ShouldBe(Money.Rupees(1180m));
        totals.IntegratedTax.ShouldBe(Money.Zero);
        totals.CentralTax.ShouldBe(Money.Rupees(90m));
        totals.StateTax.ShouldBe(Money.Rupees(90m));
    }

    /* Alterations ----------------------------------------------------------------------------------- */

    [Fact]
    public void AnAlterationRequestHasNoJobUntilItIsDecided()
    {
        // Plan #34 records the request at open and links the new or reopened job at decide, so the identifier
        // is nullable until then rather than the caller being handed an empty GUID to interpret.
        var opened = new AlterationRequestReference(Id("alteration"), null);

        opened.AlterationRequestId.ShouldBe(Id("alteration"));
        opened.GarmentJobId.ShouldBeNull();
    }

    [Fact]
    public void ADecidedAlterationNamesTheJobItProduced()
        => new AlterationRequestReference(Id("alteration"), Id("job-3")).GarmentJobId.ShouldBe(Id("job-3"));

    [Fact]
    public void EveryAlterationSourceTheDocumentsNameIsExpressible()
        => Enum.GetValues<AlterationSource>().Length.ShouldBeGreaterThanOrEqualTo(2);

    /* Fixtures -------------------------------------------------------------------------------------- */

    private static PricedTotals Totals() => new(
        Id("catalog-version"), Id("price-list-version"), Id("tax-version"),
        Money.Rupees(1000m), Money.Zero, Money.Rupees(1000m),
        Money.Rupees(90m), Money.Rupees(90m), Money.Zero, Money.Zero, Money.Zero,
        Money.Rupees(1180m), Now);

    private static GarmentJobSnapshot Job() => new(
        Id("job-1"), Id("order"), Id("organisation"), Id("branch"),
        "J-CBE01-2627-000001-01", 1, "blouse", "stitching", "Blouse", "Stitching",
        Id("workflow-definition"), Id("workflow-version"), Due,
        GarmentJobState.InProduction, Now, Now, null, null,
        null, null, null,
        false, Now,
        [ReadyStateBlock.Of(ReadyBlockReason.WorkflowComplete, "finishing")],
        [new GarmentJobDependency(Id("job-2"), JobDependencyRelation.DeliverTogether)]);

    private static OrderSnapshot Order() => new(
        Id("order"), "O-CBE01-2627-000001", Id("organisation"), Id("branch"), Id("customer"),
        Id("draft"), Id("estimate"), OrderState.InProduction, 1, Due, Now, Now, null, null, null,
        [Job(), Job() with { GarmentJobId = Id("job-2"), GarmentJobNumber = "J-CBE01-2627-000001-02", JobIndex = 2 }]);

    private static PricedOrderSnapshot Priced() => new(
        Order(), true, Totals(), [new GarmentJobPricedTotals(Id("job-1"), Totals())]);
}
