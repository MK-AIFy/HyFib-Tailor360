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
    /// <remarks>
    /// <para>
    /// <strong>The transaction belongs to the execution strategy, because an ambiguous commit must not be
    /// replayed as a second confirmation.</strong> This used to hand <c>strategy.ExecuteAsync</c> a plain
    /// delegate that opened and committed its own transaction, and a retrying strategy replays a delegate
    /// whenever the call fails transiently — including when the connection dropped while PostgreSQL was
    /// acknowledging the <c>COMMIT</c>, which is a commit that succeeded and an answer that was lost. The replay
    /// then found the draft already consumed and answered <c>DraftNotFound</c>: the counter was told the
    /// confirmation had failed while the order, its garment jobs, its frozen snapshots and its outbox rows all
    /// existed. A customer's order taken and then denied, and INV-ORD-01 — "the order, every garment job,
    /// every snapshot, the barcode identity and the outbox message commit together, or nothing does" — read
    /// backwards by the one caller entitled to trust it.
    /// </para>
    /// <para>
    /// <c>ExecuteInTransactionAsync</c> is EF's answer and is what this now uses: it owns the transaction, and
    /// when the failure arrives <em>while the commit is being made</em> it asks
    /// <see cref="ConfirmationCommittedAsync"/> whether the work is in fact there before deciding to retry. A
    /// verified commit is reported as the success it was; an unverified one is replayed from nothing, which is
    /// what <see cref="DetachReadsAndFailedAttempts"/> exists to make safe.
    /// </para>
    /// <para>
    /// <strong>The price is that a refusal leaves this operation by throwing.</strong> EF commits as soon as the
    /// operation returns, so a <c>Result.Failure</c> returned from inside it would commit whatever the callback
    /// had already saved; throwing is the only way to abandon a transaction EF began.
    /// <see cref="ConfirmationRefusedException"/> travels exactly as far as the <c>catch</c> below and the port
    /// answers with the <c>Result</c> it promises — the refusal reaches nobody as an exception.
    /// </para>
    /// </remarks>
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
        // confirmation is about to commit. Added and nothing else — an outbox row that is already Unchanged
        // was written by an earlier save in this scope, is in the database, and inserting it again would be a
        // duplicate key rather than a rescued event. See DetachReadsAndFailedAttempts.
        var published = context.ChangeTracker
            .Entries<OutboxMessage>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToHashSet();

        // The connection retries on a transient failure, and a retry replays the whole operation. An explicit
        // transaction opened outside the strategy would be rejected by EF for exactly that reason.
        var strategy = context.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteInTransactionAsync(
                token => AttemptConfirmationAsync(
                    orderDraftId, estimateId, organisationId, confirm, published, token),
                token => ConfirmationCommittedAsync(orderDraftId, organisationId, token),
                cancellationToken);
        }
        catch (ConfirmationRefusedException refused)
        {
            return Result.Failure<TOutcome>(refused.Error);
        }
    }

    /// <summary>One attempt at a confirmation, inside the transaction the execution strategy opened for it.</summary>
    /// <remarks>
    /// Everything this refuses, it refuses by throwing <see cref="ConfirmationRefusedException"/>, for the
    /// reason <see cref="InConfirmationTransactionAsync"/> records: the transaction is EF's and a returned
    /// failure would be committed. Nothing else about the sequence has changed.
    /// </remarks>
    /// <typeparam name="TOutcome">What the confirmation produces.</typeparam>
    /// <param name="orderDraftId">The draft being confirmed.</param>
    /// <param name="estimateId">The estimate being converted, or null.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="confirm">The decision, given the locked draft and the locked estimate.</param>
    /// <param name="published">The outbox rows this scope had published before the first attempt.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>What the callback produced.</returns>
    /// <exception cref="ConfirmationRefusedException">The confirmation was refused and must roll back.</exception>
    private async Task<Result<TOutcome>> AttemptConfirmationAsync<TOutcome>(
        Guid orderDraftId,
        Guid? estimateId,
        Guid organisationId,
        Func<OrderDraft, Estimate?, CancellationToken, Task<Result<TOutcome>>> confirm,
        HashSet<OutboxMessage> published,
        CancellationToken cancellationToken)
    {
        DetachReadsAndFailedAttempts(published);

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
            throw new ConfirmationRefusedException(OrdersErrors.DraftNotFound);
        }

        Estimate? estimate = null;

        if (estimateId is { } namedEstimateId)
        {
            estimate = await context.Estimates.FirstOrDefaultAsync(
                one => one.Id == namedEstimateId && one.OrganisationId == organisationId,
                cancellationToken);

            if (estimate is null)
            {
                throw new ConfirmationRefusedException(OrdersErrors.EstimateNotFound);
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
            throw new ConfirmationRefusedException(OrdersErrors.ConcurrentChange);
        }

        if (outcome.IsFailure)
        {
            throw new ConfirmationRefusedException(outcome.Error);
        }

        return outcome;
    }

    /// <summary>Whether a confirmation whose commit was ambiguous did in fact commit.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Two facts together, because either alone is true of something that is not this
    /// confirmation.</strong> A consumed draft on its own is equally true of a draft an earlier confirmation
    /// consumed, so a replay of a confirmation that had genuinely failed could read it as its own success. An
    /// order on its own cannot be looked for at all, because the attempt that lost its answer never told anybody
    /// which order it minted — the draft is the only identity both sides hold, which is what
    /// <c>OrderSnapshot.OrderDraftId</c> means by "how an idempotent re-confirmation is recognised". Together
    /// they are decisive: <c>OrderDraft.Consume</c> refuses a draft that is not open, so exactly one
    /// confirmation can ever move a draft from open to consumed, and an order standing against that same draft
    /// is the work of the attempt that moved it. Both are read within the organisation, because every other read
    /// on this port is.
    /// </para>
    /// <para>
    /// <strong>It is consulted only after an ambiguous commit.</strong> EF asks it when the failure arrived
    /// while the transaction was being committed <em>and</em> the exception is one the strategy treats as
    /// transient; a <see cref="ConfirmationRefusedException"/> is neither, so a refusal never reaches it. The two
    /// statements run outside any transaction of ours, on a connection EF reopens if the commit took the last
    /// one with it, and neither is served by an index — <c>orders</c> carries none over
    /// <c>order_draft_id</c>. That is deliberate: an index exists for a query, and the only query is this one,
    /// which runs at most once per confirmation and only after a failure that has already happened.
    /// </para>
    /// </remarks>
    /// <param name="orderDraftId">The draft the confirmation consumed.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="cancellationToken">Cancels the verification.</param>
    /// <returns>True when the draft is consumed and an order stands against it.</returns>
    private async Task<bool> ConfirmationCommittedAsync(
        Guid orderDraftId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var consumed = await context.OrderDrafts
            .AsNoTracking()
            .AnyAsync(
                draft => draft.Id == orderDraftId
                    && draft.OrganisationId == organisationId
                    && draft.ConsumedAt != null,
                cancellationToken);

        return consumed
            && await context.Orders
                .AsNoTracking()
                .AnyAsync(
                    order => order.OrderDraftId == orderDraftId && order.OrganisationId == organisationId,
                    cancellationToken);
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
    /// <strong>A retry is the other half.</strong> <c>ExecuteInTransactionAsync</c> replays the whole attempt
    /// after a transient failure has rolled the transaction back, so the rows a failed attempt published are
    /// dropped here as well: they were never committed, and carrying them into the next attempt would publish
    /// each event twice. Keeping the set captured before the first attempt is what tells the two apart.
    /// </para>
    /// <para>
    /// <strong>And keeping a row is not the same as leaving it alone</strong>, which is the half that was
    /// missing. A rolled-back attempt whose <c>SaveChangesAsync</c> had already succeeded leaves every entity it
    /// saved <c>Unchanged</c> — acceptance happens on the save, not on the commit — so a preserved outbox
    /// row would be carried into the retry in a state that inserts nothing. The retry would commit the order
    /// and lose the event announcing it: the same class of defect as the <c>ChangeTracker.Clear()</c> one above,
    /// reached from the other side. The rows this keeps are therefore put back to <c>Added</c>, which is the
    /// state they were captured in.
    /// </para>
    /// </remarks>
    /// <param name="published">The outbox rows that were already on the tracker when the confirmation began.</param>
    private void DetachReadsAndFailedAttempts(HashSet<OutboxMessage> published)
    {
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is OutboxMessage message && published.Contains(message))
            {
                // Put back to Added rather than left where the last attempt left it. SaveChangesAsync accepts
                // every tracked entity the moment it succeeds, which is before the commit is even attempted, so
                // a row that entered this method as Added comes out of a rolled-back attempt as Unchanged —
                // and an Unchanged row is written by no later save. The retry would then commit the order and
                // silently drop the event that announces it, which is the defect ChangeTracker.Clear() caused
                // arriving by the other door. Setting the state of a row that is still Added changes nothing.
                entry.State = EntityState.Added;

                continue;
            }

            entry.State = EntityState.Detached;
        }
    }

    /// <summary>Carries a refusal out of one confirmation attempt so that its transaction is abandoned.</summary>
    /// <remarks>
    /// <strong>No refusal reaches a caller as an exception.</strong> This exists for the few lines between the
    /// throw inside <see cref="AttemptConfirmationAsync"/> and the catch inside
    /// <see cref="InConfirmationTransactionAsync"/>, which is why it is private: there is no other code that
    /// could catch it, and no code outside this file that should have to know the transaction is abandoned this
    /// way. The message is the error's own code — a stable identifier and never the prose a trigger or a
    /// validator wrote about a named person.
    /// </remarks>
    /// <param name="error">The refusal to answer with.</param>
    private sealed class ConfirmationRefusedException(Error error) : Exception(error.Code)
    {
        /// <summary>The refusal this carries.</summary>
        public Error Error { get; } = error;
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
