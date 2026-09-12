using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Contracts.Orders;
using Tailor360.Modules.Orders.Domain.Estimates;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// That the <c>orders</c> schema accepts what the Orders domain produces, and hands all of it back (issue #133).
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the test the Entity Framework blocker would have failed.</strong> A garment job carries its
/// frozen price copy as a complex property and, while it was also split across a <c>job_ready_state</c> table,
/// every read of one threw <c>InvalidOperationException: Sequence contains more than one element</c> at
/// query-compilation time — so an order could be written and never read back, and
/// <c>OrderStore.FindAsync</c> and <c>IOrderSnapshotQuery.GetPricedAsync</c> were both a 500 rather than a
/// result. Nothing below is reachable without a database, and nothing in the unit or architecture tiers can
/// stand in for it: those assert the model's metadata, and this asserts that PostgreSQL took the rows.
/// </para>
/// <para>
/// The order is confirmed through <c>Order.Confirm</c> and written through the real <c>OrderStore</c>, then read
/// back twice — once through <c>IOrderSnapshotQuery</c>, which is the only way <c>orders.*</c> leaves the module,
/// and once through <c>IOrderStore</c>, which is the aggregate the module's own commands work on. The two reads
/// answer different questions: the contract proves what a consumer receives, and the aggregate proves that the
/// three frozen copies — measurement, design and price — came back through their converters intact.
/// </para>
/// <para>
/// Every value is synthetic. See <see cref="OrdersHarness"/>.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class OrderPersistenceTests(WebApplicationFixture fixture)
{
    [Fact]
    public async Task ConfirmsAnOrderOfTwoGarmentsAndHandsTheWholeGraphBackThroughThePublishedRead()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var branchCode = OrdersHarness.BranchCode("RTP");

        var confirmed = await OrdersHarness.ConfirmAsync(
            fixture, branchCode, garments: 2, bindSecondGarmentToFirst: true);

        var snapshot = (await QueryAsync(query => query.GetAsync(confirmed.Id, OrdersHarness.Token)))
            .ShouldNotBeNull("the order was written and the published read could not find it");

        snapshot.OrderNumber.ShouldBe(OrdersHarness.Number(branchCode).Value);
        snapshot.OrganisationId.ShouldBe(OrdersHarness.Organisation);
        snapshot.BranchId.ShouldBe(OrdersHarness.Branch);
        snapshot.CustomerId.ShouldBe(OrdersHarness.Customer);
        snapshot.OrderDraftId.ShouldBe(confirmed.OrderDraftId);
        snapshot.EstimateId.ShouldBeNull();
        snapshot.State.ShouldBe(OrderState.Confirmed);
        snapshot.RevisionNumber.ShouldBe(Order.FirstRevisionNumber);
        snapshot.DueDate.ShouldBe(OrdersHarness.DueDate);
        snapshot.ConfirmedAt.ShouldBe(OrdersHarness.Now);
        snapshot.ProductionStartedAt.ShouldBeNull();
        snapshot.DeliveredAt.ShouldBeNull();
        snapshot.CancelledAt.ShouldBeNull();
        snapshot.CancellationReasonCode.ShouldBeNull();

        // Job-index order, because that is the order the job cards print in and the order the contract promises.
        snapshot.Jobs.Count.ShouldBe(2);
        snapshot.Jobs.Select(job => job.JobIndex).ShouldBe([1, 2]);

        var first = snapshot.Jobs[0];
        var second = snapshot.Jobs[1];

        first.GarmentJobId.ShouldBe(confirmed.Jobs.First(job => job.JobIndex == 1).Id);
        first.OrderId.ShouldBe(confirmed.Id);
        first.GarmentJobNumber.ShouldBe(OrdersHarness.JobNumber(branchCode, jobIndex: 1).Value);
        first.CategoryKey.ShouldBe(OrdersHarness.CategoryKey);
        first.ServiceTypeKey.ShouldBe(OrdersHarness.ServiceTypeKey);
        first.WorkflowDefinitionId.ShouldBe(OrdersHarness.WorkflowDefinition);
        first.WorkflowVersionId.ShouldBeNull("nothing is pinned until production starts (INV-JOB-02)");
        first.DueDate.ShouldBe(OrdersHarness.DueDate);
        first.State.ShouldBe(GarmentJobState.Confirmed);
        first.ConfirmedAt.ShouldBe(OrdersHarness.Now);
        first.IsReadyForDelivery.ShouldBeFalse("a garment is confirmed not ready (INV-JOB-07)");
        first.ReadyStateComputedAt.ShouldBeNull();
        first.ReadyStateBlocks.ShouldBeEmpty();
        first.Dependencies.ShouldBeEmpty();

        // The two labels are the only things IOrderSnapshotQuery reads out of design_snapshots, which is why
        // they are columns of that table rather than fields inside its selections document.
        first.CategoryLabel.ShouldBe("Blouse");
        first.ServiceTypeLabel.ShouldBe("Stitch a new garment");

        // INV-JOB-09: the promise the shop floor has been given, stored as a job_dependencies row and published
        // on the garment that waits rather than on the one that is waited for.
        var dependency = second.Dependencies.ShouldHaveSingleItem();
        dependency.PrerequisiteGarmentJobId.ShouldBe(first.GarmentJobId);
        dependency.Relation.ShouldBe(JobDependencyRelation.FinishBefore);
    }

    [Fact]
    public async Task HandsBackTheFrozenPriceOfAnOrderAndOfEveryGarmentToTheLastPaisa()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The read Billing performs when it converts an order into an invoice (#42). INV-ORD-02 says the three
        // configuration versions cross the boundary with the amounts, because a total that arrived without them
        // is a figure nobody can recompute.
        var branchCode = OrdersHarness.BranchCode("PAI");
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, branchCode, garments: 2);

        var priced = (await QueryAsync(
                query => query.GetPricedAsync(confirmed.Id, [], OrdersHarness.Token)))
            .ShouldNotBeNull();

        priced.TotalsIncluded.ShouldBeTrue();

        var totals = priced.Totals.ShouldNotBeNull();

        totals.CatalogVersionId.ShouldBe(OrdersHarness.CatalogVersion);
        totals.PriceListVersionId.ShouldBe(OrdersHarness.PriceListVersion);
        totals.TaxConfigurationVersionId.ShouldBe(OrdersHarness.TaxConfigurationVersion);
        totals.CalculatedAt.ShouldBe(OrdersHarness.Now);

        // Every figure to the last paisa, and the tax to the last of the four decimal places the column holds:
        // nine per cent of 10 500.45 is 945.0405, so a column that had quietly become numeric(18,2) — or a
        // converter that rounded on the way out — fails here and nowhere else.
        totals.Subtotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultSubtotal));
        totals.DiscountTotal.ShouldBe(Money.Rupees(499.55m));
        totals.TaxableValue.ShouldBe(Money.Rupees(10_500.45m));
        totals.CentralTax.ShouldBe(Money.Rupees(OrdersHarness.DefaultTaxEachWay));
        totals.StateTax.ShouldBe(Money.Rupees(OrdersHarness.DefaultTaxEachWay));
        totals.IntegratedTax.ShouldBe(Money.Zero);
        totals.Cess.ShouldBe(Money.Zero);

        // The one amount the schema's "no amount is negative" check deliberately excludes.
        totals.RoundOff.ShouldBe(Money.Rupees(-0.33m));
        totals.GrandTotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultGrandTotal));
        totals.GrandTotal.Currency.ShouldBe(Money.IndianRupee);

        // Keyed by garment rather than positional, and in job-index order, which is what an invoice line joins on.
        priced.JobTotals.Count.ShouldBe(2);
        priced.JobTotals.Select(job => job.GarmentJobId)
            .ShouldBe([.. priced.Order.Jobs.Select(job => job.GarmentJobId)]);

        foreach (var job in priced.JobTotals)
        {
            job.Totals.GrandTotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultGrandTotal));
            job.Totals.CentralTax.ShouldBe(Money.Rupees(OrdersHarness.DefaultTaxEachWay));
            job.Totals.CatalogVersionId.ShouldBe(OrdersHarness.CatalogVersion);
        }
    }

    [Fact]
    public async Task HandsBackBothFrozenCopiesOfEveryGarmentThroughTheAggregate()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // measurement_snapshots and design_snapshots hold their collections as jsonb, because the domain types
        // are sealed records whose collection is a constructor parameter and Entity Framework cannot supply a
        // collection navigation as a constructor argument. The converter is therefore the only thing standing
        // between a stored order and a garment nobody can cut — and OrdersJson puts every row back through
        // MeasuredValue.Create and DesignSelection.Create on the way out, which is what this asks about.
        var branchCode = OrdersHarness.BranchCode("FRZ");
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, branchCode, garments: 2);

        var loaded = (await StoreAsync(store => store.FindAsync(
                confirmed.Id, OrdersHarness.Organisation, OrdersHarness.Token)))
            .ShouldNotBeNull();

        loaded.Jobs.Count.ShouldBe(2);

        var job = loaded.Jobs.Single(one => one.JobIndex == 1);

        job.Measurements.MeasurementTemplateId.ShouldBe(OrdersHarness.MeasurementTemplate);
        job.Measurements.TemplateVersionId.ShouldBe(OrdersHarness.MeasurementTemplateVersion);
        job.Measurements.VersionNumber.ShouldBe(1);
        job.Measurements.TakenAt.ShouldBe(OrdersHarness.Now.AddDays(-1));
        job.Measurements.TakenBy.ShouldBe(OrdersHarness.Actor);
        job.Measurements.FrozenAt.ShouldBe(OrdersHarness.Now);

        // Both shapes a measured value takes, because MeasuredValue's "both or neither" rule lost its database
        // half when the collection became a document: a reading with a unit, and a chosen option with neither.
        job.Measurements.Values.Count.ShouldBe(2);

        var chest = job.Measurements.Values.Single(value => value.Key == "chest");
        chest.Millimetres.ShouldBe(860m);
        chest.EnteredUnit.ShouldBe("cm");
        chest.Choice.ShouldBeNull();
        chest.Acknowledged.ShouldBeFalse();

        var fit = job.Measurements.Values.Single(value => value.Key == "fit");
        fit.Millimetres.ShouldBeNull();
        fit.EnteredUnit.ShouldBeNull();
        fit.Choice.ShouldBe("loose");
        fit.Acknowledged.ShouldBeTrue();

        job.Design.CatalogVersionId.ShouldBe(OrdersHarness.CatalogVersion);
        job.Design.CategoryKey.ShouldBe(OrdersHarness.CategoryKey);
        job.Design.CategoryLabel.ShouldBe("Blouse");
        job.Design.ServiceTypeKey.ShouldBe(OrdersHarness.ServiceTypeKey);
        job.Design.ServiceTypeLabel.ShouldBe("Stitch a new garment");
        job.Design.GarmentInstructions.ShouldBe("Synthetic instruction recorded by an integration test.");
        job.Design.ConditionalNotes.ShouldBe(["Lining stitched separately."]);
        job.Design.FrozenAt.ShouldBe(OrdersHarness.Now);

        var selection = job.Design.Selections.ShouldHaveSingleItem();
        selection.GroupCode.ShouldBe("neckline");
        selection.GroupLabel.ShouldBe("Neckline");
        selection.GroupDisplayOrder.ShouldBe(1);
        selection.OptionCode.ShouldBe("round");
        selection.OptionLabel.ShouldBe("Round");
        selection.OptionDisplayOrder.ShouldBe(1);
        selection.OptionVersion.ShouldBe(1);
        selection.PriceListItemCode.ShouldBe("NECK-RND");
        selection.IllustrationMediaId.ShouldBeNull();

        // The two garments were given different necklines, so a converter that read one row and handed the same
        // instance to both would be caught rather than passed.
        loaded.Jobs.Single(one => one.JobIndex == 2)
            .Design.Selections.ShouldHaveSingleItem().OptionCode.ShouldBe("square");

        // A PostgreSQL uuid[] column, which is what keeps the order Reception attached the images in.
        job.ReferenceMediaIds.ShouldHaveSingleItem();

        job.Price.GrandTotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultGrandTotal));
        job.Price.CalculatedAt.ShouldBe(OrdersHarness.Now);
    }

    [Fact]
    public async Task RecordsTheConfirmationAsTheOrdersFirstRevision()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-ORD-01: the order, its garments and the first revision come into being in one construction, so
        // there is no partly built order for a transaction to persist. The revision row is what makes the
        // position the order was confirmed at readable after a later revision has moved it.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("REV"));

        var loaded = (await StoreAsync(store => store.FindAsync(
                confirmed.Id, OrdersHarness.Organisation, OrdersHarness.Token)))
            .ShouldNotBeNull();

        var revision = loaded.Revisions.ShouldHaveSingleItem();

        revision.OrderId.ShouldBe(confirmed.Id);
        revision.RevisionNumber.ShouldBe(Order.FirstRevisionNumber);
        revision.Reason.ShouldBeNull("a confirmation is not recorded against a reason; a revision is");
        revision.DueDate.ShouldBe(OrdersHarness.DueDate);
        revision.SupersededEstimateId.ShouldBeNull();
        revision.RecordedAt.ShouldBe(OrdersHarness.Now);
        revision.RecordedBy.ShouldBe(OrdersHarness.Actor);
        revision.Totals.GrandTotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultGrandTotal));
    }

    [Fact]
    public async Task ReachesAnOrderFromOneOfItsGarmentJobs()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // What every production command starts from: a scan gives a garment, and the decision is about the set.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("BYJ"), garments: 2);
        var garmentJobId = confirmed.Jobs.Single(job => job.JobIndex == 2).Id;

        var loaded = (await StoreAsync(store => store.FindByJobAsync(
                garmentJobId, OrdersHarness.Organisation, OrdersHarness.Token)))
            .ShouldNotBeNull();

        loaded.Id.ShouldBe(confirmed.Id);
        loaded.Jobs.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AnswersNothingForAnOrderOfAnotherOrganisation()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The tenant boundary is never the caller's to assert past (IOrderDraftStore's own words), and both
        // store reads are scoped by organisation for that reason. The negative case is the one worth having:
        // a read that only ever ran against its own tenant would pass with the predicate deleted.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("TEN"));
        var elsewhere = Guid.Parse("0199d000-0000-7000-8000-0000000000bb");

        (await StoreAsync(store => store.FindAsync(confirmed.Id, elsewhere, OrdersHarness.Token)))
            .ShouldBeNull();

        (await StoreAsync(store => store.FindByJobAsync(
                confirmed.Jobs.First().Id, elsewhere, OrdersHarness.Token)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task AnswersNothingForAnOrderThatWasNeverConfirmed()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var missing = Guid.CreateVersion7();

        (await QueryAsync(query => query.GetAsync(missing, OrdersHarness.Token))).ShouldBeNull();
        (await QueryAsync(query => query.GetJobAsync(missing, OrdersHarness.Token))).ShouldBeNull();
        (await QueryAsync(query => query.GetPricedAsync(missing, [], OrdersHarness.Token))).ShouldBeNull();

        // Answered before the database is touched, which is a promise the contract makes rather than an
        // optimisation: a parcel check at the door with nothing in it must not be a round trip.
        (await QueryAsync(query => query.GetJobsAsync([], OrdersHarness.Token))).ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadsASetOfGarmentJobsInJobIndexOrderWhateverOrderItWasAsked()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The parcel read at the door. The contract promises job-index order, and the caller holds a set of
        // scanned identifiers in no particular order.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("SET"), garments: 3);

        var scanned = confirmed.Jobs
            .OrderByDescending(job => job.JobIndex)
            .Select(job => job.Id)
            .ToList();

        var jobs = await QueryAsync(query => query.GetJobsAsync(scanned, OrdersHarness.Token));

        jobs.Select(job => job.JobIndex).ShouldBe([1, 2, 3]);

        var single = (await QueryAsync(query => query.GetJobAsync(scanned[0], OrdersHarness.Token)))
            .ShouldNotBeNull();

        single.JobIndex.ShouldBe(3);
        single.GarmentJobId.ShouldBe(scanned[0]);
    }

    [Fact]
    public async Task StoresAnOrderAgainstTheEstimateItWasConfirmedFrom()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // orders.estimate_id is a real foreign key with ON DELETE RESTRICT, because an estimate is a permanent
        // business record and Order.Confirm can refuse an empty identifier but cannot check that the row is
        // there. Confirming against one that was issued is the half that has to be shown to work: the key is
        // declared across two aggregates of one schema, and an estimate written by EstimateStore and an order
        // written by OrderStore share one context and therefore one flush.
        var branchCode = OrdersHarness.BranchCode("EST");
        var estimate = await OrdersHarness.IssueAsync(fixture, branchCode);

        var confirmed = await OrdersHarness.ConfirmAsync(fixture, branchCode, estimateId: estimate.Id);

        var snapshot = (await QueryAsync(query => query.GetAsync(confirmed.Id, OrdersHarness.Token)))
            .ShouldNotBeNull();

        snapshot.EstimateId.ShouldBe(estimate.Id);

        var stored = await ReadEstimateAsync(estimate.Id);

        stored.EstimateNumber.Value.ShouldBe(OrdersHarness.QuoteNumber(branchCode).Value);
        stored.Totals.GrandTotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultGrandTotal));
        stored.IssuedOn.ShouldBe(OrdersHarness.IssuedOn);
        stored.ValidUntil.ShouldBe(OrdersHarness.IssuedOn.AddDays(14));
        stored.IssuedAt.ShouldBe(OrdersHarness.Now);
    }

    private async Task<TResult> QueryAsync<TResult>(Func<IOrderSnapshotQuery, Task<TResult>> read)
    {
        using var scope = fixture.Services.CreateScope();

        return await read(scope.ServiceProvider.GetRequiredService<IOrderSnapshotQuery>());
    }

    private async Task<TResult> StoreAsync<TResult>(Func<IOrderStore, Task<TResult>> read)
    {
        using var scope = fixture.Services.CreateScope();

        return await read(scope.ServiceProvider.GetRequiredService<IOrderStore>());
    }

    private async Task<Estimate> ReadEstimateAsync(Guid estimateId)
    {
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IEstimateStore>();

        return (await store.FindAsync(estimateId, OrdersHarness.Organisation, OrdersHarness.Token))
            .ShouldNotBeNull();
    }
}
