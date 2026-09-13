using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>The rendered documents over the module's context.</summary>
/// <param name="context">The module's context.</param>
public sealed class DocumentArtifactStore(BillingDbContext context) : IDocumentArtifactStore
{
    /// <inheritdoc />
    public async Task<DocumentArtifact?> FindAsync(DocumentKind kind, Guid documentId, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.DocumentArtifacts
            .Where(artifact => artifact.Kind == kind && artifact.DocumentId == documentId && artifact.OrganisationId == organisationId)
            .OrderByDescending(artifact => artifact.Version)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentArtifact>> ListPendingAsync(int limit, CancellationToken cancellationToken = default)
        => await context.DocumentArtifacts
            .Where(artifact => artifact.Status == DocumentArtifactStatus.Pending)
            .OrderBy(artifact => artifact.RequestedAt)
            .ThenBy(artifact => artifact.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(DocumentArtifact artifact) => context.DocumentArtifacts.Add(artifact);

    /// <inheritdoc />
    public void Forget(DocumentArtifact artifact) => context.Entry(artifact).State = EntityState.Detached;

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
            // Two workers on one artefact: the lease should have kept them apart, and the loser leaves the
            // winner's rendering where it is.
            return Result.Failure(BillingErrors.DocumentAlreadyRendered);
        }
    }
}
