using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Orders.Infrastructure.Drafts;

/// <summary>
/// Which branch is building an order draft (#199).
/// </summary>
/// <remarks>
/// The module's first <see cref="IResourceScopeResolver"/>. It answers for the draft only: a draft
/// belongs to exactly one branch for its life — the branch it was started in — the same shape
/// <c>DesignSelectionDraftScopeResolver</c> and <c>MeasurementDraftScopeResolver</c> both answer for
/// their own drafts.
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class OrderDraftScopeResolver(OrdersDbContext context) : IResourceScopeResolver
{
    /// <inheritdoc />
    public string ResourceKind => OrdersResourceKinds.OrderDraft;

    /// <inheritdoc />
    public async ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        // One indexed read of the owning row, and only the column that decides. A draft carries no
        // notion of assignment: whoever is at the counter, holding orders.intake, may carry it on.
        var branchId = await context.OrderDrafts
            .Where(draft => draft.Id == resourceId)
            .Select(draft => (Guid?)draft.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        return branchId is { } branch
            ? ResourceScope.Unassigned(OrdersResourceKinds.OrderDraft, resourceId, branch)
            : null;
    }
}
