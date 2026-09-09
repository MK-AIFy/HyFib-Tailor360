namespace Tailor360.Modules.Catalog.Application.Abstractions;

/// <summary>
/// Discards what this node has cached about the catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Called by the node that changed something, so that its own next read reflects its own write. It is
/// not a distributed invalidation and does not pretend to be: the cache it clears is in this process
/// (decision D21 permits a distributed cache only once more than one replica runs, and the application
/// does not run one today).
/// </para>
/// <para>
/// What makes that safe on another node is the shape of the cache rather than the sweeping.
/// Entries are keyed by the catalogue version's identifier and a published version's tree is
/// immutable, so publishing a new version does not make an entry wrong — it makes it
/// <em>unreachable</em>, because the identifier every read starts from has changed. The one mutation a
/// published version admits is a presentation correction, and a label lagging on a second web replica
/// for a moment is exactly the kind of staleness D21's "no application cache is ever authoritative"
/// permits.
/// </para>
/// </remarks>
public interface ICatalogCache
{
    /// <summary>Discards every cached catalogue version on this node.</summary>
    void Invalidate();
}
