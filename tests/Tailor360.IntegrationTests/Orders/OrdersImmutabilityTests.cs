using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// The nine triggers and the check constraints the <c>orders</c> migration installs, asserted where they
/// actually hold (issue #133).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every refusal below is written as raw SQL, and that is the only way to ask the question.</strong> The
/// domain publishes no route to any of these writes — <c>GarmentJob</c> has no setter for a frozen copy,
/// <c>OrderRevision</c> and <c>JobDependency</c> publish no mutator at all, and ready state has exactly one
/// writer — so the trigger exists precisely for a write that reached the table without going through the Domain:
/// a repair script, a console, a future handler that forgot. A test that went through the Domain would prove
/// that the C# refuses what the C# refuses.
/// </para>
/// <para>
/// <strong>The permitted case is asserted as hard as the refused one.</strong> The migration's own comment calls
/// the <c>Confirmed</c> arm of <c>job_snapshots_are_immutable</c> load-bearing and says it must not be simplified
/// to a blanket refusal, because <c>Order.Revise</c> replaces all three frozen copies and INV-ORD-05 permits
/// exactly that while every garment is still confirmed. A blanket trigger would pass every refusal test here and
/// break the one command the module has.
/// </para>
/// <para>
/// Every value is synthetic. See <see cref="OrdersHarness"/>.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class OrdersImmutabilityTests(WebApplicationFixture fixture)
{
    [Fact]
    public async Task RefusesToRewriteAGarmentsFrozenCopiesOnceItHasLeftConfirmed()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-JOB-01. The three copies are what the garment was cut to, and a garment already being cut cannot
        // have them changed under it — the route once production has started is an alteration request (#34).
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("IMM"));
        var garmentJobId = confirmed.Jobs.Single().Id;

        await OrdersHarness.StartProductionAsync(fixture, confirmed.Id, garmentJobId);

        var measurements = await OrdersHarness.RefusedAsync(
            fixture,
            "UPDATE orders.measurement_snapshots SET version_number = 99 WHERE garment_job_id = {0}",
            garmentJobId);

        measurements.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        var design = await OrdersHarness.RefusedAsync(
            fixture,
            "UPDATE orders.design_snapshots SET category_label = 'Rewritten' WHERE garment_job_id = {0}",
            garmentJobId);

        design.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        // The third copy has no table of its own — Entity Framework Core 10 cannot nest a two-column value
        // object inside an owned type — so INV-JOB-01 over it is a trigger comparing the twenty-two price_…
        // columns of garment_jobs, and nothing else on that heavily written row.
        var price = await OrdersHarness.RefusedAsync(
            fixture,
            "UPDATE orders.garment_jobs SET price_grand_total_amount = 1 WHERE id = {0}",
            garmentJobId);

        price.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        // Nothing was written by any of the three.
        var loaded = (await FindAsync(confirmed.Id)).ShouldNotBeNull();
        var job = loaded.Jobs.Single();

        job.Measurements.VersionNumber.ShouldBe(1);
        job.Design.CategoryLabel.ShouldBe("Blouse");
        job.Price.GrandTotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultGrandTotal));
    }

    [Fact]
    public async Task PermitsTheReplacementARevisionMakesWhileEveryGarmentIsStillConfirmed()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-ORD-05 and the Confirmed arm of job_snapshots_are_immutable, which is the half a blanket
        // append-only trigger would have broken: Order.Revise rewrites the measurement copy, the design copy and
        // the price copy of every garment it names, in one transaction, against rows the same triggers guard.
        var branchCode = OrdersHarness.BranchCode("RVS");
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, branchCode, garments: 2);

        var revised = await OrdersHarness.ReviseAsync(
            fixture, confirmed, subtotal: 13_000.00m, chestMillimetres: 900m);

        revised.IsSuccess.ShouldBeTrue(
            "the revision the two immutability triggers are written to permit was refused: "
            + revised.Error.Code);

        var loaded = (await FindAsync(confirmed.Id)).ShouldNotBeNull();

        loaded.RevisionNumber.ShouldBe(2);
        loaded.DueDate.ShouldBe(OrdersHarness.DueDate.AddDays(3));
        loaded.Totals.GrandTotal.ShouldBe(Money.Rupees(RevisedGrandTotal));

        // A revision appends: the position the order was confirmed at stays readable beside the new one.
        loaded.Revisions.Count.ShouldBe(2);
        loaded.Revisions.Select(revision => revision.RevisionNumber).OrderBy(number => number).ShouldBe([1, 2]);

        loaded.Revisions.Single(revision => revision.RevisionNumber == Order.FirstRevisionNumber)
            .Totals.GrandTotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultGrandTotal));

        var appended = loaded.Revisions.Single(revision => revision.RevisionNumber == 2);
        appended.Reason.ShouldBe("The customer asked for a looser fit at the counter.");
        appended.Totals.GrandTotal.ShouldBe(Money.Rupees(RevisedGrandTotal));

        foreach (var job in loaded.Jobs)
        {
            job.Status.ShouldBe(GarmentJobStatus.Confirmed);
            job.Measurements.VersionNumber.ShouldBe(2);
            job.Measurements.Values.Single(value => value.Key == "chest").Millimetres.ShouldBe(900m);
            job.Design.Selections.ShouldHaveSingleItem().OptionCode.ShouldBe("boat");
            job.Price.GrandTotal.ShouldBe(Money.Rupees(RevisedGrandTotal));
            job.DueDate.ShouldBe(OrdersHarness.DueDate.AddDays(3));
        }
    }

    [Fact]
    public async Task RefusesEveryRewriteAndEveryDeletionOfARevisionWhileItsOrderStands()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // A revision appends and never rewrites, which is what makes the order's history a history. The DELETE
        // arm is conditional on the order still being there, because order_revisions is declared
        // ON DELETE CASCADE from orders and an unconditional refusal made that cascade unexecutable.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("ARV"));

        var loaded = (await FindAsync(confirmed.Id)).ShouldNotBeNull();
        var revisionId = loaded.Revisions.Single().Id;

        var rewrite = await OrdersHarness.RefusedAsync(
            fixture,
            "UPDATE orders.order_revisions SET reason = 'Rewritten by hand' WHERE id = {0}",
            revisionId);

        rewrite.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        var removal = await OrdersHarness.RefusedAsync(
            fixture, "DELETE FROM orders.order_revisions WHERE id = {0}", revisionId);

        removal.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        (await OrdersHarness.CountAsync(fixture, "order_revisions", "id", revisionId)).ShouldBe(1);
    }

    [Fact]
    public async Task RefusesEveryRewriteAndEveryDeletionOfADependencyWhileItsGarmentStands()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-JOB-09 calls a declared dependency a promise the shop floor has been given. GarmentJob publishes
        // no withdraw and JobDependency no mutator; this is the database saying the same thing.
        var confirmed = await OrdersHarness.ConfirmAsync(
            fixture, OrdersHarness.BranchCode("ADP"), garments: 2, bindSecondGarmentToFirst: true);

        var loaded = (await FindAsync(confirmed.Id)).ShouldNotBeNull();
        var dependencyId = loaded.Jobs.Single(job => job.JobIndex == 2).Dependencies.Single().Id;

        var rewrite = await OrdersHarness.RefusedAsync(
            fixture,
            "UPDATE orders.job_dependencies SET reason = 'Rewritten by hand' WHERE id = {0}",
            dependencyId);

        rewrite.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        var removal = await OrdersHarness.RefusedAsync(
            fixture, "DELETE FROM orders.job_dependencies WHERE id = {0}", dependencyId);

        removal.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        (await OrdersHarness.CountAsync(fixture, "job_dependencies", "id", dependencyId)).ShouldBe(1);
    }

    [Fact]
    public async Task RefusesAGarmentJobThatArrivesAlreadyReadyForDelivery()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-JOB-07: no route makes a garment ready except the gate. A job is confirmed not ready and only ever
        // becomes ready by being moved there, so an insert arriving already ready would skip the evaluation
        // entirely — the one arm of the old job_ready_state trigger that crossed over to garment_jobs.
        var branchCode = OrdersHarness.BranchCode("GAT");
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, branchCode);
        var garmentJobId = confirmed.Jobs.Single().Id;

        // A copy of a legitimate row with three fields overridden, rather than sixty columns written out: the
        // row type is the table, so jsonb_populate_record round-trips every column the scaffolder declared and
        // a column added later needs no edit here. The identity, the display number and the position are all
        // changed as well, so the only thing this insert can fail on is the trigger under test — a duplicate
        // would otherwise be reported by ux_garment_jobs_organisation_number and look like the same pass.
        var refused = await OrdersHarness.RefusedAsync(
            fixture,
            """
            INSERT INTO orders.garment_jobs
            SELECT (jsonb_populate_record(
                        NULL::orders.garment_jobs,
                        to_jsonb(existing) || jsonb_build_object(
                            'id', {0}::text,
                            'job_number', {1}::text,
                            'job_index', {2}::int,
                            'is_ready_for_delivery', true))).*
            FROM orders.garment_jobs AS existing
            WHERE existing.id = {3}
            """,
            Guid.CreateVersion7(),
            OrdersHarness.JobNumber(branchCode, jobIndex: 9).Value,
            9,
            garmentJobId);

        refused.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        refused.MessageText.ShouldContain("ready gate");

        (await OrdersHarness.CountAsync(fixture, "garment_jobs", "order_id", confirmed.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task RefusesAReadyGarmentThatStillCarriesABlockingReason()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // ReadyGateOutcome.Of defines "ready" as exactly "no blocks", so a ready row carrying a reason is a
        // contradiction the delivery queue would render as a garment that is both collectable and not.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("BLK"));
        var garmentJobId = confirmed.Jobs.Single().Id;

        // The document travels as a parameter rather than as a literal, and not only for tidiness:
        // ExecuteSqlRaw substitutes its {0} placeholders through string.Format, so a brace inside the
        // statement would have to be doubled and a reader would be looking at JSON that is not quite JSON.
        var refused = await OrdersHarness.RefusedAsync(
            fixture,
            """
            UPDATE orders.garment_jobs
               SET is_ready_for_delivery = true,
                   ready_state_computed_at = confirmed_at,
                   ready_state_blocks = {1}::jsonb
             WHERE id = {0}
            """,
            garmentJobId,
            """[{"predicate":"QcPassed","reference":"QC-01"}]""");

        refused.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        refused.ConstraintName.ShouldBe("ck_garment_jobs_ready_has_no_blocks");
    }

    [Fact]
    public async Task RefusesAReadyGarmentThatRecordsNoEvaluation()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The companion check. CI-03 makes a caller judge staleness against ready_state_computed_at rather than
        // against its own read's instant, so a ready row with no instant is a row no caller can reason about.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("EVL"));
        var garmentJobId = confirmed.Jobs.Single().Id;

        var refused = await OrdersHarness.RefusedAsync(
            fixture,
            """
            UPDATE orders.garment_jobs
               SET is_ready_for_delivery = true,
                   ready_state_computed_at = NULL,
                   ready_state_blocks = '[]'::jsonb
             WHERE id = {0}
            """,
            garmentJobId);

        refused.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        refused.ConstraintName.ShouldBe("ck_garment_jobs_ready_has_been_computed");
    }

    [Fact]
    public async Task RefusesADeliveredGarmentWithNoHandoverDateAndAcceptsAHandoverDateInAnotherStatus()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // One direction only, and the schema means it. A delivered garment has a handover date, and nothing can
        // make that false. The converse — a date implies the status — is deliberately absent, because issue
        // #34's accepted post-delivery alteration moves a delivered garment back into production without
        // untelling the customer that they received it, and a check of that shape would have to be contracted
        // out over two releases.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("DLV"), garments: 2);
        var first = confirmed.Jobs.Single(job => job.JobIndex == 1).Id;
        var second = confirmed.Jobs.Single(job => job.JobIndex == 2).Id;

        var refused = await OrdersHarness.RefusedAsync(
            fixture, "UPDATE orders.garment_jobs SET status = 'Delivered' WHERE id = {0}", first);

        refused.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        refused.ConstraintName.ShouldBe("ck_garment_jobs_delivered_is_consistent");

        (await OrdersHarness.ExecuteAsync(
                fixture,
                "UPDATE orders.garment_jobs SET delivered_at = confirmed_at WHERE id = {0}",
                second))
            .ShouldBe(1, "a handover date outside the Delivered status is a state the schema permits");
    }

    [Theory]
    [InlineData("orders", "order_number")]
    [InlineData("estimates", "estimate_number")]
    [InlineData("garment_jobs", "job_number")]
    public async Task RefusesToRewriteADisplayNumberOnAnyOfTheThreeNumberedTables(string table, string column)
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-ORD-03: a display number is never reused, and never rewritten either. The domain publishes no
        // mutator for any of the three, so this is the half that holds when somebody reaches the table — and it
        // is three functions rather than one taking the column name, because a generic form read its column
        // through to_jsonb(NEW) and serialised a sixty-column row on every update of garment_jobs.
        var branchCode = OrdersHarness.BranchCode("NUM");
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, branchCode);
        var estimate = await OrdersHarness.IssueAsync(fixture, branchCode);

        var (id, replacement) = table switch
        {
            "orders" => (confirmed.Id, OrdersHarness.Number(branchCode, sequence: 999).Value),
            "estimates" => (estimate.Id, OrdersHarness.QuoteNumber(branchCode, sequence: 999).Value),
            _ => (
                confirmed.Jobs.Single().Id,
                OrdersHarness.JobNumber(branchCode, jobIndex: 1, sequence: 999).Value),
        };

        // The table and the column are constants chosen by the theory's own inline data, and the values travel
        // as parameters; nothing here is composed from anything a caller supplied.
        var refused = await OrdersHarness.RefusedAsync(
            fixture,
            $"UPDATE orders.{table} SET {column} = {{0}} WHERE id = {{1}}",
            replacement,
            id);

        refused.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        refused.MessageText.ShouldContain("INV-ORD-03");
    }

    /// <summary>
    /// What <c>OrdersHarness.Price</c> asks for at a subtotal of thirteen thousand, to the last paisa.
    /// </summary>
    /// <remarks>
    /// Written out rather than recomputed for the reason <c>OrdersHarness.DefaultGrandTotal</c> gives: an
    /// expectation built from the helper the fixture used would agree with itself after a revision that stored
    /// nothing at all.
    /// </remarks>
    private const decimal RevisedGrandTotal = 14_750.2010m;

    private async Task<Order?> FindAsync(Guid orderId)
    {
        using var scope = fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrderStore>()
            .FindAsync(orderId, OrdersHarness.Organisation, OrdersHarness.Token);
    }
}
