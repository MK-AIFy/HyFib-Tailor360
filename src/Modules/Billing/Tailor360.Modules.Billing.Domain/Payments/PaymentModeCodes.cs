namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>
/// The one payment-mode code the domain itself has to know: cash is what a denomination count sheet
/// counts, so the cashier session derives the counted cash total from the sheet under this code.
/// Every other mode is data.
/// </summary>
public static class PaymentModeCodes
{
    /// <summary>Notes and coins in the drawer.</summary>
    public const string Cash = "CASH";
}
