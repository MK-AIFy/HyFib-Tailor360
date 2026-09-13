using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Billing.Infrastructure.Payments;

/// <summary>Answers which branch a cashier session belongs to, for the routes that name one (ARCH-023).</summary>
public sealed class CashierSessionScopeResolver(BillingDbContext context) : IResourceScopeResolver
{
    /// <inheritdoc />
    public string ResourceKind => BillingResourceKinds.CashierSession;

    /// <inheritdoc />
    public async ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        // The branch decides reach; who may close it is the handler's check against the session's cashier.
        var branchId = await context.CashierSessions
            .IgnoreAutoIncludes()
            .Where(session => session.Id == resourceId)
            .Select(session => (Guid?)session.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        return branchId is { } branch ? ResourceScope.Unassigned(BillingResourceKinds.CashierSession, resourceId, branch) : null;
    }
}
