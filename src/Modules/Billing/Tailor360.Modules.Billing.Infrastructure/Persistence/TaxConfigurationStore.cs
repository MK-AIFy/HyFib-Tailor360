using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>The tax configuration store over <see cref="BillingDbContext"/>.</summary>
public sealed class TaxConfigurationStore(BillingDbContext context) : ITaxConfigurationStore
{
    /// <inheritdoc />
    public async Task<TaxConfigurationVersion?> FindAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.TaxConfigurationVersions
            .SingleOrDefaultAsync(
                version => version.Id == versionId && version.OrganisationId == organisationId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<TaxConfigurationVersion?> FindPublishedAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.TaxConfigurationVersions
            .SingleOrDefaultAsync(
                version => version.OrganisationId == organisationId
                           && version.Status == TaxConfigurationStatus.Published,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaxConfigurationVersion>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.TaxConfigurationVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.OrganisationId == organisationId)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<int> NextVersionNumberAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var highest = await context.TaxConfigurationVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.OrganisationId == organisationId)
            .MaxAsync(version => (int?)version.VersionNumber, cancellationToken);

        return (highest ?? 0) + 1;
    }

    /// <inheritdoc />
    public async Task<TaxCodeLedger> ReadCodeHistoryAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
                from code in context.TaxCodes.AsNoTracking().IgnoreAutoIncludes()
                join version in context.TaxConfigurationVersions.IgnoreAutoIncludes()
                    on code.TaxConfigurationVersionId equals version.Id
                where code.OrganisationId == organisationId && version.Status != TaxConfigurationStatus.Draft
                orderby version.VersionNumber
                select new { code.Key, code.Code })
            .ToListAsync(cancellationToken);

        var codeByKey = new Dictionary<Guid, string>();
        var keyByCode = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            codeByKey[row.Key] = row.Code;
            _ = keyByCode.TryAdd(row.Code, row.Key);
        }

        return new TaxCodeLedger(codeByKey, keyByCode);
    }

    /// <inheritdoc />
    public void Add(TaxConfigurationVersion version) => context.TaxConfigurationVersions.Add(version);

    /// <inheritdoc />
    public EntityTag EntityTagOf(TaxConfigurationVersion version) => context.EntityTagOf(version);

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
            // Two writers held the same valid tag; the in-memory comparison passed for both, and
            // the second's xmin no longer matched at the write. Same answer as a failed If-Match,
            // because it is the same situation arriving a moment later.
            return Result.Failure(BillingErrors.VersionChanged);
        }
    }

    /// <inheritdoc />
    public async Task<Result> SaveDraftAsync(CancellationToken cancellationToken = default)
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
                ConstraintName: BillingDbContext.TaxConfigurationNumberIndex,
            })
        {
            return Result.Failure(BillingErrors.DraftNumberConflict);
        }
    }

    /// <inheritdoc />
    public async Task<Result> SavePublicationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (PostgresException exception)
            when (exception is { SqlState: PostgresErrorCodes.ExclusionViolation, ConstraintName: BillingDbContext.OnePublishedTaxConfigurationConstraint })
        {
            // The constraint is deferred, so it is judged at commit and arrives unwrapped: two
            // administrators published different drafts as the first-ever publication in the same
            // moment, and the database let exactly one through.
            return Result.Failure(BillingErrors.PublishConflict);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.ExclusionViolation,
                ConstraintName: BillingDbContext.OnePublishedTaxConfigurationConstraint,
            })
        {
            return Result.Failure(BillingErrors.PublishConflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two administrators published different drafts over the same published version: both
            // retired it, and the loser's retirement found the row already moved. The draft they
            // were looking at did not change — somebody else's is now live — so this is the publish
            // conflict, not a stale tag.
            return Result.Failure(BillingErrors.PublishConflict);
        }
    }
}
