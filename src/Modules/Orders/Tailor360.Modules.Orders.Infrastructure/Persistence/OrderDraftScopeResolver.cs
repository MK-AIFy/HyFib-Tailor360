using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>Loads the owning branch before draft read or edit authorisation.</summary>
public sealed class OrderDraftScopeResolver(OrdersDbContext context) : IResourceScopeResolver
{
    public string ResourceKind => OrderDraftResourceKinds.Draft;

    public async ValueTask<ResourceScope?> ResolveAsync(
        Guid resourceId, CancellationToken cancellationToken)
    {
        var branchId = await context.OrderDrafts
            .Where(draft => draft.Id == resourceId)
            .Select(draft => (Guid?)draft.BranchId)
            .SingleOrDefaultAsync(cancellationToken);
        return branchId is { } branch
            ? ResourceScope.Unassigned(OrderDraftResourceKinds.Draft, resourceId, branch)
            : null;
    }
}
