using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Catalog.Infrastructure.Catalogue;

/// <summary>
/// Which branch is choosing a design (#30, issue #140).
/// </summary>
/// <remarks>
/// The module's first <see cref="IResourceScopeResolver"/>. <c>CatalogModuleServiceCollectionExtensions</c>
/// explains why the catalogue itself registers none: a category is offered at a <em>set</em> of branches
/// and a catalogue version is organisation-wide configuration, neither of which one branch owns. A design
/// selection draft is the opposite shape, for the same reason a measurement draft is
/// (<c>MeasurementDraftScopeResolver</c>): it is one garment's choices in progress, shared within the
/// branch choosing it, and another branch writing to it would be overwriting a decision somebody else's
/// customer is standing in front of.
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class DesignSelectionDraftScopeResolver(CatalogDbContext context) : IResourceScopeResolver
{
    /// <inheritdoc />
    public string ResourceKind => DesignSelectionResourceKinds.DesignSelectionDraft;

    /// <inheritdoc />
    public async ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        // One indexed read of the owning row, and only the column that decides. A draft carries no
        // notion of assignment: whoever is at the counter chooses.
        var branchId = await context.DesignSelectionDrafts
            .Where(draft => draft.Id == resourceId)
            .Select(draft => (Guid?)draft.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        return branchId is { } branch
            ? ResourceScope.Unassigned(DesignSelectionResourceKinds.DesignSelectionDraft, resourceId, branch)
            : null;
    }
}
