using System.Collections.Concurrent;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Contracts.Catalogue;

namespace Tailor360.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// One process's copy of the catalogue versions it has read, keyed by version identifier.
/// </summary>
/// <remarks>
/// <para>
/// A singleton, and version-keyed because that is what makes it correct rather than merely fast
/// (decision D21). A published version's tree cannot change: publishing a successor does not rewrite
/// this one, it supersedes it, and every read begins by asking the database which version is published
/// now. So a cached entry is never a stale answer to the question that was asked — at worst it is an
/// answer nobody asks for any more.
/// </para>
/// <para>
/// Which leaves one exception, stated rather than hidden: a published version admits corrections to
/// its labels, descriptions and display order. The correcting node clears its own cache at once; a
/// second web replica would show the old label until its entry is evicted. That is a caption on a
/// screen, and D21 is explicit that no application cache is ever authoritative.
/// </para>
/// <para>
/// The cap is a memory bound and nothing more. A shop reads its published version constantly and a
/// handful of retired ones while rendering old job cards; past that, the least recently stored entry
/// goes and is re-read on demand.
/// </para>
/// </remarks>
public sealed class CatalogSnapshotCache : ICatalogCache
{
    /// <summary>How many versions one process keeps.</summary>
    public const int Capacity = 8;

    private readonly ConcurrentDictionary<Guid, CachedCatalogVersion> _entries = new();
    private long _sequence;

    /// <inheritdoc />
    public void Invalidate() => _entries.Clear();

    /// <summary>How many versions are held. Exposed so a test can prove the cap holds.</summary>
    public int Count => _entries.Count;

    /// <summary>One cached version, or null.</summary>
    /// <param name="versionId">The version.</param>
    /// <returns>The entry, or null when this node has not read it.</returns>
    public CachedCatalogVersion? Find(Guid versionId)
        => _entries.TryGetValue(versionId, out var entry) ? entry : null;

    /// <summary>Keeps a version, evicting the least recently stored if the cap is reached.</summary>
    /// <param name="entry">The version.</param>
    public void Store(CachedCatalogVersion entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries[entry.VersionId] = entry with { Sequence = Interlocked.Increment(ref _sequence) };

        while (_entries.Count > Capacity)
        {
            var oldest = _entries.Values.MinBy(candidate => candidate.Sequence);

            if (oldest is null || !_entries.TryRemove(oldest.VersionId, out _))
            {
                break;
            }
        }
    }
}

/// <summary>One catalogue version as this node keeps it.</summary>
/// <param name="VersionId">The version.</param>
/// <param name="OrganisationId">The organisation it belongs to.</param>
/// <param name="Services">Every service type of the version, by identifier.</param>
/// <param name="Sequence">When it was stored, as a monotonic counter used only for eviction.</param>
public sealed record CachedCatalogVersion(
    Guid VersionId,
    Guid OrganisationId,
    IReadOnlyDictionary<Guid, CachedService> Services,
    long Sequence = 0);

/// <summary>
/// One service type, with everything <c>IsOrderable</c> has to know about it and its category.
/// </summary>
/// <remarks>
/// Flattened at read time rather than walked per question. Orderability asks about the service and its
/// category together — active periods, branch sets, the flag, whether the category is a grouping
/// node — and answering it from the tree would mean a lookup per field on a path that runs on every
/// intake keystroke.
/// </remarks>
/// <param name="Snapshot">The service as another module reads it.</param>
/// <param name="CategoryIsGroupingNode">Whether the category has sub-categories, and is therefore never ordered against.</param>
/// <param name="CategoryFeatureFlagKey">The flag that can switch the category off, or null.</param>
/// <param name="CategoryActiveFrom">The category's first active day, or null.</param>
/// <param name="CategoryActiveTo">The category's last active day, or null.</param>
/// <param name="CategoryBranchIds">The branches offering the category.</param>
/// <param name="ServiceActiveFrom">The service's first active day, or null.</param>
/// <param name="ServiceActiveTo">The service's last active day, or null.</param>
/// <param name="ServiceBranchIds">The branches offering the service.</param>
public sealed record CachedService(
    CatalogServiceSnapshot Snapshot,
    bool CategoryIsGroupingNode,
    string? CategoryFeatureFlagKey,
    DateOnly? CategoryActiveFrom,
    DateOnly? CategoryActiveTo,
    IReadOnlySet<Guid> CategoryBranchIds,
    DateOnly? ServiceActiveFrom,
    DateOnly? ServiceActiveTo,
    IReadOnlySet<Guid> ServiceBranchIds)
{
    /// <summary>Whether both the category and the service are within their active periods.</summary>
    /// <param name="on">The day, in the branch's timezone.</param>
    /// <returns>True when both are active.</returns>
    public bool IsActiveOn(DateOnly on)
        => Within(CategoryActiveFrom, CategoryActiveTo, on) && Within(ServiceActiveFrom, ServiceActiveTo, on);

    /// <summary>Whether a branch offers both the category and the service.</summary>
    /// <param name="branchId">The branch.</param>
    /// <returns>True when both sets contain it.</returns>
    public bool IsOfferedAt(Guid branchId)
        => CategoryBranchIds.Contains(branchId) && ServiceBranchIds.Contains(branchId);

    private static bool Within(DateOnly? from, DateOnly? to, DateOnly on)
        => (from is not { } start || on >= start) && (to is not { } end || on <= end);
}
