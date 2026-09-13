using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Billing.Infrastructure.Invoicing;

/// <summary>Which branch an invoice belongs to, for the branch requirement on its routes (ARCH-023).</summary>
public sealed class InvoiceScopeResolver(BillingDbContext context) : IResourceScopeResolver
{
    /// <inheritdoc />
    public string ResourceKind => BillingResourceKinds.Invoice;

    /// <inheritdoc />
    public async ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        // One indexed read of the owning row, and only the column that decides. An invoice carries no
        // notion of assignment: whoever is at the till bills.
        var branchId = await context.Invoices
            .IgnoreAutoIncludes()
            .Where(invoice => invoice.Id == resourceId)
            .Select(invoice => (Guid?)invoice.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        return branchId is { } branch ? ResourceScope.Unassigned(BillingResourceKinds.Invoice, resourceId, branch) : null;
    }
}
