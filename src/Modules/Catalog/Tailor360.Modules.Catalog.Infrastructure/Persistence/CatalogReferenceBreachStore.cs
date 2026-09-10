using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The breach record over the Catalog context.
/// </summary>
/// <remarks>
/// The same context the reconciliation's inbox row is written on, which is the whole point: the breach and the
/// record that the check ran commit together or not at all.
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class CatalogReferenceBreachStore(CatalogDbContext context) : ICatalogReferenceBreachStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogReferenceBreach>> ListOpenAsync(
        Guid catalogVersionId,
        CancellationToken cancellationToken = default)
        => await context.ReferenceBreaches
            .Where(breach => breach.CatalogVersionId == catalogVersionId && breach.ResolvedAt == null)
            .OrderBy(breach => breach.DetectedAt)
            .ThenBy(breach => breach.Target)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogReferenceBreach>> ListOpenForOrganisationAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.ReferenceBreaches
            .Where(breach => breach.OrganisationId == organisationId && breach.ResolvedAt == null)
            .OrderByDescending(breach => breach.DetectedAt)
            .ThenBy(breach => breach.Target)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(CatalogReferenceBreach breach) => context.ReferenceBreaches.Add(breach);
}
