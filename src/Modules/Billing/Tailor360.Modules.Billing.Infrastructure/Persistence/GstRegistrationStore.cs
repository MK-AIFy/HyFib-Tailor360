using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>The GST registration store over <see cref="BillingDbContext"/>.</summary>
public sealed class GstRegistrationStore(BillingDbContext context) : IGstRegistrationStore
{
    /// <inheritdoc />
    public async Task<GstRegistration?> FindAsync(
        Guid registrationId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.GstRegistrations
            .SingleOrDefaultAsync(
                registration => registration.Id == registrationId && registration.OrganisationId == organisationId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<GstRegistration>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.GstRegistrations
            .AsNoTracking()
            .Where(registration => registration.OrganisationId == organisationId)
            .OrderBy(registration => registration.BranchId)
            .ThenBy(registration => registration.EffectiveFrom)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<GstRegistration>> ListForBranchAsync(
        Guid branchId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.GstRegistrations
            .AsNoTracking()
            .Where(registration => registration.OrganisationId == organisationId && registration.BranchId == branchId)
            .OrderBy(registration => registration.EffectiveFrom)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(GstRegistration registration) => context.GstRegistrations.Add(registration);

    /// <inheritdoc />
    public EntityTag EntityTagOf(GstRegistration registration) => context.EntityTagOf(registration);

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
                SqlState: PostgresErrorCodes.ExclusionViolation,
                ConstraintName: BillingDbContext.OneRegistrationInForceConstraint,
            })
        {
            // Two administrators recorded overlapping registrations in the same moment; the read in
            // the handler saw neither. The exclusion constraint settled it, and the loser is told the
            // same thing the read would have told them.
            return Result.Failure(BillingErrors.RegistrationOverlaps);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(BillingErrors.RegistrationChanged);
        }
    }
}
