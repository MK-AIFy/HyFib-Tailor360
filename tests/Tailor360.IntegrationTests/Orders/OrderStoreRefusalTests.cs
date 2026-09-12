using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// That a refusal the <c>orders</c> schema raises reaches the caller as the <c>Result</c> the store maps, and
/// never as an unhandled exception (issue #133).
/// </summary>
/// <remarks>
/// <para>
/// <strong>What this adds to the unit tier, which already enumerates the mapping table.</strong>
/// <c>OrdersWriteFailureTests</c> asserts that a <c>PostgresException</c> naming a given constraint becomes a
/// given <c>Error</c> — a pure function, exhaustively covered, and blind to whether PostgreSQL ever raises that
/// exception or names that constraint. These tests close the other half of the loop: a real constraint, in a
/// real schema, raising a real failure through a real <c>SaveChangesAsync</c>. Where a constraint name is
/// asserted it is read from the <see cref="OrdersDbContext"/> constant the mapping reads, so a rename that broke
/// the schema cannot keep passing on both sides.
/// </para>
/// <para>
/// <strong>Why it matters that these are results and not exceptions.</strong> Every one of the trigger bodies is
/// prose addressed to a person and names the garment job it refused; an unhandled <c>PostgresException</c> would
/// carry that text to the wire, which CLAUDE.md section 4 rule 3 forbids. A caller gets
/// <c>orders.write-refused</c> instead: the Domain rule the trigger mirrors has already been broken, so there is
/// nothing the counter can act on, but there is a result rather than a 500.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class OrderStoreRefusalTests(WebApplicationFixture fixture)
{
    [Fact]
    public async Task RefusesASecondConfirmationThatTookTheSameOrderNumber()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-ORD-03, and the race it exists for: NextOrderSequenceAsync allocates outside the confirmation's
        // transaction, so two confirmations can reach Order.Confirm holding the same position. The index is what
        // settles it, and the loser's number is never written — which is what keeps the invariant true whichever
        // of the two arrives first.
        var branchCode = OrdersHarness.BranchCode("DUP");

        await OrdersHarness.ConfirmAsync(fixture, branchCode);

        var second = OrdersHarness.Build(branchCode);
        var refused = await OrdersHarness.StoreAsync(fixture, second);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(OrdersErrors.ConcurrentChange.Code);

        (await OrdersHarness.CountAsync(fixture, "orders", "id", second.Id))
            .ShouldBe(0, "the loser's row is never written, so its number is never taken");
    }

    [Fact]
    public async Task RefusesASecondIssueThatTookTheSameEstimateNumber()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-ORD-04 gives an estimate its own series, so this is a separate index over a separate sequence and
        // is asserted separately: a conversion consumes no order number, and an unconverted estimate leaves no
        // gap in one.
        var branchCode = OrdersHarness.BranchCode("DQE");

        await OrdersHarness.IssueAsync(fixture, branchCode);

        var second = OrdersHarness.BuildEstimate(branchCode);
        var refused = await OrdersHarness.StoreAsync(fixture, second);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(OrdersErrors.ConcurrentChange.Code);

        (await OrdersHarness.CountAsync(fixture, "estimates", "id", second.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task RefusesAGarmentJobThatTookANumberAnotherOrdersGarmentAlreadyHolds()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-ORD-03 over the third numbered table. This one is reached by hand rather than through a second
        // confirmation, and the reason is structural: a garment's number is minted from its order's number and
        // Order.Confirm refuses a garment whose number names a different order, so two confirmations can only
        // collide here by first colliding on ux_orders_organisation_number — which the orders row, inserted
        // first, reports instead. Reaching the table is therefore the only way to ask whether the garment index
        // is organisation-wide, which is what INV-ORD-03 says it is.
        var source = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("JNA"));
        var target = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("JNB"));

        var taken = source.Jobs.Single().JobNumber.Value;

        var refused = await OrdersHarness.RefusedAsync(
            fixture,
            """
            INSERT INTO orders.garment_jobs
            SELECT (jsonb_populate_record(
                        NULL::orders.garment_jobs,
                        to_jsonb(existing) || jsonb_build_object(
                            'id', {0}::text,
                            'job_number', {1}::text,
                            'job_index', 2))).*
            FROM orders.garment_jobs AS existing
            WHERE existing.id = {2}
            """,
            Guid.CreateVersion7(),
            taken,
            target.Jobs.Single().Id);

        refused.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        refused.ConstraintName.ShouldBe(OrdersDbContext.GarmentJobNumberIndex);
    }

    [Fact]
    public async Task RefusesASecondOrderConfirmedFromOneEstimate()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // Estimate.Convert refuses a second conversion; ux_orders_estimate is the half that holds when two
        // confirmations run at once. The refusal has to say what actually happened rather than "somebody else
        // changed this", because a caller told the latter would retry and be refused identically for ever.
        var branchCode = OrdersHarness.BranchCode("TWC");
        var estimate = await OrdersHarness.IssueAsync(fixture, branchCode);

        await OrdersHarness.ConfirmAsync(fixture, branchCode, estimateId: estimate.Id);

        var second = OrdersHarness.Build(
            OrdersHarness.BranchCode("TWD"), estimateId: estimate.Id);

        var refused = await OrdersHarness.StoreAsync(fixture, second);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(OrdersErrors.EstimateAlreadyConverted.Code);
    }

    [Fact]
    public async Task RefusesADependencyThatDeclaresTheSamePrerequisiteAndKindTwice()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The triple is OrdersErrors.DuplicateDependency rather than a second rule beside it, which is why
        // job_dependencies has no surrogate rule of its own. Order.Confirm refuses a duplicate within one
        // confirmation, so the index is what holds against anything that reached the table another way.
        var confirmed = await OrdersHarness.ConfirmAsync(
            fixture, OrdersHarness.BranchCode("DDP"), garments: 2, bindSecondGarmentToFirst: true);

        var dependent = confirmed.Jobs.Single(job => job.JobIndex == 2).Id;
        var prerequisite = confirmed.Jobs.Single(job => job.JobIndex == 1).Id;

        var refused = await OrdersHarness.RefusedAsync(
            fixture,
            """
            INSERT INTO orders.job_dependencies
                (id, garment_job_id, prerequisite_garment_job_id, kind, reason, declared_at, declared_by)
            VALUES ({0}, {1}, {2}, 'FinishBefore', NULL, {3}, NULL)
            """,
            Guid.CreateVersion7(),
            dependent,
            prerequisite,
            OrdersHarness.Now);

        refused.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        refused.ConstraintName.ShouldBe(OrdersDbContext.JobDependencyIndex);
    }

    [Fact]
    public async Task AnswersAStaleOrderWithAConcurrencyConflictRatherThanLosingTheSecondWriter()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // Two people acting on one order at once. Last-writer-wins would silently lose a commitment somebody
        // made, so orders carries xmin and the loser is told — which is a 409 a screen can explain rather than a
        // cancellation that quietly reverted.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("STL"));

        using var firstScope = fixture.Services.CreateScope();
        using var secondScope = fixture.Services.CreateScope();

        var firstStore = firstScope.ServiceProvider.GetRequiredService<IOrderStore>();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<IOrderStore>();

        // Both read while the order still permits what both are about to do.
        var read = (await firstStore.FindAsync(
            confirmed.Id, OrdersHarness.Organisation, OrdersHarness.Token)).ShouldNotBeNull();
        var stale = (await secondStore.FindAsync(
            confirmed.Id, OrdersHarness.Organisation, OrdersHarness.Token)).ShouldNotBeNull();

        Cancel(read, "The customer withdrew the order at the counter.").IsSuccess.ShouldBeTrue();
        (await firstStore.SaveAsync(OrdersHarness.Token)).IsSuccess.ShouldBeTrue();

        Cancel(stale, "The second counter cancelled it as well.").IsSuccess.ShouldBeTrue();

        var refused = await secondStore.SaveAsync(OrdersHarness.Token);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(OrdersErrors.ConcurrentChange.Code);

        // The first writer's reason is the one on the row: the second was refused, not merged.
        var loaded = (await FindAsync(confirmed.Id)).ShouldNotBeNull();
        loaded.CancellationReason.ShouldBe("The customer withdrew the order at the counter.");
    }

    [Fact]
    public async Task AnswersACheckConstraintViolationWithARefusalRatherThanAnException()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The domain refuses a job index below one, so the only way to reach ck_garment_jobs_job_index_is_positive
        // through a store is to write the column past the property that guards it. That is not a contrived
        // route: it is what a future handler using the metadata API, or a mapping that lost a guard, would do —
        // and the answer must be a bounded refusal rather than a 500 carrying PostgreSQL's own text.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("CHK"));

        var refused = await SaveThroughTheChangeTrackerAsync(
            confirmed.Id,
            (context, order) =>
                context.Entry(order.Jobs.Single()).Property(job => job.JobIndex).CurrentValue = 0);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(OrdersErrors.WriteRefused.Code);

        (await FindAsync(confirmed.Id)).ShouldNotBeNull().Jobs.Single().JobIndex.ShouldBe(1);
    }

    [Fact]
    public async Task AnswersATriggerRefusalWithARefusalRatherThanAnException()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-ORD-03 reached through a store rather than through a console. orders_number_is_immutable raises
        // with ERRCODE = 'restrict_violation' and a message naming the invariant, and that message must not
        // reach the wire — OrdersErrors.WriteRefused is deliberately generic for exactly this reason.
        var branchCode = OrdersHarness.BranchCode("TRG");
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, branchCode);

        var refused = await SaveThroughTheChangeTrackerAsync(
            confirmed.Id,
            (context, order) => context.Entry(order).Property(one => one.OrderNumber).CurrentValue =
                OrdersHarness.Number(branchCode, sequence: 999));

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(OrdersErrors.WriteRefused.Code);

        // The refusal carries nothing of the database's own message.
        refused.Error.Message.ShouldNotContain("INV-ORD-03");
        refused.Error.Message.ShouldNotContain("orders.orders");

        (await FindAsync(confirmed.Id)).ShouldNotBeNull()
            .OrderNumber.Value.ShouldBe(OrdersHarness.Number(branchCode).Value);
    }

    /// <summary>Cancels an order with a configured reason code and a reason, which both transitions demand.</summary>
    /// <param name="order">The order.</param>
    /// <param name="reason">Why it is being cancelled.</param>
    /// <returns>What the Domain answered.</returns>
    private static Result Cancel(Order order, string reason)
        => order.Cancel("customer-withdrew", reason, [], OrdersHarness.Now.AddHours(1), OrdersHarness.Actor);

    /// <summary>
    /// Loads an order, edits a mapped column past the property that guards it, and saves through the store.
    /// </summary>
    /// <remarks>
    /// The store and the context are resolved from one scope, so they are one <see cref="OrdersDbContext"/> and
    /// therefore one unit of work — which is the arrangement the whole module is composed under and the reason
    /// <c>OrdersWriteFailures</c> is one table rather than three.
    /// </remarks>
    /// <param name="orderId">The order to load.</param>
    /// <param name="edit">What to write past the Domain.</param>
    /// <returns>What the store answered.</returns>
    private async Task<Result> SaveThroughTheChangeTrackerAsync(
        Guid orderId,
        Action<OrdersDbContext, Order> edit)
    {
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOrderStore>();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var order = (await store.FindAsync(orderId, OrdersHarness.Organisation, OrdersHarness.Token))
            .ShouldNotBeNull();

        edit(context, order);

        return await store.SaveAsync(OrdersHarness.Token);
    }

    private async Task<Order?> FindAsync(Guid orderId)
    {
        using var scope = fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrderStore>()
            .FindAsync(orderId, OrdersHarness.Organisation, OrdersHarness.Token);
    }
}
