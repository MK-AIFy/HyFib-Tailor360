namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>Change a payment mode. Every field is sent; the code is not, because it never changes.</summary>
/// <param name="Name">The name on the button.</param>
/// <param name="RequiresReference">Whether a payment must carry an external reference.</param>
/// <param name="RequiresProvider">Whether a payment goes through a provider intent.</param>
/// <param name="AllowedForRefund">Whether a refund may be paid through it.</param>
/// <param name="IsActive">Whether new payments may use it.</param>
/// <param name="BranchIds">The branches to restrict it to; empty or absent for every branch.</param>
public sealed record DescribePaymentModeRequest(
    string? Name,
    bool RequiresReference,
    bool RequiresProvider,
    bool AllowedForRefund,
    bool IsActive,
    IReadOnlyList<Guid>? BranchIds);

/// <summary>Open a cashier session.</summary>
/// <param name="OpeningFloat">The cash put in the drawer, in rupees, to the paisa.</param>
public sealed record OpenCashierSessionRequest(decimal OpeningFloat);

/// <summary>Close a cashier session against its count.</summary>
/// <param name="Denominations">The count sheet: one line per note or coin present.</param>
/// <param name="ModeTotals">What was counted per mode other than cash; a mode left out is counted as zero.</param>
/// <param name="Reason">Why the count differs from what the session should hold, where it does.</param>
public sealed record CloseCashierSessionRequest(
    IReadOnlyList<DenominationCountRequest>? Denominations,
    IReadOnlyList<ModeCountRequest>? ModeTotals,
    string? Reason);

/// <summary>One line of the count sheet.</summary>
/// <param name="Denomination">The note or coin, in rupees.</param>
/// <param name="Quantity">How many.</param>
public sealed record DenominationCountRequest(decimal Denomination, int Quantity);

/// <summary>What was counted for one mode.</summary>
/// <param name="ModeCode">The payment mode's code.</param>
/// <param name="Counted">The amount, to the paisa.</param>
public sealed record ModeCountRequest(string? ModeCode, decimal Counted);
