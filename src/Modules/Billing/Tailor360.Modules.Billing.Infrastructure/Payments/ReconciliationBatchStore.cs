using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Infrastructure.Payments;

/// <summary>EF Core persistence for <see cref="ReconciliationBatch"/>.</summary>
public sealed class ReconciliationBatchStore(BillingDbContext context) : IReconciliationBatchStore
{
    /// <inheritdoc />
    public Task<ReconciliationBatch?> FindBySessionAsync(Guid sessionId, Guid organisationId, CancellationToken cancellationToken = default)
        => context.ReconciliationBatches.SingleOrDefaultAsync(
            batch => batch.CashierSessionId == sessionId && batch.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public void Add(ReconciliationBatch batch) => context.ReconciliationBatches.Add(batch);

    /// <inheritdoc />
    public async Task<Result<TOutcome>> ApproveInTransactionAsync<TOutcome>(
        Guid sessionId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        var strategy = context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteInTransactionAsync(
                async token =>
                {
                    context.ChangeTracker.Clear();

                    // FOR NO KEY UPDATE: a second approval of the same session's batch waits for this one
                    // to commit, then reads it already approved, rather than racing it to the same row.
                    await context.Database.ExecuteSqlAsync(
                        $"SELECT id FROM billing.reconciliation_batches WHERE cashier_session_id = {sessionId} FOR NO KEY UPDATE", token);

                    var outcome = await work(token);
                    if (outcome.IsFailure)
                    {
                        throw new AttemptRefusedException(outcome.Error);
                    }

                    return outcome;
                },
                // When the commit's outcome is unknown, a batch found approved is this approval having
                // landed: the row takes exactly one approval, so no rival's can be mistaken for it
                // without a conflict first.
                token => context.ReconciliationBatches.AsNoTracking()
                    .AnyAsync(batch => batch.CashierSessionId == sessionId && batch.Status == ReconciliationBatchStatus.Approved, token),
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
            // The only write a batch takes after it is opened is its approval, so a row that moved under
            // this one was approved by the request's twin.
            return Result.Failure(BillingErrors.ReconciliationBatchAlreadyApproved);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.RestrictViolation })
        {
            // The one-transition trigger: a batch that does not accept this write refused it.
            return Result.Failure(BillingErrors.ReconciliationBatchAlreadyApproved);
        }
    }
}
