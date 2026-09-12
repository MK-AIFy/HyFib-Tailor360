using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>The calculation snapshot store over <see cref="BillingDbContext"/>.</summary>
public sealed class CalculationSnapshotStore(BillingDbContext context) : ICalculationSnapshotStore
{
    /// <inheritdoc />
    public async Task<CalculationSnapshot?> FindAsync(Guid organisationId, string reference, CancellationToken cancellationToken = default)
        => await context.CalculationSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                snapshot => snapshot.OrganisationId == organisationId && snapshot.Reference == reference,
                cancellationToken);

    /// <inheritdoc />
    public void Add(CalculationSnapshot snapshot) => context.CalculationSnapshots.Add(snapshot);

    /// <inheritdoc />
    public async Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: BillingDbContext.CalculationReferenceIndex,
            })
        {
            // The loser's entry must leave the context, or the next save would try it again.
            foreach (var entry in context.ChangeTracker.Entries<CalculationSnapshot>().Where(entry => entry.State == EntityState.Added).ToArray())
            {
                entry.State = EntityState.Detached;
            }

            return Result.Failure(BillingErrors.SnapshotExists);
        }
    }
}
