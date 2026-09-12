namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>One line of the count sheet as the cashier submits it.</summary>
/// <param name="Denomination">The note or coin, in rupees.</param>
/// <param name="Quantity">How many.</param>
public sealed record DenominationCount(decimal Denomination, int Quantity);

/// <summary>What the cashier counted for one mode other than cash, as the cashier submits it.</summary>
/// <param name="ModeCode">The payment mode.</param>
/// <param name="Counted">The amount, to the paisa.</param>
public sealed record ModeCount(string ModeCode, decimal Counted);
