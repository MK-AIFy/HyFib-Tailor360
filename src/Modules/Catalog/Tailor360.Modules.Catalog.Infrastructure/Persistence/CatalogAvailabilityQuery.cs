using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.FeatureFlags;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// Answers what the catalogue offers, reading through the version-keyed cache.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Which version is published is never cached; what is in a version always is.</strong> That
/// split is the whole design. The first is one indexed single-row read and changes the moment somebody
/// publishes, so caching it would be the one thing that could make this answer wrong. The second
/// cannot change at all once published, so caching it costs nothing in correctness and saves the tree
/// walk on a path that runs on every intake keystroke.
/// </para>
/// <para>
/// <strong>Active dates are read as dates in the branch's timezone.</strong> "Offered from the first
/// of October" is a statement about the shop's calendar; evaluated in UTC it would open the category
/// five and a half hours early (<c>docs/architecture/conventions.md</c> 2.4). The default zone is
/// Indian Standard Time; per-branch zones arrive with #25's branch record and this is where they will
/// be read.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
/// <param name="cache">This node's cached versions.</param>
/// <param name="flags">The platform's feature-flag evaluation.</param>
public sealed class CatalogAvailabilityQuery(
    CatalogDbContext context,
    CatalogSnapshotCache cache,
    IFeatureFlags flags)
    : ICatalogAvailabilityQuery
{
    /// <inheritdoc />
    public async Task<bool> IsOrderableAsync(
        Guid serviceTypeId,
        Guid branchId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var located = await LocateAsync(serviceTypeId, cancellationToken);

        if (located is null)
        {
            return false;
        }

        var published = await PublishedVersionIdAsync(located.OrganisationId, cancellationToken);

        // A service of a retired or draft version is readable and is not orderable. This is the check
        // that keeps a retired category off the intake screen while its old jobs still render.
        if (published != located.VersionId || located.Service is not { } service)
        {
            return false;
        }

        if (!IsOrderable(service, branchId, TodayAt(at)))
        {
            return false;
        }

        // Last, because it is the only check that can reach outside this module. An unknown flag is
        // off, which is the safe default: a category behind a flag nobody has configured is not
        // offered rather than offered by accident.
        return service.CategoryFeatureFlagKey is not { } flagKey
               || await flags.IsEnabledAsync(
                   flagKey,
                   new OrganisationContext(located.OrganisationId, branchId),
                   cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogServiceSnapshot>> GetOrderableAsync(
        Guid organisationId,
        Guid branchId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var published = await PublishedVersionIdAsync(organisationId, cancellationToken);

        if (published is not { } versionId
            || await LoadAsync(versionId, cancellationToken) is not { } version)
        {
            return [];
        }

        var today = TodayAt(at);
        var orderable = new List<CatalogServiceSnapshot>();

        foreach (var service in version.Services.Values)
        {
            if (!IsOrderable(service, branchId, today))
            {
                continue;
            }

            if (service.CategoryFeatureFlagKey is { } flagKey
                && !await flags.IsEnabledAsync(
                    flagKey, new OrganisationContext(organisationId, branchId), cancellationToken))
            {
                continue;
            }

            orderable.Add(service.Snapshot);
        }

        return
        [
            .. orderable
                .OrderBy(service => service.CategoryCode, StringComparer.Ordinal)
                .ThenBy(service => service.ServiceCode, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    public async Task<CatalogServiceSnapshot?> GetServiceAsync(
        Guid serviceTypeId,
        CancellationToken cancellationToken = default)
    {
        var located = await LocateAsync(serviceTypeId, cancellationToken);

        return located?.Service?.Snapshot;
    }

    /// <inheritdoc />
    public async Task<Guid?> GetPublishedVersionIdAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await PublishedVersionIdAsync(organisationId, cancellationToken);

    private async Task<Located?> LocateAsync(Guid serviceTypeId, CancellationToken cancellationToken)
    {
        var owner = await context.ServiceTypes
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(service => service.Id == serviceTypeId)
            .Select(service => new { service.OrganisationId, service.CatalogVersionId })
            .SingleOrDefaultAsync(cancellationToken);

        if (owner is null)
        {
            return null;
        }

        var version = await LoadAsync(owner.CatalogVersionId, cancellationToken);

        return new Located(
            owner.OrganisationId,
            owner.CatalogVersionId,
            version?.Services.GetValueOrDefault(serviceTypeId));
    }

    private async Task<Guid?> PublishedVersionIdAsync(
        Guid organisationId,
        CancellationToken cancellationToken)
        => await context.CatalogVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.OrganisationId == organisationId
                              && version.Status == CatalogStatus.Published)
            .Select(version => (Guid?)version.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CachedCatalogVersion?> LoadAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        if (cache.Find(versionId) is { } cached)
        {
            return cached;
        }

        var version = await context.CatalogVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == versionId, cancellationToken);

        if (version is null)
        {
            return null;
        }

        var services = new Dictionary<Guid, CachedService>();

        foreach (var service in version.ServiceTypes)
        {
            if (version.Find(service.CategoryId) is not { } category)
            {
                // A service whose category is missing is the orphan publish validation refuses. It
                // cannot be answered about, and treating it as unknown is the honest answer.
                continue;
            }

            services[service.Id] = new CachedService(
                CatalogProjection.ToSnapshot(service, category),
                version.IsGroupingNode(category.Id),
                category.FeatureFlagKey,
                category.ActiveFrom,
                category.ActiveTo,
                category.BranchIds.ToHashSet(),
                service.ActiveFrom,
                service.ActiveTo,
                service.BranchIds.ToHashSet());
        }

        var entry = new CachedCatalogVersion(version.Id, version.OrganisationId, services);

        cache.Store(entry);

        return entry;
    }

    /// <summary>
    /// Everything about orderability that does not need to leave this process.
    /// </summary>
    /// <remarks>
    /// Shared by the single question and the branch-wide list so that there is one answer to "what
    /// orderable means". The feature flag is deliberately outside it: it is the one check that reaches
    /// another component, and keeping it at the call sites is what stops the list asking the flag
    /// evaluator once per service that had already failed on a date or a branch.
    /// </remarks>
    private static bool IsOrderable(CachedService service, Guid branchId, DateOnly on)
        => !service.CategoryIsGroupingNode
           && !service.Snapshot.NotOrderable
           && service.IsActiveOn(on)
           && service.IsOfferedAt(branchId);

    /// <summary>The instant read as a date in the branch's timezone.</summary>
    private static DateOnly TodayAt(DateTimeOffset at)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, IndiaTimeZone.Instance).DateTime);

    private sealed record Located(Guid OrganisationId, Guid VersionId, CachedService? Service);
}
