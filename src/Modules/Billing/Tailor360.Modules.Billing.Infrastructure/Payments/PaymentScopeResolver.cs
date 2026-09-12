using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Billing.Infrastructure.Payments;

/// <summary>Answers which branch a payment was taken at, for the routes that name one (ARCH-023).</summary>
public sealed class PaymentScopeResolver(BillingDbContext context) : IResourceScopeResolver
{
    /// <inheritdoc />
    public string ResourceKind => BillingResourceKinds.Payment;

    /// <inheritdoc />
    public async ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var branchId = await context.Payments
            .IgnoreAutoIncludes()
            .Where(payment => payment.Id == resourceId)
            .Select(payment => (Guid?)payment.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        return branchId is { } branch ? ResourceScope.Unassigned(BillingResourceKinds.Payment, resourceId, branch) : null;
    }
}

/// <summary>
/// Answers which branch an order was confirmed at, from Billing's own fact of it, for the Billing routes
/// that name an order (ARCH-023). Orders' own routes answer from Orders; this one never reads there.
/// </summary>
public sealed class OrderFactScopeResolver(BillingDbContext context) : IResourceScopeResolver
{
    /// <inheritdoc />
    public string ResourceKind => BillingResourceKinds.Order;

    /// <inheritdoc />
    public async ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var branchId = await context.OrderFacts
            .IgnoreAutoIncludes()
            .Where(order => order.OrderId == resourceId)
            .Select(order => (Guid?)order.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        return branchId is { } branch ? ResourceScope.Unassigned(BillingResourceKinds.Order, resourceId, branch) : null;
    }
}

/// <summary>Answers which branch a receipt was issued at, for the routes that name one (ARCH-023).</summary>
public sealed class ReceiptScopeResolver(BillingDbContext context) : IResourceScopeResolver
{
    /// <inheritdoc />
    public string ResourceKind => BillingResourceKinds.Receipt;

    /// <inheritdoc />
    public async ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var branchId = await context.Receipts
            .IgnoreAutoIncludes()
            .Where(receipt => receipt.Id == resourceId)
            .Select(receipt => (Guid?)receipt.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        return branchId is { } branch ? ResourceScope.Unassigned(BillingResourceKinds.Receipt, resourceId, branch) : null;
    }
}

/// <summary>Answers which branch a refund was paid at, for the routes that name one (ARCH-023).</summary>
public sealed class RefundScopeResolver(BillingDbContext context) : IResourceScopeResolver
{
    /// <inheritdoc />
    public string ResourceKind => BillingResourceKinds.Refund;

    /// <inheritdoc />
    public async ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var branchId = await context.Refunds
            .IgnoreAutoIncludes()
            .Where(refund => refund.Id == resourceId)
            .Select(refund => (Guid?)refund.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        return branchId is { } branch ? ResourceScope.Unassigned(BillingResourceKinds.Refund, resourceId, branch) : null;
    }
}
