namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>One branch a price-list version prices for.</summary>
public sealed class PriceListVersionBranch
{
    private PriceListVersionBranch()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private PriceListVersionBranch(Guid priceListVersionId, Guid branchId)
    {
        PriceListVersionId = priceListVersionId;
        BranchId = branchId;
    }

    /// <summary>The version.</summary>
    public Guid PriceListVersionId { get; private set; }

    /// <summary>The branch.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>A row for a version and a branch.</summary>
    public static PriceListVersionBranch For(Guid priceListVersionId, Guid branchId) => new(priceListVersionId, branchId);
}
