using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Modules.Orders.Infrastructure.Persistence;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// What a hard delete does to the <c>orders</c> schema, and what it is still refused (issue #133).
/// </summary>
/// <remarks>
/// <para>
/// <strong>No business path deletes any of this.</strong> An order is cancelled and never deleted (G-5,
/// INV-ORD-06), a garment that should not have been created is cancelled, and a revision is a permanent record.
/// The delete path exists for retention and repair, and it is tested because the schema declares it: five
/// <c>ON DELETE CASCADE</c> arms and two <c>ON DELETE RESTRICT</c> arms, crossed by three triggers whose
/// <c>DELETE</c> arms have to agree with them. The first version of those triggers refused unconditionally,
/// which made every cascade in the schema unexecutable and made two of the messages say the opposite of what the
/// trigger did — so the agreement is exactly the thing worth a test.
/// </para>
/// <para>
/// <strong>One of these was an open question rather than a settled expectation.</strong>
/// <c>job_dependencies</c> holds two foreign keys into <c>garment_jobs</c>, one cascading and one guarding the
/// prerequisite, and <c>docs/dev/migrations.md</c> records that a static read could not settle whether a garment
/// that is another garment's prerequisite aborts the cascade. It did; the key is deferred now, and the pair of
/// tests below hold both halves of what the deferral has to mean.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class OrdersCascadeTests(WebApplicationFixture fixture)
{
    [Fact]
    public async Task DeletingAnOrderTakesItsGarmentsRevisionsAndBothFrozenCopiesWithIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The straightforward shape, with no dependency between the garments: orders cascades to garment_jobs
        // and order_revisions, and garment_jobs cascades to both snapshot tables. Each of those child rows is
        // guarded by a trigger whose DELETE arm has to recognise that its parent has already gone from this
        // transaction's view, which is the whole reason this can be got wrong.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("CAS"), garments: 2);
        var garmentJobIds = confirmed.Jobs.Select(job => job.Id).ToList();

        var refusal = await CascadeAsync(confirmed.Id);

        refusal.ShouldBeNull(
            "deleting an order must cascade rather than abort; a refusal here is one of the three DELETE arms "
            + "disagreeing with an ON DELETE CASCADE the same migration declares");

        (await OrdersHarness.CountAsync(fixture, "orders", "id", confirmed.Id)).ShouldBe(0);
        (await OrdersHarness.CountAsync(fixture, "garment_jobs", "order_id", confirmed.Id)).ShouldBe(0);
        (await OrdersHarness.CountAsync(fixture, "order_revisions", "order_id", confirmed.Id)).ShouldBe(0);

        foreach (var garmentJobId in garmentJobIds)
        {
            (await OrdersHarness.CountAsync(fixture, "measurement_snapshots", "garment_job_id", garmentJobId))
                .ShouldBe(0);
            (await OrdersHarness.CountAsync(fixture, "design_snapshots", "garment_job_id", garmentJobId))
                .ShouldBe(0);
        }

        (await FindAsync(confirmed.Id)).ShouldBeNull();
    }

    /// <summary>
    /// <strong>The question the register could not settle from a static read, and the test that settled it.</strong>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>job_dependencies</c> carries <c>garment_job_id</c> with <c>ON DELETE CASCADE</c> and
    /// <c>prerequisite_garment_job_id</c> with a key of its own, and the asymmetry is deliberate: a garment
    /// somebody is waiting for must not be taken from under them. The cascade from <c>orders</c> deletes every
    /// garment of the order in one statement, and PostgreSQL fires each deleted row's referential triggers in
    /// turn — so whether the prerequisite's check runs before or after the dependent garment's own cascade has
    /// removed the row decides whether the delete completes or aborts. <c>docs/dev/migrations.md</c> says in as
    /// many words that a review could not settle it without a database.
    /// </para>
    /// <para>
    /// <strong>It did abort, and the schema changed rather than this test.</strong> The first CI run of this file
    /// answered with <c>23503: update or delete on table "garment_jobs" violates foreign key constraint
    /// "fk_job_dependencies_garment_jobs_prerequisite_garment_job_id"</c> — an <c>ON DELETE RESTRICT</c> arm
    /// evaluated against the prerequisite before the dependent garment's cascade had removed the row naming it,
    /// which meant an order carrying any declared dependency could not be hard-deleted at all. The key is now
    /// <c>NO ACTION DEFERRABLE INITIALLY DEFERRED</c>, which moves the check to <c>COMMIT</c>, where the question
    /// is decidable. <see cref="RefusesToDeleteAGarmentThatAnotherSurvivingGarmentIsWaitingFor"/> is the half
    /// that proves the deferral gave nothing away.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes when the question has been asked of a real PostgreSQL.</returns>
    [Fact]
    public async Task DeletingAnOrderWhoseGarmentIsAnotherGarmentsPrerequisiteStillCascades()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var confirmed = await OrdersHarness.ConfirmAsync(
            fixture, OrdersHarness.BranchCode("PRQ"), garments: 2, bindSecondGarmentToFirst: true);

        var prerequisite = confirmed.Jobs.Single(job => job.JobIndex == 1).Id;

        var refusal = await CascadeAsync(confirmed.Id);

        refusal.ShouldBeNull(
            "garment one is garment two's prerequisite, and both go in the same cascade; a refusal here is "
            + "fk_job_dependencies_garment_jobs_prerequisite_garment_job_id being evaluated before the "
            + "dependent garment's own cascade removed the row, which means an order carrying a declared "
            + "dependency cannot be hard-deleted at all — the state this branch was in until that key was "
            + "made DEFERRABLE INITIALLY DEFERRED (docs/dev/migrations.md, InitialOrdersSchema)");

        (await OrdersHarness.CountAsync(fixture, "garment_jobs", "order_id", confirmed.Id)).ShouldBe(0);
        (await OrdersHarness.CountAsync(
            fixture, "job_dependencies", "prerequisite_garment_job_id", prerequisite)).ShouldBe(0);
    }

    [Fact]
    public async Task RefusesToDeleteAFrozenCopyWhileItsGarmentJobStands()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The other side of the same trigger. A garment job's frozen copies go when the job goes and never on
        // their own: a garment job with no measurement copy is a garment nobody can cut, and INV-JOB-01 is what
        // says the copy and the job are one fact.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("ORP"));
        var garmentJobId = confirmed.Jobs.Single().Id;

        var measurements = await OrdersHarness.RefusedAsync(
            fixture, "DELETE FROM orders.measurement_snapshots WHERE garment_job_id = {0}", garmentJobId);

        measurements.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        var design = await OrdersHarness.RefusedAsync(
            fixture, "DELETE FROM orders.design_snapshots WHERE garment_job_id = {0}", garmentJobId);

        design.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        (await OrdersHarness.CountAsync(fixture, "measurement_snapshots", "garment_job_id", garmentJobId))
            .ShouldBe(1);
        (await OrdersHarness.CountAsync(fixture, "design_snapshots", "garment_job_id", garmentJobId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task DeletingOneGarmentJobTakesItsFrozenCopiesAndLeavesTheOrderStanding()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // What "the copies go when the job goes" means, asked directly rather than through the order: the
        // snapshot trigger's DELETE arm tests whether the parent garment is still visible in this transaction,
        // and the whole point of that test is that it is not during a cascade.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("ONE"), garments: 2);
        var going = confirmed.Jobs.Single(job => job.JobIndex == 2).Id;
        var staying = confirmed.Jobs.Single(job => job.JobIndex == 1).Id;

        (await OrdersHarness.ExecuteAsync(fixture, "DELETE FROM orders.garment_jobs WHERE id = {0}", going))
            .ShouldBe(1);

        (await OrdersHarness.CountAsync(fixture, "measurement_snapshots", "garment_job_id", going)).ShouldBe(0);
        (await OrdersHarness.CountAsync(fixture, "design_snapshots", "garment_job_id", going)).ShouldBe(0);

        (await OrdersHarness.CountAsync(fixture, "measurement_snapshots", "garment_job_id", staying)).ShouldBe(1);
        (await OrdersHarness.CountAsync(fixture, "orders", "id", confirmed.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task RefusesToDeleteAGarmentThatAnotherSurvivingGarmentIsWaitingFor()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The guarantee the prerequisite key exists for, and the one the deferral must not have given away.
        // fk_job_dependencies_garment_jobs_prerequisite_garment_job_id is NO ACTION DEFERRABLE INITIALLY
        // DEFERRED so that a whole-order cascade can take both garments together; deferring moves the check to
        // COMMIT rather than removing it, and at COMMIT this case is still a garment removed from under a
        // promise somebody else is waiting on. It is the negative half of the test above it, and without it the
        // deferral would read as "the key was weakened until the cascade stopped complaining".
        var confirmed = await OrdersHarness.ConfirmAsync(
            fixture, OrdersHarness.BranchCode("WAI"), garments: 2, bindSecondGarmentToFirst: true);

        var prerequisite = confirmed.Jobs.Single(job => job.JobIndex == 1).Id;

        var refused = await OrdersHarness.RefusedAsync(
            fixture, "DELETE FROM orders.garment_jobs WHERE id = {0}", prerequisite);

        refused.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        refused.ConstraintName.ShouldBe("fk_job_dependencies_garment_jobs_prerequisite_garment_job_id");

        // Refused at COMMIT means the whole statement's work is rolled back, frozen copies included — so the
        // garment is still there rather than half gone, which is the difference between a deferred check and no
        // check at all.
        (await OrdersHarness.CountAsync(fixture, "garment_jobs", "id", prerequisite)).ShouldBe(1);
        (await OrdersHarness.CountAsync(fixture, "measurement_snapshots", "garment_job_id", prerequisite))
            .ShouldBe(1);
        (await OrdersHarness.CountAsync(
            fixture, "job_dependencies", "prerequisite_garment_job_id", prerequisite)).ShouldBe(1);
    }

    [Fact]
    public async Task RefusesToDeleteAnEstimateAnOrderWasConfirmedFrom()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The second RESTRICT arm. An estimate is a permanent business record and an order files itself against
        // one, so taking the estimate away would leave the order naming a quotation nobody can produce. The
        // refusal is a foreign-key violation rather than a trigger, which OrdersWriteFailures deliberately
        // leaves unmapped: it guards a delete no command performs.
        var branchCode = OrdersHarness.BranchCode("KEP");
        var estimate = await OrdersHarness.IssueAsync(fixture, branchCode);

        await OrdersHarness.ConfirmAsync(fixture, branchCode, estimateId: estimate.Id);

        var refused = await OrdersHarness.RefusedAsync(
            fixture, "DELETE FROM orders.estimates WHERE id = {0}", estimate.Id);

        refused.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        refused.ConstraintName.ShouldBe("fk_orders_estimates_estimate_id");

        (await OrdersHarness.CountAsync(fixture, "estimates", "id", estimate.Id)).ShouldBe(1);
    }

    /// <summary>Hard-deletes one order and reports the refusal, where there was one.</summary>
    /// <remarks>
    /// The refusal is returned rather than thrown so that the assertion can say what a failure <em>means</em>.
    /// A bare <c>PostgresException</c> escaping the test would report the trigger's own prose, which is written
    /// for whoever reached the table by hand and says nothing about which of the schema's seven referential arms
    /// resolved in which order.
    /// </remarks>
    /// <param name="orderId">The order to delete.</param>
    /// <returns>The refusal, or null when the cascade completed.</returns>
    private async Task<PostgresException?> CascadeAsync(Guid orderId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM orders.orders WHERE id = {0}", [orderId], OrdersHarness.Token);

            return null;
        }
        catch (PostgresException refusal)
        {
            return refusal;
        }
    }

    private async Task<Order?> FindAsync(Guid orderId)
    {
        using var scope = fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrderStore>()
            .FindAsync(orderId, OrdersHarness.Organisation, OrdersHarness.Token);
    }
}
