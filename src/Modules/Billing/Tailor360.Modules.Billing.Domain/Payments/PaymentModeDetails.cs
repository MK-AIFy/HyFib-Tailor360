namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>What an administrator sets on a payment mode; the code is fixed at definition.</summary>
/// <param name="Name">The name the cashier sees on the button.</param>
/// <param name="RequiresReference">Whether a payment in this mode must carry an external reference (a UPI transaction identifier, a card authorisation).</param>
/// <param name="RequiresProvider">Whether a payment in this mode goes through a provider intent rather than being recorded by hand.</param>
/// <param name="AllowedForRefund">Whether a refund may be paid out through this mode.</param>
public sealed record PaymentModeDetails(string Name, bool RequiresReference, bool RequiresProvider, bool AllowedForRefund);
