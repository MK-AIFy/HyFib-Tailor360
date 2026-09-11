using System.Data.Common;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Contracts.Events;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Sequencing;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// That a confirmation whose commit was acknowledged is never taken a second time, and that a confirmation that
/// genuinely has to be retried loses nothing on the way (issue #133).
/// </summary>
/// <remarks>
/// <para>
/// <strong>INV-ORD-01 is what these two protect.</strong> Confirmation is atomic across the whole order — the
/// order, every garment job, every frozen snapshot, the barcode identity and the outbox message commit together
/// or nothing does — and a retrying execution strategy is the one thing that can make that untrue without any
/// single statement failing. The connection can drop while PostgreSQL is acknowledging the commit, and the
/// client then cannot tell "committed" from "did not commit" by itself.
/// </para>
/// <para>
/// <strong>Both failures are injected, because neither can be waited for.</strong> A dropped acknowledgement is
/// a network event, and a test that hoped for one would never run. An interceptor on the transaction produces
/// each of the two shapes exactly: one where the commit reached the server and the answer did not, and one where
/// the commit never happened at all. The exception thrown is an <c>NpgsqlException</c> wrapping an
/// <c>IOException</c> — the shape Npgsql reports a broken connection as, and the one
/// <c>EnableRetryOnFailure</c> exists to retry.
/// </para>
/// <para>
/// <strong>The store is built over an observed context rather than resolved</strong>, for the reason
/// <c>OrdersHarness.ObservedContext</c> gives: an interceptor can only be attached while the options are being
/// built, and the options these start from are the module's own, retry policy included. Everything else — the
/// draft, the order, the event — goes through the Domain and the real stores, as the rest of this tier does.
/// </para>
/// <para>
/// Every value is synthetic. See <see cref="OrdersHarness"/>.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class OrderConfirmationRetryTests(WebApplicationFixture fixture)
{
    [Fact]
    public async Task ReportsTheConfirmationThatCommittedEvenWhenItsAcknowledgementWasLost()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The first of the two shapes: PostgreSQL committed, and the client was told the connection had gone.
        // Replaying the operation here would take the order a second time — or, finding the draft already
        // consumed, tell the counter that a confirmation which is on disk had failed. Either is a customer's
        // order taken and then denied.
        var branchCode = OrdersHarness.BranchCode("ACK");
        var draft = await OrdersHarness.StartAsync(fixture);

        var sabotage = new BreakTheConnection(FailureShape.AfterTheCommit);

        using var scope = fixture.Services.CreateScope();
        await using var context = OrdersHarness.ObservedContext(scope, sabotage);

        var confirmed = await ConfirmAsync(scope, context, draft.Id, branchCode);

        confirmed.IsSuccess.ShouldBeTrue(
            $"the confirmation committed and was reported as: {confirmed.Error.Code}");

        sabotage.Commits.ShouldBe(
            1, "a commit that was verified is not attempted again, whatever the connection said about it");

        (await OrdersHarness.CountAsync(fixture, "orders", "order_draft_id", draft.Id))
            .ShouldBe(1, "one confirmation, one order — INV-ORD-01 either way round");

        (await OrdersHarness.CountAsync(fixture, "garment_jobs", "order_id", confirmed.Value))
            .ShouldBe(1);

        var consumed = (await OrdersHarness.FindDraftAsync(fixture, draft.Id)).ShouldNotBeNull();

        consumed.IsOpen.ShouldBeFalse("the draft the order was made from is spent");
    }

    [Fact]
    public async Task KeepsTheEventPublishedBeforeTheConfirmationWhenTheFirstAttemptIsRolledBack()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The second shape, and the one that is not about the commit at all: the transaction really was rolled
        // back, so the operation has to run again — and it must run again from nothing except the outbox rows
        // the command had already published into this scope. SaveChangesAsync accepts every tracked entity the
        // moment it succeeds, which is before the commit is even attempted, so the first attempt leaves that
        // outbox row Unchanged; carried into the retry in that state it is written by nothing, and the order
        // commits while the event announcing it is silently dropped. docs/platform/outbox.md and
        // IOrdersEventPublisher both promise the opposite: an event published into this scope "is committed by
        // the next save on any of them and discarded if that save rolls back. That is the whole guarantee."
        var branchCode = OrdersHarness.BranchCode("RBK");
        var draft = await OrdersHarness.StartAsync(fixture);

        var sabotage = new BreakTheConnection(FailureShape.InsteadOfTheCommit);

        using var scope = fixture.Services.CreateScope();
        await using var context = OrdersHarness.ObservedContext(scope, sabotage);

        var announced = Announce(scope, context, draft.Id, branchCode);

        var confirmed = await ConfirmAsync(scope, context, draft.Id, branchCode);

        confirmed.IsSuccess.ShouldBeTrue($"the retried confirmation was refused: {confirmed.Error.Code}");

        sabotage.Commits.ShouldBe(2, "one commit refused and one that stood");

        (await OrdersHarness.CountAsync(fixture, "orders", "order_draft_id", draft.Id))
            .ShouldBe(1, "the attempt that was rolled back left nothing behind");

        (await OrdersHarness.CountAsync(fixture, "outbox_messages", "id", announced))
            .ShouldBe(1, "the event the command published commits with the order it announces");
    }

    /// <summary>
    /// Publishes one event into the scope the confirmation is about to commit, through the module's own
    /// publisher.
    /// </summary>
    /// <remarks>
    /// Through <c>OrdersEventPublisher</c> and not by adding a row, because the whole point of the assertion is
    /// what the publisher promises: the message it adds "is tracked by the same change tracker as the aggregate
    /// and goes out on the same SaveChangesAsync" (issue #77). It is constructed over the observed context for
    /// the same reason the store is — the container's publisher is bound to the container's context, which is
    /// not the one under test here.
    /// </remarks>
    /// <param name="scope">The scope the clock and the correlation come from.</param>
    /// <param name="context">The context the confirmation will commit.</param>
    /// <param name="orderDraftId">The draft the event names.</param>
    /// <param name="branchCode">The branch the order number is composed from.</param>
    /// <returns>The identity of the published message, which is also the identity of the event.</returns>
    private static Guid Announce(
        IServiceScope scope,
        OrdersDbContext context,
        Guid orderDraftId,
        string branchCode)
    {
        var publisher = new OrdersEventPublisher(
            context,
            scope.ServiceProvider.GetRequiredService<IClock>(),
            scope.ServiceProvider.GetRequiredService<IOutboxCorrelation>());

        var eventId = Guid.CreateVersion7();

        publisher.Publish(new OrderConfirmed(
            eventId,
            OrdersHarness.Now,
            AggregateId: Guid.CreateVersion7(),
            OrdersHarness.Organisation,
            OrdersHarness.Branch,
            OrdersHarness.Customer,
            OrdersHarness.Number(branchCode).Value,
            orderDraftId,
            EstimateId: null,
            OrdersHarness.DueDate,
            GarmentJobCount: 1,
            RevisionNumber: 1));

        return eventId;
    }

    /// <summary>Runs a confirmation through the real store, the way the confirmation command will.</summary>
    /// <remarks>
    /// The callback consumes the draft and writes the order, and reports what the Domain said rather than
    /// asserting it: a retry re-reads the draft from the database, so an attempt that found it already consumed
    /// would answer <c>orders.draft-already-confirmed</c> — which is exactly the wrong answer these tests are
    /// written to catch, and it has to be able to reach the assertion to be caught there.
    /// </remarks>
    /// <param name="scope">The scope the sequence allocator comes from.</param>
    /// <param name="context">The context under observation.</param>
    /// <param name="orderDraftId">The draft to confirm.</param>
    /// <param name="branchCode">The branch the display numbers are composed from.</param>
    /// <returns>The identity of the order that was confirmed, or the refusal.</returns>
    private static async Task<Result<Guid>> ConfirmAsync(
        IServiceScope scope,
        OrdersDbContext context,
        Guid orderDraftId,
        string branchCode)
    {
        var store = new OrderStore(
            context, scope.ServiceProvider.GetRequiredService<ISequenceAllocator>());

        return await store.InConfirmationTransactionAsync<Guid>(
            orderDraftId,
            estimateId: null,
            OrdersHarness.Organisation,
            async (locked, _, token) =>
            {
                var consumed = locked.Consume(OrdersHarness.Now);

                if (consumed.IsFailure)
                {
                    return Result.Failure<Guid>(consumed.Error);
                }

                // Built inside the callback and not outside it, because an attempt that is replayed builds its
                // order again — the one the rolled-back attempt made was detached with everything else it read.
                var order = OrdersHarness.Build(branchCode, orderDraftId: orderDraftId);

                store.Add(order);

                var saved = await store.SaveAsync(token);

                return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : Result.Success(order.Id);
            },
            OrdersHarness.Token);
    }

    /// <summary>Which of the two commit failures to produce.</summary>
    private enum FailureShape
    {
        /// <summary>The commit reached the server and the acknowledgement did not come back.</summary>
        AfterTheCommit = 0,

        /// <summary>The connection went before the commit was made, so nothing was committed.</summary>
        InsteadOfTheCommit = 1,
    }

    /// <summary>Breaks the connection on the first commit, in whichever of the two ways it was asked for.</summary>
    /// <remarks>
    /// <para>
    /// <c>TransactionCommitted</c> runs after the server has committed, so throwing from it is the ambiguous
    /// case exactly: the work is on disk and the caller has an exception.
    /// <c>TransactionCommitting</c> runs before, so rolling the transaction back there and then throwing is the
    /// other case exactly: the caller has the same exception and the work is not on disk. Nothing else about the
    /// two tests differs, which is what makes the pair the evidence that the verification tells them apart.
    /// </para>
    /// <para>
    /// The exception is the shape Npgsql reports a broken connection as, because
    /// <c>EnableRetryOnFailure</c> retries what Npgsql calls transient and nothing else — a synthetic exception
    /// of some other type would simply propagate, and the test would pass for the wrong reason.
    /// </para>
    /// </remarks>
    /// <param name="shape">Which failure to produce.</param>
    private sealed class BreakTheConnection(FailureShape shape) : DbTransactionInterceptor
    {
        /// <summary>How many commits have been attempted, sabotaged ones included.</summary>
        public int Commits { get; private set; }

        /// <inheritdoc />
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            Commits++;

            if (shape is FailureShape.InsteadOfTheCommit && Commits == 1)
            {
                await transaction.RollbackAsync(cancellationToken);

                throw Dropped();
            }

            return result;
        }

        /// <inheritdoc />
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (shape is FailureShape.AfterTheCommit && Commits == 1)
            {
                throw Dropped();
            }

            return Task.CompletedTask;
        }

        private static NpgsqlException Dropped()
            => new(
                "Exception while writing to stream.",
                new IOException("The connection was closed by an integration test, on purpose."));
    }
}
