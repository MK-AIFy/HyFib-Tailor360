namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>The branch-owned resources Billing's routes name, for the resource scope they declare (ARCH-023).</summary>
public static class BillingResourceKinds
{
    /// <summary>An invoice, owned by the branch that issues it.</summary>
    public const string Invoice = "billing.invoice";

    /// <summary>A cashier session, reached by the branch whose drawer it is.</summary>
    public const string CashierSession = "billing.cashier_session";
}
