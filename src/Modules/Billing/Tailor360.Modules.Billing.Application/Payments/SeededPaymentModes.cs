using Tailor360.Modules.Billing.Domain.Payments;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// The five modes the plan seeds (<c>docs/IMPLEMENTATION_PLAN.md</c>, #43: cash, card, UPI, bank transfer,
/// other). The flags are the seed's starting position for a shop with standalone terminals and no payment
/// gateway (OD-03 is open): a card, UPI or bank payment carries the reference the terminal or the bank
/// prints, none goes through a provider, and only cash may be refunded through the drawer. An Owner
/// changes any of them; the seeder never changes them back.
/// </summary>
public static class SeededPaymentModes
{
    /// <summary>The seeded set, in the order the buttons are laid out.</summary>
    public static IReadOnlyList<SeededPaymentMode> All { get; } =
    [
        new(PaymentModeCodes.Cash, "Cash", RequiresReference: false, RequiresProvider: false, AllowedForRefund: true),
        new("CARD", "Card", RequiresReference: true, RequiresProvider: false, AllowedForRefund: false),
        new("UPI", "UPI", RequiresReference: true, RequiresProvider: false, AllowedForRefund: false),
        new("BANK_TRANSFER", "Bank transfer", RequiresReference: true, RequiresProvider: false, AllowedForRefund: false),
        new("OTHER", "Other", RequiresReference: true, RequiresProvider: false, AllowedForRefund: false),
    ];
}

/// <summary>One seeded mode.</summary>
/// <param name="Code">The code, fixed for good.</param>
/// <param name="Name">The name on the button, corrected by the seeder when this changes.</param>
/// <param name="RequiresReference">The starting flag.</param>
/// <param name="RequiresProvider">The starting flag.</param>
/// <param name="AllowedForRefund">The starting flag.</param>
public sealed record SeededPaymentMode(string Code, string Name, bool RequiresReference, bool RequiresProvider, bool AllowedForRefund)
{
    /// <summary>The details a new mode is defined with.</summary>
    public PaymentModeDetails Details => new(Name, RequiresReference, RequiresProvider, AllowedForRefund);
}
