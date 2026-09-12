using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Domain.Estimates;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Application.Abstractions;

/// <summary>
/// Reads and writes confirmed orders, their garment jobs and their revisions.
/// </summary>
/// <remarks>
/// <para>
/// The module's internal port. An order is loaded whole — jobs, dependencies, the three frozen snapshots and the
/// revision history — because every command on it is a decision about the set: a revision re-prices all the
/// garments, a cancellation refuses if any garment has gone too far, and the ready gate reads a garment's
/// siblings before it answers about the garment.
/// </para>
/// <para>
/// Reads are scoped by organisation and not by branch, for the reason <see cref="IOrderDraftStore"/> gives.
/// </para>
/// </remarks>
public interface IOrderStore
{
    /// <summary>Loads one order for change, or null when none has that identity within the organisation.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The order, or null.</returns>
    Task<Order?> FindAsync(
        Guid orderId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the order one garment job belongs to, or null when no job has that identity.
    /// </summary>
    /// <remarks>
    /// A scan, a hold and a ready-state evaluation all name a garment and nothing else, and the aggregate root is
    /// the order — so the job's own identity has to reach the order it hangs from before any of them can be
    /// decided. The order arrives whole, which is what the parcel and dependency rules need.
    /// </remarks>
    /// <param name="garmentJobId">The garment job.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The order the job belongs to, or null.</returns>
    Task<Order?> FindByJobAsync(
        Guid garmentJobId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a newly confirmed order to the unit of work.</summary>
    /// <param name="order">The order.</param>
    void Add(Order order);

    /// <summary>The concurrency token an edit must be made against.</summary>
    /// <param name="order">A tracked order.</param>
    /// <returns>The entity tag.</returns>
    EntityTag EntityTagOf(Order order);

    /// <summary>
    /// Allocates the next position in the order sequence for a branch and financial year.
    /// </summary>
    /// <remarks>
    /// The position, not the number — see <see cref="IEstimateStore.NextEstimateSequenceAsync"/> for why, and for
    /// why a gap is acceptable and a reuse is not.
    /// </remarks>
    /// <param name="branchCode">The branch's short code, as Identity holds it.</param>
    /// <param name="financialYear">The financial year, evaluated in the branch timezone.</param>
    /// <param name="cancellationToken">Cancels the allocation.</param>
    /// <returns>The next one-based position within that branch and year.</returns>
    Task<long> NextOrderSequenceAsync(
        string branchCode,
        FinancialYear financialYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits, turning the failures two simultaneous commands can cause into results rather than exceptions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Answers <c>OrdersErrors.ConcurrentChange</c> when the row moved or two writers took the same display
    /// number or revision number, <c>OrdersErrors.DuplicateGarmentJob</c> naming <c>jobNumber</c> or
    /// <c>jobIndex</c>, <c>OrdersErrors.EstimateAlreadyConverted</c> when one estimate reached two orders,
    /// <c>OrdersErrors.DuplicateDependency</c> when a garment declared the same binding twice, and
    /// <c>OrdersErrors.GarmentAlreadyOnDraft</c> when a garment identity reached one draft twice.
    /// </para>
    /// <para>
    /// <strong>It saves the whole unit of work and not only this store's aggregates</strong>, because the three
    /// stores share one context — see <see cref="InConfirmationTransactionAsync"/> — so it answers for every named
    /// constraint of the schema and not only for the tables an order is on. A refusal the module did not name,
    /// including a check constraint or one of the schema's append-only triggers, is
    /// <c>OrdersErrors.WriteRefused</c>: a result rather than an unhandled exception carrying a raw PostgreSQL
    /// message, and deliberately not <c>ConcurrentChange</c>, which would invite a retry that fails identically.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the reason the write was refused.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a confirmation over a draft and, where one is being converted, its estimate — both rows locked, in
    /// one transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why a callback rather than a lock method.</strong> The lock is worth something only for as long as
    /// the transaction that took it stays open, so a port that handed back two locked aggregates and returned
    /// would have ended the transaction before the caller decided anything. Inverting it puts the whole
    /// confirmation inside the lock and leaves no way of writing it outside — which matters here more than
    /// anywhere else in the module, because confirmation is the one command that consumes a draft, converts an
    /// estimate, allocates display numbers, freezes three snapshots per garment and lets Custody allocate a
    /// barcode identity, all of which must be one atomic fact
    /// (<c>docs/architecture/sequences/order-confirmation.md</c>).
    /// </para>
    /// <para>
    /// <strong>What the implementation guarantees.</strong> Nothing read before the lock is reused. Both rows are
    /// locked before either is read, in a fixed order — the draft first, then the estimate — so that two
    /// confirmations racing over the same pair queue instead of deadlocking. Both are then read
    /// <strong>scoped by organisation</strong>, like every other read on these three ports, so nothing about the
    /// tenant boundary rests on the callback: confirming another organisation's draft answers
    /// <c>DraftNotFound</c> rather than depending on <c>Estimate.Convert</c> being called. The aggregates handed
    /// to the callback are read inside the transaction and inside the lock, so they are current. The callback's
    /// own save is part of this transaction, and a failing result rolls the whole thing back.
    /// </para>
    /// <para>
    /// <strong>An event already published into this scope is committed with the confirmation, not discarded by
    /// it.</strong> The reset that drops the reads taken before the lock keeps the module's outbox rows, which is
    /// what <c>docs/platform/outbox.md</c> and <see cref="IOrdersEventPublisher"/> promise of a publisher bound to
    /// the same context (issue #77); only a retried attempt's own rows are dropped, and those were rolled back.
    /// </para>
    /// <para>
    /// Answers <c>OrdersErrors.DraftNotFound</c> or <c>OrdersErrors.EstimateNotFound</c> when a named row is
    /// missing or belongs to another organisation, and <c>OrdersErrors.ConcurrentChange</c> when the save loses
    /// to somebody else.
    /// </para>
    /// </remarks>
    /// <typeparam name="TOutcome">What the confirmation produces.</typeparam>
    /// <param name="orderDraftId">The draft being confirmed.</param>
    /// <param name="estimateId">The estimate being converted, or null when the order is confirmed without one.</param>
    /// <param name="organisationId">The tenant the caller is acting within. Both rows are read within it.</param>
    /// <param name="confirm">
    /// The decision, given the locked draft and the locked estimate — null when none was named. It is
    /// responsible for saving; the transaction commits only when it returns success.
    /// </param>
    /// <param name="cancellationToken">Cancels the confirmation.</param>
    /// <returns>What the callback produced, or the reason the confirmation was refused.</returns>
    Task<Result<TOutcome>> InConfirmationTransactionAsync<TOutcome>(
        Guid orderDraftId,
        Guid? estimateId,
        Guid organisationId,
        Func<OrderDraft, Estimate?, CancellationToken, Task<Result<TOutcome>>> confirm,
        CancellationToken cancellationToken = default);
}
