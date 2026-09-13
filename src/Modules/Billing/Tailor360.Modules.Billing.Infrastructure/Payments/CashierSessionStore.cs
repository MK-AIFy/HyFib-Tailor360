using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Billing.Infrastructure.Payments;

/// <summary>EF Core persistence for <see cref="CashierSession"/>.</summary>
public sealed class CashierSessionStore(BillingDbContext context) : ICashierSessionStore
{
    /// <inheritdoc />
    public Task<CashierSession?> FindAsync(Guid sessionId, Guid organisationId, CancellationToken cancellationToken = default)
        => context.CashierSessions.SingleOrDefaultAsync(session => session.Id == sessionId && session.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public Task<CashierSession?> FindOpenAsync(Guid branchId, Guid cashierId, Guid organisationId, CancellationToken cancellationToken = default)
        => context.CashierSessions.SingleOrDefaultAsync(
            session => session.OrganisationId == organisationId
                && session.BranchId == branchId
                && session.CashierId == cashierId
                && session.ClosedAt == null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CashierSession>> ListAsync(Guid organisationId, Guid branchId, CashierSessionStatus? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = context.CashierSessions
            .AsNoTracking()
            .Where(session => session.OrganisationId == organisationId && session.BranchId == branchId);
        if (status is { } wanted)
        {
            query = query.Where(session => session.Status == wanted);
        }

        return await query
            .OrderByDescending(session => session.OpenedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(CashierSession session) => context.CashierSessions.Add(session);

    /// <inheritdoc />
    public Task LockForPaymentAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();

        // FOR SHARE: many payments hold it at once, and the close's own update of the row waits for all
        // of them; a payment arriving during the close waits for the close and then reads it closed.
        return context.Database.ExecuteSqlAsync($"SELECT id FROM billing.cashier_sessions WHERE id = {sessionId} FOR SHARE", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<TOutcome>> CloseInTransactionAsync<TOutcome>(
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

                    // FOR NO KEY UPDATE: conflicts with every payment's share hold, so the totals read after
                    // this are the totals the close commits over, and no payment lands in a closed session.
                    await context.Database.ExecuteSqlAsync($"SELECT id FROM billing.cashier_sessions WHERE id = {sessionId} FOR NO KEY UPDATE", token);

                    var outcome = await work(token);
                    if (outcome.IsFailure)
                    {
                        throw new AttemptRefusedException(outcome.Error);
                    }

                    return outcome;
                },
                // When the commit's outcome is unknown, a session found closed is this close having landed:
                // the row takes exactly one close, so no rival's can be mistaken for it without a 409 first.
                token => context.CashierSessions.AsNoTracking().IgnoreAutoIncludes().AnyAsync(session => session.Id == sessionId && session.ClosedAt != null, token),
                cancellationToken);
        }
        catch (AttemptRefusedException refused)
        {
            return Result.Failure<TOutcome>(refused.Error);
        }
    }

    /// <inheritdoc />
    public EntityTag EntityTagOf(CashierSession session) => context.EntityTagOf(session);

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A cashier session is held inside the transaction that records against it.");
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
            // The only write an open session takes is its close, so a row that moved under this one was
            // closed by the request's twin: the conditional update conventions section 4.4 asks for.
            return Result.Failure(BillingErrors.CashierSessionAlreadyClosed);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: BillingDbContext.OneOpenCashierSessionIndex,
            })
        {
            return Result.Failure(BillingErrors.CashierSessionAlreadyOpen);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.RestrictViolation })
        {
            // The immutability trigger: a closed session refused a change at the database.
            return Result.Failure(BillingErrors.CashierSessionAlreadyClosed);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "pk_cashier_session_counts" or "pk_cashier_session_mode_totals",
            })
        {
            // Two closes at once: the rival's count rows landed first, before this one's row update could
            // be refused by the version token. The same answer as the token gives.
            return Result.Failure(BillingErrors.CashierSessionAlreadyClosed);
        }
    }
}
