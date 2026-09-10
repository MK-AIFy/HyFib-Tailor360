using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.Modules.Catalog.Application.Abstractions;

/// <summary>
/// The reconciliation's record of cross-module references that stopped being valid after publication.
/// </summary>
/// <remarks>
/// <strong>Nothing here saves.</strong> Every write happens inside the delivery of an integration event, and the
/// dispatcher commits the handler's staged writes together with the inbox row that records the handler ran
/// (<c>IOutboxMessageHandler</c>). A store that saved on its own would put the breach in one transaction and the
/// record of having found it in another, and a crash between them would either lose the breach or replay the
/// reconciliation against a row that says it already ran.
/// </remarks>
public interface ICatalogReferenceBreachStore
{
    /// <summary>Every breach of one version that is still standing.</summary>
    /// <param name="catalogVersionId">The published version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open breaches, in the order they were detected.</returns>
    Task<IReadOnlyList<CatalogReferenceBreach>> ListOpenAsync(
        Guid catalogVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>Every breach of an organisation that is still standing, across every version.</summary>
    /// <remarks>
    /// Across every version because a version retired while a breach stood keeps it: the shop was exposed, and a
    /// list that hid it the moment the catalogue moved on would hide exactly the occurrences worth reviewing.
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open breaches, newest first.</returns>
    Task<IReadOnlyList<CatalogReferenceBreach>> ListOpenForOrganisationAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a newly opened breach.</summary>
    /// <param name="breach">The breach.</param>
    void Add(CatalogReferenceBreach breach);
}
