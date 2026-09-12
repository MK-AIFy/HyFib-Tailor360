using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Estimates;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Sequencing;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// Estimates over the Orders context, including the sequence their display numbers are counted from.
/// </summary>
/// <param name="context">The module's context.</param>
/// <param name="sequences">The platform's sequence allocator, for the estimate number.</param>
public sealed class EstimateStore(OrdersDbContext context, ISequenceAllocator sequences) : IEstimateStore
{
    /// <inheritdoc />
    public Task<Estimate?> FindAsync(
        Guid estimateId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => context.Estimates.FirstOrDefaultAsync(
            estimate => estimate.Id == estimateId && estimate.OrganisationId == organisationId,
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Tracked rather than <c>AsNoTracking</c>, because this is the read that precedes a supersede: the standing
    /// estimate found here is the one the new issue marks superseded, and reading it untracked would mean loading
    /// it a second time to write it.
    /// </remarks>
    public async Task<IReadOnlyList<Estimate>> ListForDraftAsync(
        Guid orderDraftId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.Estimates
            .Where(estimate => estimate.OrderDraftId == orderDraftId
                               && estimate.OrganisationId == organisationId)
            .OrderByDescending(estimate => estimate.IssuedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(Estimate estimate) => context.Estimates.Add(estimate);

    /// <inheritdoc />
    public EntityTag EntityTagOf(Estimate estimate) => context.EntityTagOf(estimate);

    /// <inheritdoc />
    public Task<long> NextEstimateSequenceAsync(
        string branchCode,
        FinancialYear financialYear,
        CancellationToken cancellationToken = default)
        => sequences.NextAsync(
            OrdersSequences.EstimateNumber,
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
            // The estimate moved between the read and the write — including the case that matters, a second
            // issue superseding it first. A conflict the counter is told about, never a silently lost supersede.
            return Result.Failure(OrdersErrors.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (OrdersWriteFailures.TryMap(exception, out var error))
        {
            // Every named constraint in the schema and not only this store's. A save issued here flushes the
            // whole unit of work, so it can fail on a garment job's number, an order's estimate or a declared
            // dependency just as easily as on ux_estimates_organisation_number — and while this arm matched that
            // one index alone, those escaped unmapped as a raw PostgresException. OrdersWriteFailures holds the
            // whole table, including the reading of the estimate-number index that used to be written out here:
            // two issues took the same position in the sequence, nothing was created, and INV-ORD-03 holds either
            // way because the loser's number is never written.
            return Result.Failure(error);
        }
    }
}
