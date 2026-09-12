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
    public EntityTag EntityTagOf(CashierSession session) => context.EntityTagOf(session);

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
