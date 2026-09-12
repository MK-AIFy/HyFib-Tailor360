namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>A branch a payment mode is restricted to. The same shape as a price-list version's branches.</summary>
public sealed class PaymentModeBranch
{
    private PaymentModeBranch()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private PaymentModeBranch(Guid paymentModeId, Guid branchId)
    {
        PaymentModeId = paymentModeId;
        BranchId = branchId;
    }

    /// <summary>The mode.</summary>
    public Guid PaymentModeId { get; private set; }

    /// <summary>The branch.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>Restricts a mode to a branch.</summary>
    public static PaymentModeBranch For(Guid paymentModeId, Guid branchId) => new(paymentModeId, branchId);
}
