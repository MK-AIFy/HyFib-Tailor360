using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Orders.Contracts.Orders;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Modules.Orders.Infrastructure.Orders;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// That the read Billing raises an invoice from answers from one snapshot of the database, however busy the
/// order is while it runs (issue #133).
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the only tier that can ask the question.</strong> The defect is a property of PostgreSQL's
/// <c>READ COMMITTED</c> isolation level — a fresh snapshot at the start of every statement — so no fake, no
/// fixture and no model test can reproduce it: it needs a real server, a real second connection and a real
/// commit landing between two real statements. Nothing in the unit, architecture or contract tiers stands in for
/// it.
/// </para>
/// <para>
/// <strong>The interleaving is made to happen rather than waited for.</strong> A revision committing at some
/// unpredictable moment would prove nothing, because the moment that matters is the one between the first
/// statement of <c>GetPricedAsync</c> and the rest of them. An interceptor on the context runs the revision
/// exactly there, on its own scope and its own connection, so the race is deterministic and the test either
/// fails every time or passes every time. That is why these tests build their own context instead of resolving
/// <c>IOrderSnapshotQuery</c> from the container — see <c>OrdersHarness.ObservedContext</c>, which starts from
/// the module's own composed options so that <c>EnableRetryOnFailure</c> and the rest are not quietly dropped.
/// </para>
/// <para>
/// <strong>Why the answer matters.</strong> <c>docs/architecture/module-ownership.md</c> section 5.8 has Billing
/// converting an order into an invoice through this one read, and <c>IOrderSnapshotQuery</c> promises that the
/// state arrives with the money "so that eligibility and money are judged from one read of one aggregate rather
/// than from two reads that can disagree". One revision's state beside the next revision's amounts is an invoice
/// raised for a figure the customer was never quoted.
/// </para>
/// <para>
/// Every value is synthetic. See <see cref="OrdersHarness"/>.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class OrderPricedReadSnapshotTests(WebApplicationFixture fixture)
{
    /// <summary>The subtotal the revision re-prices to, in rupees. Far enough from the first to be unmistakable.</summary>
    private const decimal RevisedSubtotal = 13_000.00m;

    /// <summary>
    /// What <see cref="OrdersHarness.Price"/> asks for at <see cref="RevisedSubtotal"/>, to the last paisa.
    /// </summary>
    /// <remarks>
    /// Written out rather than computed from the helper the revision used, for the reason
    /// <c>OrdersHarness.DefaultGrandTotal</c> gives: an expectation built by the same arithmetic as the fixture
    /// agrees with itself whatever either of them does.
    /// </remarks>
    private const decimal RevisedGrandTotal = 14_750.2010m;

    [Fact]
    public async Task AnswersOneRevisionsStateAndThatSameRevisionsMoneyWhenAnotherCommitsMidRead()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var branchCode = OrdersHarness.BranchCode("SNP");
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, branchCode, garments: 2);

        // A revision rewrites orders.totals_*, every garment_jobs.price_*, both due dates and the revision
        // number, and commits all of it at once — so the rows are never inconsistent on disk. What this arranges
        // is a reader that could nevertheless see them so: the whole revision lands after the priced read's
        // first statement and before its last.
        var interleave = new ReviseAfterTheFirstStatement(
            () => OrdersHarness.ReviseAsync(fixture, confirmed, RevisedSubtotal, chestMillimetres: 900m));

        using var scope = fixture.Services.CreateScope();
        await using var context = OrdersHarness.ObservedContext(scope, interleave);

        var priced = (await new OrderSnapshotQuery(context)
                .GetPricedAsync(confirmed.Id, [], OrdersHarness.Token))
            .ShouldNotBeNull();

        var revised = interleave.Revised
            .ShouldNotBeNull("the revision has to run, or this test asserts nothing at all");

        (await revised).IsSuccess.ShouldBeTrue("the revision this test interleaves was itself refused");

        // One snapshot, so every figure below is the confirmation's and none of them is the revision's. Read at
        // READ COMMITTED the state comes from the first statement and the money from the third, and the grand
        // total below is the revised one — an invoice for 14 750.20 against an order quoted at 12 390.20.
        priced.Order.RevisionNumber.ShouldBe(Order.FirstRevisionNumber);
        priced.Order.DueDate.ShouldBe(OrdersHarness.DueDate);
        priced.Order.Jobs.ShouldAllBe(job => job.DueDate == OrdersHarness.DueDate);

        var totals = priced.Totals.ShouldNotBeNull();

        totals.Subtotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultSubtotal));
        totals.GrandTotal.ShouldBe(Money.Rupees(OrdersHarness.DefaultGrandTotal));

        priced.JobTotals.Count.ShouldBe(2);
        priced.JobTotals.ShouldAllBe(
            job => job.Totals.GrandTotal == Money.Rupees(OrdersHarness.DefaultGrandTotal));

        // And the revision really did commit, which is what makes the assertions above an answer rather than an
        // accident: a database that had refused it would have let every one of them pass.
        var after = (await ReadPricedAsync(confirmed.Id)).ShouldNotBeNull();

        after.Order.RevisionNumber.ShouldBe(Order.FirstRevisionNumber + 1);
        after.Totals.ShouldNotBeNull().GrandTotal.ShouldBe(Money.Rupees(RevisedGrandTotal));
        after.JobTotals.ShouldAllBe(job => job.Totals.GrandTotal == Money.Rupees(RevisedGrandTotal));
    }

    [Fact]
    public async Task ReadsTheMoneyInsideOneTransactionAndReadsStateAloneOutsideAnyOfThem()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The mechanism, asserted apart from the behaviour above because the two can fail apart: statements
        // grouped into a transaction opened at the default level would still be a snapshot each, and would still
        // pass a test that only counted transactions. GetAsync is asserted as well, and asserted to be outside
        // one — the dispatch scan and the workload count that read state alone must not begin paying for a
        // transaction only the money read needs.
        //
        // How many statements each read costs is deliberately not asserted. It is a decision of the query
        // compiler, the module states its own position on it in JobsOfAsync's remarks, and a test that pinned it
        // here would fail for a change to Entity Framework rather than for a change to this module.
        var confirmed = await OrdersHarness.ConfirmAsync(fixture, OrdersHarness.BranchCode("TXN"), garments: 2);

        var observer = new RecordEveryStatement();

        using var scope = fixture.Services.CreateScope();
        await using var context = OrdersHarness.ObservedContext(scope, observer);
        var query = new OrderSnapshotQuery(context);

        (await query.GetPricedAsync(confirmed.Id, [], OrdersHarness.Token)).ShouldNotBeNull();

        observer.Statements.ShouldNotBeEmpty();
        observer.Statements.ShouldAllBe(
            statement => statement.InTransaction,
            "a statement outside the transaction reads a snapshot of its own");

        observer.Statements
            .Select(statement => statement.Transaction)
            .Distinct()
            .Count()
            .ShouldBe(1, "one transaction, and therefore one snapshot");

        observer.Reset();

        (await query.GetAsync(confirmed.Id, OrdersHarness.Token)).ShouldNotBeNull();

        observer.Statements.ShouldNotBeEmpty();
        observer.Statements.ShouldAllBe(
            statement => !statement.InTransaction, "the state read is left exactly as it was");
    }

    private async Task<PricedOrderSnapshot?> ReadPricedAsync(Guid orderId)
    {
        using var scope = fixture.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IOrderSnapshotQuery>();

        return await query.GetPricedAsync(orderId, [], OrdersHarness.Token);
    }

    /// <summary>Commits a revision once, after the first statement of the read under test has executed.</summary>
    /// <remarks>
    /// After the first statement and not before it: PostgreSQL fixes a repeatable-read transaction's snapshot at
    /// its first statement rather than at <c>BEGIN</c>, so a revision committed before then would be inside the
    /// snapshot and the test would be asserting nothing. The work runs on a scope and a connection of its own,
    /// which is why it does not queue behind a reader — a reader takes no row lock.
    /// </remarks>
    /// <param name="revise">The revision to commit.</param>
    private sealed class ReviseAfterTheFirstStatement(Func<Task<Result>> revise) : DbCommandInterceptor
    {
        /// <summary>What the revision answered, or null while it has not been started.</summary>
        public Task<Result>? Revised { get; private set; }

        /// <inheritdoc />
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Revised is null)
            {
                // Assigned before it is awaited, so that the statements after this one do not each start one.
                Revised = revise();

                await Revised;
            }

            return result;
        }
    }

    /// <summary>Records, for each statement, whether it ran inside a transaction and which one.</summary>
    /// <remarks>
    /// The transaction is identified by reference rather than by anything it exposes, which is enough for the
    /// only question asked of it: whether the statements shared one or had one each.
    /// </remarks>
    private sealed class RecordEveryStatement : DbCommandInterceptor
    {
        private readonly List<Statement> _statements = [];

        /// <summary>The statements executed since the last <see cref="Reset"/>, in order.</summary>
        public IReadOnlyList<Statement> Statements => _statements;

        /// <summary>Forgets what has been recorded, so that one context can be asked two questions.</summary>
        public void Reset() => _statements.Clear();

        /// <inheritdoc />
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);

            _statements.Add(new Statement(command.Transaction is not null, command.Transaction));

            return ValueTask.FromResult(result);
        }

        /// <summary>One executed statement, as this test needs to see it.</summary>
        /// <param name="InTransaction">Whether it carried a transaction.</param>
        /// <param name="Transaction">The transaction it carried, or null.</param>
        public sealed record Statement(bool InTransaction, DbTransaction? Transaction);
    }
}
