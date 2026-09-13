using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Infrastructure.Payments;

/// <summary>EF Core persistence for <see cref="DispatchException"/>.</summary>
public sealed class DispatchExceptionStore(BillingDbContext context) : IDispatchExceptionStore
{
    /// <inheritdoc />
    public Task<DispatchException?> FindAsync(Guid dispatchExceptionId, Guid organisationId, CancellationToken cancellationToken = default)
        => context.DispatchExceptions.SingleOrDefaultAsync(
            exception => exception.Id == dispatchExceptionId && exception.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public async Task<DispatchException?> FindLiveMatchingAsync(
        Guid orderId, Guid organisationId, IReadOnlyCollection<Guid> jobIds, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var candidates = await context.DispatchExceptions
            .Where(exception =>
                exception.OrganisationId == organisationId
                && exception.OrderId == orderId
                && exception.Status == DispatchExceptionStatus.Approved
                && exception.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        // The set is evaluated in memory: an exact job-set match is not a query EF can translate over
        // an owned collection, and an order carries at most a handful of live exceptions at once.
        return candidates.Find(exception => exception.MatchesJobSet(jobIds));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DispatchException>> ListDueForExpiryAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
        => await context.DispatchExceptions
            .Where(exception => exception.Status == DispatchExceptionStatus.Approved && exception.ExpiresAt <= now)
            .OrderBy(exception => exception.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(DispatchException exception) => context.DispatchExceptions.Add(exception);

    /// <inheritdoc />
    public async Task<Result<TOutcome>> RunExclusiveAsync<TOutcome>(
        Guid dispatchExceptionId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        Func<CancellationToken, Task<bool>> committed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(committed);

        var strategy = context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteInTransactionAsync(
                async token =>
                {
                    context.ChangeTracker.Clear();

                    // FOR NO KEY UPDATE: a second attempt on the same exception waits for this one to
                    // commit, then reads it moved on already, rather than racing it to the same row.
                    await context.Database.ExecuteSqlAsync(
                        $"SELECT id FROM billing.dispatch_exceptions WHERE id = {dispatchExceptionId} FOR NO KEY UPDATE", token);

                    var outcome = await work(token);
                    if (outcome.IsFailure)
                    {
                        throw new AttemptRefusedException(outcome.Error);
                    }

                    return outcome;
                },
                committed,
                cancellationToken);
        }
        catch (AttemptRefusedException refused)
        {
            return Result.Failure<TOutcome>(refused.Error);
        }
    }

    private sealed class AttemptRefusedException(Error error) : Exception(error.Code)
    {
        public Error Error { get; } = error;
    }

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
            // The only writes after approval are a consumption or an expiry, so a row that moved under
            // this one took the other transition, or the same one, first.
            return Result.Failure(BillingErrors.DispatchExceptionAlreadyConsumed);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.RestrictViolation })
        {
            // The one-transition trigger: an exception that does not accept this write refused it.
            return Result.Failure(BillingErrors.DispatchExceptionAlreadyConsumed);
        }
    }
}
