using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>The rendered documents, over the module's own context.</summary>
public interface IDocumentArtifactStore
{
    /// <summary>The latest artefact of a document, tracked, or null.</summary>
    Task<DocumentArtifact?> FindAsync(DocumentKind kind, Guid documentId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The pending artefacts, oldest first, at most the number asked, tracked.</summary>
    Task<IReadOnlyList<DocumentArtifact>> ListPendingAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Stages a new artefact; committed by <see cref="SaveAsync"/>.</summary>
    void Add(DocumentArtifact artifact);

    /// <summary>Commits the unit of work.</summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops tracking an artefact whose save was refused, so its stale row does not ride along with — and
    /// fail — every later save in the same pass.
    /// </summary>
    void Forget(DocumentArtifact artifact);
}
