using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Domain;
using Tailor360.Modules.Catalog.Domain.Design;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Catalog.Infrastructure.Persistence;

/// <summary>Design selection drafts over the Catalog context (#30, issue #140).</summary>
/// <param name="context">The module's context.</param>
public sealed class DesignSelectionDraftStore(CatalogDbContext context) : IDesignSelectionDraftStore
{
    /// <inheritdoc />
    public async Task<DesignSelectionDraft?> FindAsync(
        Guid draftId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.DesignSelectionDrafts.SingleOrDefaultAsync(
            draft => draft.Id == draftId && draft.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public void Add(DesignSelectionDraft draft) => context.DesignSelectionDrafts.Add(draft);

    /// <inheritdoc />
    public EntityTag EntityTagOf(DesignSelectionDraft draft) => context.EntityTagOf(draft);

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
            // Two people on one branch choosing a design between them. Last-writer-wins here would
            // silently drop half the garment's choices, so the loser is told and re-reads — the same
            // translation MeasurementCaptureStore.SaveAsync makes.
            return Result.Failure(CatalogErrors.DesignDraftChanged);
        }
    }
}
