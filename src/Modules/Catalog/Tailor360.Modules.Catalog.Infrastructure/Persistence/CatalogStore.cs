using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Domain;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// Reads and writes catalogue versions over the module's context.
/// </summary>
/// <remarks>
/// A version is loaded whole, which the context arranges by auto-including its categories and service
/// types and their child rows. The data is small by construction — the seeded catalogue is seven
/// categories and eighteen service types — and every rule worth enforcing spans the version, so
/// loading it in pieces would buy nothing and cost the ability to check anything.
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class CatalogStore(CatalogDbContext context) : ICatalogStore
{
    /// <inheritdoc />
    public async Task<CatalogVersion?> FindAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.CatalogVersions
            .SingleOrDefaultAsync(
                version => version.Id == versionId && version.OrganisationId == organisationId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<CatalogVersion?> FindPublishedAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.CatalogVersions
            .SingleOrDefaultAsync(
                version => version.OrganisationId == organisationId
                           && version.Status == CatalogStatus.Published,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogVersion>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.CatalogVersions
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
        var highest = await context.CatalogVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.OrganisationId == organisationId)
            .MaxAsync(version => (int?)version.VersionNumber, cancellationToken);

        return (highest ?? 0) + 1;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Two folds over the same rows, in publication order, and the direction of each is the rule it
    /// serves. <c>CodeByKey</c> keeps the <em>last</em> code a concept was published under, because
    /// that is what a new draft must still call it. <c>KeyByCode</c> keeps the <em>first</em> concept a
    /// code was published for, because a code belongs to the thing that first used it and is never
    /// handed on — not even after that thing is retired.
    /// </remarks>
    public async Task<CatalogCodeLedger> ReadCodeHistoryAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var categories = await (
                from category in context.Categories.AsNoTracking().IgnoreAutoIncludes()
                join version in context.CatalogVersions.IgnoreAutoIncludes()
                    on category.CatalogVersionId equals version.Id
                where category.OrganisationId == organisationId && version.Status != CatalogStatus.Draft
                orderby version.VersionNumber
                select new CodeRow(category.Key, category.Code))
            .ToListAsync(cancellationToken);

        var services = await (
                from service in context.ServiceTypes.AsNoTracking().IgnoreAutoIncludes()
                join category in context.Categories.IgnoreAutoIncludes()
                    on service.CategoryId equals category.Id
                join version in context.CatalogVersions.IgnoreAutoIncludes()
                    on service.CatalogVersionId equals version.Id
                where service.OrganisationId == organisationId && version.Status != CatalogStatus.Draft
                orderby version.VersionNumber
                select new CodeRow(service.Key, category.Code + "." + service.Code))
            .ToListAsync(cancellationToken);

        var groups = await (
                from grp in context.DesignGroups.AsNoTracking().IgnoreAutoIncludes()
                join category in context.Categories.IgnoreAutoIncludes()
                    on grp.CategoryId equals category.Id
                join version in context.CatalogVersions.IgnoreAutoIncludes()
                    on grp.CatalogVersionId equals version.Id
                where grp.OrganisationId == organisationId && version.Status != CatalogStatus.Draft
                orderby version.VersionNumber
                select new CodeRow(grp.Key, category.Code + "." + grp.Code))
            .ToListAsync(cancellationToken);

        var options = await (
                from option in context.DesignOptions.AsNoTracking().IgnoreAutoIncludes()
                join grp in context.DesignGroups.IgnoreAutoIncludes()
                    on option.DesignOptionGroupId equals grp.Id
                join category in context.Categories.IgnoreAutoIncludes()
                    on grp.CategoryId equals category.Id
                join version in context.CatalogVersions.IgnoreAutoIncludes()
                    on option.CatalogVersionId equals version.Id
                where option.OrganisationId == organisationId && version.Status != CatalogStatus.Draft
                orderby version.VersionNumber
                select new CodeRow(option.Key, category.Code + "." + grp.Code + "." + option.Code))
            .ToListAsync(cancellationToken);

        var (categoryCodeByKey, categoryKeyByCode) = Fold(categories);
        var (serviceCodeByKey, serviceKeyByCode) = Fold(services);
        var (groupCodeByKey, groupKeyByCode) = Fold(groups);
        var (optionCodeByKey, optionKeyByCode) = Fold(options);

        return new CatalogCodeLedger(
            categoryCodeByKey,
            categoryKeyByCode,
            serviceCodeByKey,
            serviceKeyByCode,
            groupCodeByKey,
            groupKeyByCode,
            optionCodeByKey,
            optionKeyByCode);
    }


    /// <inheritdoc />
    public void Add(CatalogVersion version) => context.CatalogVersions.Add(version);

    /// <inheritdoc />
    public EntityTag EntityTagOf(CatalogVersion version) => context.EntityTagOf(version);

    /// <inheritdoc />
    public async Task<int> SaveAsync(CancellationToken cancellationToken = default)
        => await context.SaveChangesAsync(cancellationToken);

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
                ConstraintName: CatalogDbContext.VersionNumberIndex,
            })
        {
            // Two administrators started a draft in the same moment and read the same maximum version
            // number. The insert that lost is told so; the draft it would have created does not exist,
            // which is exactly what "nothing was created" has to mean for a caller that retries.
            return Result.Failure(CatalogErrors.DraftNumberConflict);
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
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CatalogDbContext.OnePublishedVersionIndex,
            })
        {
            // Two administrators published different drafts in the same moment. The database settled
            // it, and the loser is told plainly rather than being shown a five hundred: the version
            // they were looking at is still a draft, and somebody else's is now live.
            return Result.Failure(CatalogErrors.PublishConflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The draft moved under the caller between the read and the save. Same answer as a failed
            // If-Match, because it is the same situation arriving a moment later.
            return Result.Failure(CatalogErrors.VersionChanged);
        }
    }

    private static (Dictionary<Guid, string> CodeByKey, Dictionary<string, Guid> KeyByCode) Fold(
        IEnumerable<CodeRow> rows)
    {
        var codeByKey = new Dictionary<Guid, string>();
        var keyByCode = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            codeByKey[row.Key] = row.Code;
            _ = keyByCode.TryAdd(row.Code, row.Key);
        }

        return (codeByKey, keyByCode);
    }

    private sealed record CodeRow(Guid Key, string Code);
}
