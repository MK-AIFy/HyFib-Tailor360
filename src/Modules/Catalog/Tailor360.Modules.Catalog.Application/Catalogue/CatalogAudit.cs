using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Writes the Catalog module's audit entries.
/// </summary>
/// <remarks>
/// <para>
/// The module's rows are in the <c>catalog</c> schema and the trail is in <c>platform</c>, so they are
/// different contexts and different transactions. The order is the house rule: <strong>save the change
/// first, then record it</strong>. The trail may lag reality and must never lead it.
/// </para>
/// <para>
/// <strong>The catalogue holds no personal data at all</strong>, which makes this the one audit helper
/// in the application whose snapshots may carry content rather than shape. A code, a label and a count
/// are configuration an administrator wrote about the shop's own services, not facts about a customer,
/// so recording them is what makes the trail answer the question it exists for: which version said
/// what, when, and on whose authority.
/// </para>
/// <para>
/// Every entry is recorded against the <em>version</em>, including entries about a single category.
/// The version is the aggregate and the thing an administrator reasons about, so a trail keyed to it
/// reads as the history of one catalogue rather than as scattered notes about rows.
/// </para>
/// </remarks>
internal static class CatalogAudit
{
    /// <summary>The entity type every Catalog entry is recorded against.</summary>
    public const string EntityType = "catalog.version";

    /// <summary>Records one change and commits the entry.</summary>
    /// <param name="audit">The platform's audit writer.</param>
    /// <param name="action">The action constant.</param>
    /// <param name="versionId">The catalogue version.</param>
    /// <param name="summary">What happened, in words.</param>
    /// <param name="reason">The actor's reason, where the action demands one.</param>
    /// <param name="before">The snapshot before, where there was one.</param>
    /// <param name="after">The snapshot after.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the entry is committed.</returns>
    public static async Task RecordAsync(
        IAuditWriter audit,
        string action,
        Guid versionId,
        string summary,
        string? reason,
        ICatalogAuditState? before,
        ICatalogAuditState? after,
        CancellationToken cancellationToken)
    {
        await audit.WriteAsync(
            new AuditEntry(action, EntityType, versionId, summary, reason, before, after),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// A shape that may be written into the audit trail as a catalogue before or after state.
/// </summary>
/// <remarks>
/// A marker with no members, so that adding a new audit shape is a deliberate act rather than whatever
/// happened to be in scope at the call site. It is the same guard the Customers module uses, kept here
/// for the same reason even though the risk it manages there — personal data reaching the trail — does
/// not exist in this module.
/// </remarks>
internal interface ICatalogAuditState;

/// <summary>A catalogue version as the trail describes it.</summary>
/// <param name="VersionNumber">The number an administrator reads.</param>
/// <param name="Status">Where the version stood.</param>
/// <param name="CategoryCount">How many categories it held.</param>
/// <param name="ServiceTypeCount">How many service types it held.</param>
/// <param name="NotOrderableCount">
/// How many service types were published with a link missing, and are therefore configuration nobody
/// can order against. The number an operator acts on after a publication.
/// </param>
internal sealed record CatalogVersionSnapshot(
    int VersionNumber,
    string Status,
    int CategoryCount,
    int ServiceTypeCount,
    int NotOrderableCount) : ICatalogAuditState
{
    /// <summary>Takes a snapshot of a version.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The snapshot.</returns>
    public static CatalogVersionSnapshot Of(CatalogVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new CatalogVersionSnapshot(
            version.VersionNumber,
            version.Status.ToString(),
            version.Categories.Count,
            version.ServiceTypes.Count,
            version.ServiceTypes.Count(service => service.NotOrderable));
    }
}

/// <summary>One category or service type as the trail describes it.</summary>
/// <param name="Code">The machine key, which is what every other record refers to.</param>
/// <param name="Name">The label at the time.</param>
/// <param name="ParentCode">The parent category's code, or null at top level.</param>
/// <param name="DisplayOrder">Where it sat among its siblings.</param>
/// <param name="BranchCount">How many branches offered it.</param>
/// <param name="ActiveFrom">The first day it was offered, or null for always.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
internal sealed record CatalogEntrySnapshot(
    string Code,
    string Name,
    string? ParentCode,
    int DisplayOrder,
    int BranchCount,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo) : ICatalogAuditState
{
    /// <summary>Takes a snapshot of a category.</summary>
    /// <param name="category">The category.</param>
    /// <param name="parentCode">Its parent's code, resolved by the caller.</param>
    /// <returns>The snapshot.</returns>
    public static CatalogEntrySnapshot Of(Category category, string? parentCode)
    {
        ArgumentNullException.ThrowIfNull(category);

        return new CatalogEntrySnapshot(
            category.Code,
            category.Name,
            parentCode,
            category.DisplayOrder,
            category.Branches.Count,
            category.ActiveFrom,
            category.ActiveTo);
    }

    /// <summary>Takes a snapshot of a service type.</summary>
    /// <param name="service">The service type.</param>
    /// <param name="categoryCode">Its category's code, resolved by the caller.</param>
    /// <returns>The snapshot.</returns>
    public static CatalogEntrySnapshot Of(ServiceType service, string categoryCode)
    {
        ArgumentNullException.ThrowIfNull(service);

        return new CatalogEntrySnapshot(
            service.Code,
            service.Name,
            categoryCode,
            service.DisplayOrder,
            service.Branches.Count,
            service.ActiveFrom,
            service.ActiveTo);
    }
}
