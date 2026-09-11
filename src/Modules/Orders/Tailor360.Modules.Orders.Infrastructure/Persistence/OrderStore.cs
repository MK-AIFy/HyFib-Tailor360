using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Domain.Estimates;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Sequencing;
using Tailor360.Platform.Persistence.Concurrency;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// Confirmed orders over the Orders context: their garment jobs, their revisions, the order sequence, and the one
/// place a transaction spans more than one aggregate.
/// </summary>
/// <param name="context">The module's context.</param>
/// <param name="sequences">The platform's sequence allocator, for the order number.</param>
public sealed class OrderStore(OrdersDbContext context, ISequenceAllocator sequences) : IOrderStore
{
    private const string LockDraftStatement =
        $"SELECT id FROM {OrdersDbContext.SchemaName}.{OrdersDbContext.OrderDraftsTable} "
        + "WHERE id = {0} FOR UPDATE";

    private const string LockEstimateStatement =
        $"SELECT id FROM {OrdersDbContext.SchemaName}.{OrdersDbContext.EstimatesTable} "
        + "WHERE id = {0} FOR UPDATE";

    /// <inheritdoc />
    /// <remarks>
    /// The three frozen snapshots come with each garment job without being asked for, because they are owned by
    /// it and EF includes an owned reference automatically. The two collection levels below them — the jobs, and
    /// each job's declared dependencies — are why this splits: a single query would return the Cartesian product
    /// of the jobs, their dependencies and the revision history, which on a six-garment order with a revision is
    /// already tens of rows carrying a measurement document each. Splitting is safe because the query names one
    /// row by its key, the reasoning <c>CustomerStore.FindAsync</c> records.
    /// </remarks>
    public Task<Order?> FindAsync(
        Guid orderId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => WholeOrder().FirstOrDefaultAsync(
            order => order.Id == orderId && order.OrganisationId == organisationId,
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The job's own row is read for nothing but its order identity, and the aggregate is then loaded the one way
    /// it is ever loaded — the shape <c>MeasurementTemplateStore.FindByVersionAsync</c> uses to reach a template
    /// from one of its versions. Loading the job on its own and walking to its parent would give a second,
    /// differently-shaped path to an order, which is how two readers end up disagreeing about what an order is.
    /// </remarks>
    public async Task<Order?> FindByJobAsync(
        Guid garmentJobId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var orderId = await context.GarmentJobs
            .AsNoTracking()
            .Where(job => job.Id == garmentJobId && job.OrganisationId == organisationId)
            .Select(job => (Guid?)job.OrderId)
            .FirstOrDefaultAsync(cancellationToken);

        return orderId is { } id ? await FindAsync(id, organisationId, cancellationToken) : null;
    }

    /// <inheritdoc />
    public void Add(Order order) => context.Orders.Add(order);

    /// <inheritdoc />
    public EntityTag EntityTagOf(Order order) => context.EntityTagOf(order);

    /// <inheritdoc />
    /// <remarks>
    /// <strong>This allocation is not inside the confirmation's transaction, whatever the allocator's own
    /// remarks say.</strong> <c>SequenceAllocator</c> states that "the allocation runs inside the caller's
    /// transaction and takes a row lock, so a rolled-back invoice returns its number and a statutory series has
    /// no holes" — true of a caller that shares its context, and this module does not: the allocator is
    /// constructed over <c>PlatformDbContext</c> while <see cref="InConfirmationTransactionAsync"/> opens its
    /// transaction on <see cref="OrdersDbContext"/>, so the <c>INSERT … ON CONFLICT</c> autocommits on its own
    /// pooled connection. A retried confirmation burns another number for the same reason. Harmless here and
    /// recorded rather than fixed, because <see cref="IEstimateStore.NextEstimateSequenceAsync"/> already accepts
    /// exactly this — "a gap is acceptable in a display number and a reuse is not"
    /// (<c>docs/architecture/conventions.md</c> section 3.2). A statutory series that cannot tolerate a gap —
    /// Billing's invoice numbers — must not read the platform comment and assume it holds; correcting it there is
    /// a Platform change this pull request does not own.
    /// </remarks>
    public Task<long> NextOrderSequenceAsync(
        string branchCode,
        FinancialYear financialYear,
        CancellationToken cancellationToken = default)
        => sequences.NextAsync(
            OrdersSequences.OrderNumber,
            OrdersSequences.ScopeOf(branchCode, financialYear),
            cancellationToken);

    /// <inheritdoc />
    public async Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // The order moved between the read and the write. A revision, a hold and a ready-state evaluation all
            // write the same row, and last-writer-wins between two of them would lose a commitment somebody made.
            return Result.Failure(OrdersErrors.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (OrdersWriteFailures.TryMap(exception, out var error))
        {
            // Every named constraint in the schema and not only this store's, because the three stores share one
            // context and therefore one flush — OrdersWriteFailures says why the mapping cannot live per store.
            return Result.Failure(error);
        }
    }

    /// <inheritdoc />
    public async Task<Result<TOutcome>> InConfirmationTransactionAsync<TOutcome>(
        Guid orderDraftId,
        Guid? estimateId,
        Guid organisationId,
        Func<OrderDraft, Estimate?, CancellationToken, Task<Result<TOutcome>>> confirm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirm);

        // Captured before the first attempt and before anything below detaches anything: these are the outbox
        // rows the command had already published into this scope, and they belong to the unit of work the
        // confirmation is about to commit. See DetachReadsAndFailedAttempts.
        var published = context.ChangeTracker
            .Entries<OutboxMessage>()
            .Select(entry => entry.Entity)
            .ToHashSet();

        // The connection retries on a transient failure, and a retry replays this whole delegate. An explicit
        // transaction opened outside the strategy would be rejected by EF for exactly that reason.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            DetachReadsAndFailedAttempts(published);

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            // A fixed order, written down so that it cannot drift: the draft first, then the estimate. Two
            // confirmations racing over the same pair queue instead of deadlocking only because both take them
            // this way round. No two rows of the SAME table are locked together here, which is why there is no
            // equivalent of CustomerStore.InPostgresOrder — that is the pattern to copy the day one is.
            await LockDraftAsync(orderDraftId, cancellationToken);

            if (estimateId is { } lockedEstimateId)
            {
                await LockEstimateAsync(lockedEstimateId, cancellationToken);
            }

            // Scoped by organisation, like every other read on the three ports. It used to read by identity
            // alone, on the argument that the command had already evaluated the organisation when it decided the
            // draft was confirmable — an argument the detach above undoes, because a unit of work that starts
            // from nothing has to start the tenancy check from nothing too. Confirmation is also the one command
            // that spans three aggregates, so it is the worst place to leave the boundary resting on a callback
            // remembering to call Estimate.Convert, whose orderDraftId check was the only thing tying the draft
            // and the estimate to one organisation. IOrderDraftStore states the rule this now keeps: "the
            // organisation is the tenant boundary and is never the caller's to assert past."
            var draft = await DraftWithGarmentsAsync(orderDraftId, organisationId, cancellationToken);

            if (draft is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Result.Failure<TOutcome>(OrdersErrors.DraftNotFound);
            }

            Estimate? estimate = null;

            if (estimateId is { } namedEstimateId)
            {
                estimate = await context.Estimates.FirstOrDefaultAsync(
                    one => one.Id == namedEstimateId && one.OrganisationId == organisationId,
                    cancellationToken);

                if (estimate is null)
                {
                    await transaction.RollbackAsync(cancellationToken);

                    return Result.Failure<TOutcome>(OrdersErrors.EstimateNotFound);
                }
            }

            Result<TOutcome> outcome;

            try
            {
                outcome = await confirm(draft, estimate, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The callback saves inside this transaction and its own store call already turns this into a
                // result. Caught here as well because a confirmation writes more than one aggregate, and a raw
                // exception escaping would be a 500 for two counters confirming at once.
                await transaction.RollbackAsync(cancellationToken);

                return Result.Failure<TOutcome>(OrdersErrors.ConcurrentChange);
            }

            if (outcome.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);

                return outcome;
            }

            await transaction.CommitAsync(cancellationToken);

            return outcome;
        });
    }

    /// <summary>
    /// Puts the change tracker back to what the confirmation is entitled to start from: the outbox rows the
    /// command had already published, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Not <c>ChangeTracker.Clear()</c>, which is what this replaces.</strong> The reason for resetting
    /// at all is unchanged and is only about reads: anything read before the lock was read from an unlocked row,
    /// and the change tracker would hand that copy back to the reads below rather than going to the database.
    /// But this context is shared with <c>OrdersEventPublisher</c> and the other two stores, all scoped, and
    /// <c>ModuleEventPublisher.Publish</c> works by adding an <see cref="OutboxMessage"/> to <em>this</em> change
    /// tracker — "the row this adds is tracked by the same change tracker as the aggregate and goes out on the
    /// same SaveChangesAsync" (issue #77, <c>docs/platform/outbox.md</c>).
    /// <c>IOrdersEventPublisher</c> repeats it: an event published into this scope "is committed by the next save
    /// on any of them and discarded if that save rolls back. That is the whole guarantee." Clearing the tracker
    /// discarded such a row <em>without</em> a rollback — the one case neither document contemplates — so the
    /// outbox row was never written, the event never sent, and nothing reported that it had gone.
    /// </para>
    /// <para>
    /// <strong>A retry is the other half.</strong> <c>strategy.ExecuteAsync</c> replays the whole delegate after
    /// a transient failure has rolled the transaction back, so the rows a failed attempt published are dropped
    /// here as well: they were never committed, and carrying them into the next attempt would publish each event
    /// twice. Keeping the set captured before the first attempt is what tells the two apart.
    /// </para>
    /// </remarks>
    /// <param name="published">The outbox rows that were already on the tracker when the confirmation began.</param>
    private void DetachReadsAndFailedAttempts(HashSet<OutboxMessage> published)
    {
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is OutboxMessage message && published.Contains(message))
            {
                continue;
            }

            entry.State = EntityState.Detached;
        }
    }

    /// <summary>The one query that loads a whole order.</summary>
    /// <remarks>
    /// <strong>A known cost, recorded against <c>IOrderStore</c> rather than hidden.</strong> The three frozen
    /// snapshots arrive with every garment job because they are owned references and Entity Framework includes
    /// one whether or not the command reads it — so a hold, a resume, a cancellation, a ready-state evaluation
    /// and a scan each pull the full measurement document for every garment of the order into process memory
    /// although none of them reads a single value. That is Sensitive Personal data
    /// (<c>docs/nfr/data-classification.md</c> section 5.4 names "the measurement snapshot copied onto a garment
    /// job") materialised on paths with no use for it. It is not a leak — nothing projects or publishes it, and
    /// <c>OrderSnapshotQuery</c> reads none of it — but data minimisation would say not to fetch it, and the
    /// mapping gives no way to opt out while <c>Measurements</c> is an owned reference. The alternative is a
    /// narrower port for the commands that do not need the aggregate whole, which is a design change rather than
    /// a query change, and the aggregate-loaded-whole shape is deliberate: every command here is a decision
    /// about the set.
    /// </remarks>
    /// <returns>The query, without its predicate.</returns>
    private IQueryable<Order> WholeOrder()
        => context.Orders
            .Include(order => order.Jobs)
            .ThenInclude(job => job.Dependencies)
            .Include(order => order.Revisions)
            .AsSplitQuery();

    /// <summary>
    /// Loads a draft with its garment sections and their dependencies, inside the confirmation transaction.
    /// </summary>
    /// <remarks>
    /// The same shape <c>OrderDraftStore.FindAsync</c> loads, scope included, repeated rather than delegated
    /// because this store takes the context and the sequence allocator and nothing else — injecting the draft
    /// port here would put a second store into the confirmation's constructor for one read.
    /// </remarks>
    /// <param name="orderDraftId">The draft, already locked.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The draft, or null.</returns>
    private Task<OrderDraft?> DraftWithGarmentsAsync(
        Guid orderDraftId,
        Guid organisationId,
        CancellationToken cancellationToken)
        => context.OrderDrafts
            .Include(draft => draft.Garments)
            .ThenInclude(garment => garment.Dependencies)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                draft => draft.Id == orderDraftId && draft.OrganisationId == organisationId,
                cancellationToken);

    /// <summary>Takes a row lock on one draft for the length of the ambient transaction.</summary>
    /// <remarks>
    /// <para>
    /// A statement rather than a query with <c>FOR UPDATE</c> appended: PostgreSQL refuses <c>FOR UPDATE</c>
    /// inside the sub-query EF composes when a <c>FromSql</c> is combined with <c>Include</c> or
    /// <c>AsSplitQuery</c>, which is how the draft is loaded. Taking the lock first and reading afterwards, in
    /// the same transaction, gets both without either fighting the other — the reasoning
    /// <c>CustomerStore.LockAsync</c> records. A row that does not exist locks nothing and is not an error; the
    /// caller reads null and answers "not found".
    /// </para>
    /// <para>
    /// <strong>The schema and the table come from the model, not from the text.</strong> These two statements
    /// were the one part of the confirmation with no compile-time tie to the mapping, so a table renamed in
    /// <see cref="OrdersDbContext"/> passed the build and the model snapshot and failed at runtime inside a
    /// transaction that had already been opened. Composed from the same constants
    /// <see cref="OrdersDbContext.OrderDraftsTable"/> and <see cref="OrdersDbContext.EstimatesTable"/> that map
    /// them, which is what this file already argues for constraint names: "a constraint name is a contract
    /// between the migration and this method". The identifier stays a parameter; only the two names are
    /// interpolated, and both are <c>const</c>.
    /// </para>
    /// </remarks>
    /// <param name="orderDraftId">The draft to lock.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The rows the statement reports, which nothing reads.</returns>
    private Task<int> LockDraftAsync(Guid orderDraftId, CancellationToken cancellationToken)
        => context.Database.ExecuteSqlRawAsync(LockDraftStatement, [orderDraftId], cancellationToken);

    /// <summary>Takes a row lock on one estimate for the length of the ambient transaction.</summary>
    /// <remarks>See <see cref="LockDraftAsync"/> for why this is a statement rather than a query.</remarks>
    /// <param name="estimateId">The estimate to lock.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The rows the statement reports, which nothing reads.</returns>
    private Task<int> LockEstimateAsync(Guid estimateId, CancellationToken cancellationToken)
        => context.Database.ExecuteSqlRawAsync(LockEstimateStatement, [estimateId], cancellationToken);
}
