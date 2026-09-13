using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>The price-list store over <see cref="BillingDbContext"/>.</summary>
public sealed class PriceListStore(BillingDbContext context) : IPriceListStore
{
    /// <inheritdoc />
    public async Task<PriceList?> FindListAsync(Guid priceListId, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.PriceLists
            .SingleOrDefaultAsync(list => list.Id == priceListId && list.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public async Task<PriceList?> FindListByCodeAsync(string code, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.PriceLists
            .AsNoTracking()
            .SingleOrDefaultAsync(list => list.Code == code && list.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceList>> ListListsAsync(Guid organisationId, CancellationToken cancellationToken = default)
        => await context.PriceLists
            .AsNoTracking()
            .Where(list => list.OrganisationId == organisationId)
            .OrderBy(list => list.Code)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void AddList(PriceList priceList) => context.PriceLists.Add(priceList);

    /// <inheritdoc />
    public EntityTag EntityTagOf(PriceList priceList) => context.EntityTagOf(priceList);

    /// <inheritdoc />
    public async Task<PriceListVersion?> FindVersionAsync(Guid versionId, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.PriceListVersions
            .SingleOrDefaultAsync(version => version.Id == versionId && version.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public async Task<PriceListVersion?> FindPublishedAsync(Guid priceListId, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.PriceListVersions
            .SingleOrDefaultAsync(
                version => version.PriceListId == priceListId
                           && version.OrganisationId == organisationId
                           && version.Status == PriceListVersionStatus.Published,
                cancellationToken);

    /// <inheritdoc />
    public async Task<PriceListVersion?> FindPublishedForBranchAsync(Guid branchId, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.PriceListVersions
            .AsNoTracking()
            .Where(version => version.OrganisationId == organisationId
                              && version.Status == PriceListVersionStatus.Published
                              && version.Branches.Any(branch => branch.BranchId == branchId))
            // At most one, by the exclusion constraint; SingleOrDefault would make its violation a five hundred.
            .OrderBy(version => version.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceListVersion>> PublishedVersionsAsync(Guid organisationId, CancellationToken cancellationToken = default)
        => await context.PriceListVersions
            .AsNoTracking()
            .Where(version => version.OrganisationId == organisationId && version.Status == PriceListVersionStatus.Published)
            .OrderBy(version => version.PriceListId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceListVersion>> ListVersionsAsync(Guid priceListId, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.PriceListVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            // The branch rows ride along — a summary that says which branches a version prices is the point
            // of the list — while the items and rules, which can run to hundreds per version, stay out.
            .Include(version => version.Branches)
            .Where(version => version.PriceListId == priceListId && version.OrganisationId == organisationId)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<int> NextVersionNumberAsync(Guid priceListId, CancellationToken cancellationToken = default)
    {
        var highest = await context.PriceListVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.PriceListId == priceListId)
            .MaxAsync(version => (int?)version.VersionNumber, cancellationToken);

        return (highest ?? 0) + 1;
    }

    /// <inheritdoc />
    public async Task<PriceListLedger> ReadCodeHistoryAsync(Guid priceListId, CancellationToken cancellationToken = default)
    {
        var items = await (
                from item in context.PriceListItems.AsNoTracking()
                join version in context.PriceListVersions.IgnoreAutoIncludes() on item.PriceListVersionId equals version.Id
                where version.PriceListId == priceListId && version.Status != PriceListVersionStatus.Draft
                orderby version.VersionNumber
                select new { item.Key, item.Code })
            .ToListAsync(cancellationToken);
        var rules = await (
                from rule in context.DiscountRules.AsNoTracking()
                join version in context.PriceListVersions.IgnoreAutoIncludes() on rule.PriceListVersionId equals version.Id
                where version.PriceListId == priceListId && version.Status != PriceListVersionStatus.Draft
                orderby version.VersionNumber
                select new { rule.Key, rule.Code })
            .ToListAsync(cancellationToken);

        var itemCodeByKey = new Dictionary<Guid, string>();
        var itemKeyByCode = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var row in items)
        {
            itemCodeByKey[row.Key] = row.Code;
            _ = itemKeyByCode.TryAdd(row.Code, row.Key);
        }

        var ruleCodeByKey = new Dictionary<Guid, string>();
        var ruleKeyByCode = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var row in rules)
        {
            ruleCodeByKey[row.Key] = row.Code;
            _ = ruleKeyByCode.TryAdd(row.Code, row.Key);
        }

        return new PriceListLedger(itemCodeByKey, itemKeyByCode, ruleCodeByKey, ruleKeyByCode);
    }

    /// <inheritdoc />
    public void AddVersion(PriceListVersion version) => context.PriceListVersions.Add(version);

    /// <inheritdoc />
    public EntityTag EntityTagOf(PriceListVersion version) => context.EntityTagOf(version);

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
                ConstraintName: BillingDbContext.PriceListVersionNumberIndex,
            })
        {
            return Result.Failure(BillingErrors.DraftNumberConflict);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: BillingDbContext.PriceListCodeIndex,
            })
        {
            // Two administrators created a list with the same code in the same moment; the read in the
            // handler saw neither. The loser is told what the read would have told them.
            return Result.Failure(BillingErrors.CodeNotUnique("code"));
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
            when (exception is { SqlState: PostgresErrorCodes.ExclusionViolation, ConstraintName: BillingDbContext.OnePublishedPriceListVersionConstraint })
        {
            return Result.Failure(BillingErrors.PublishConflict);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.ExclusionViolation,
                ConstraintName: BillingDbContext.OnePublishedPriceListVersionConstraint,
            })
        {
            return Result.Failure(BillingErrors.PublishConflict);
        }
        catch (PostgresException exception)
            when (exception is { SqlState: PostgresErrorCodes.ExclusionViolation, ConstraintName: BillingDbContext.OnePublishedVersionPerBranchConstraint })
        {
            // Two lists' versions pricing one branch, published in the same moment: each check read the
            // other's draft as not published. The constraint is deferred, so it arrives bare from the commit.
            return Result.Failure(BillingErrors.BranchPublishConflict);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.ExclusionViolation,
                ConstraintName: BillingDbContext.OnePublishedVersionPerBranchConstraint,
            })
        {
            return Result.Failure(BillingErrors.BranchPublishConflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(BillingErrors.PublishConflict);
        }
    }
}
