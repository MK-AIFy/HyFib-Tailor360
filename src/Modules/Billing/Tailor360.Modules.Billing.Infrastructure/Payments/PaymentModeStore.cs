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

/// <summary>EF Core persistence for <see cref="PaymentMode"/>.</summary>
public sealed class PaymentModeStore(BillingDbContext context) : IPaymentModeStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PaymentMode>> ListAsync(Guid organisationId, CancellationToken cancellationToken = default)
        => await context.PaymentModes
            .Where(mode => mode.OrganisationId == organisationId)
            .OrderBy(mode => mode.Code)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<PaymentMode?> FindAsync(Guid paymentModeId, Guid organisationId, CancellationToken cancellationToken = default)
        => context.PaymentModes.SingleOrDefaultAsync(mode => mode.Id == paymentModeId && mode.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public void Add(PaymentMode mode) => context.PaymentModes.Add(mode);

    /// <inheritdoc />
    public EntityTag EntityTagOf(PaymentMode mode) => context.EntityTagOf(mode);

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
            return Result.Failure(BillingErrors.PaymentModeChanged);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: BillingDbContext.OnePaymentModePerCodeIndex,
            })
        {
            return Result.Failure(BillingErrors.CodeNotUnique("code"));
        }
    }
}
