namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>
/// The rupee notes and coins a count sheet may name. The notes are the ones the plan lists for the
/// sheet (<c>docs/IMPLEMENTATION_PLAN.md</c>, the cashier open and close screen: ₹2000/500/200/100/50/20/10
/// "and coins"); the coins are the rupee's circulating ones. A value not in this list is refused rather
/// than summed, so a typo in a denomination cannot balance a drawer.
/// </summary>
public static class CashDenominations
{
    /// <summary>Largest first, as a sheet is laid out.</summary>
    public static IReadOnlyList<decimal> All { get; } = [2000m, 500m, 200m, 100m, 50m, 20m, 10m, 5m, 2m, 1m];

    /// <summary>Whether a value is one the sheet may carry.</summary>
    public static bool IsKnown(decimal value) => All.Contains(value);
}
