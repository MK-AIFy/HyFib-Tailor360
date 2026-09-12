namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>The branch-owned resources Billing's routes name, for the resource scope they declare (ARCH-023).</summary>
public static class BillingResourceKinds
{
    /// <summary>An invoice, owned by the branch that issues it.</summary>
    public const string Invoice = "billing.invoice";

    /// <summary>A cashier session, reached by the branch whose drawer it is.</summary>
    public const string CashierSession = "billing.cashier_session";

    /// <summary>A payment, reached by the branch it was taken at.</summary>
    public const string Payment = "billing.payment";

    /// <summary>An order as Billing knows it, reached by the branch the order was confirmed at.</summary>
    public const string Order = "billing.order";
}
