using Tailor360.Modules.Billing.Domain.Payments;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>A payment mode as an administrator sees it.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Code">The code, fixed for good.</param>
/// <param name="Name">The name on the button.</param>
/// <param name="RequiresReference">Whether a payment must carry an external reference.</param>
/// <param name="RequiresProvider">Whether a payment goes through a provider intent.</param>
/// <param name="AllowedForRefund">Whether a refund may be paid through it.</param>
/// <param name="IsActive">Whether new payments may use it.</param>
/// <param name="BranchIds">The branches it is restricted to; empty means every branch.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public sealed record PaymentModePayload(
    Guid Id,
    string Code,
    string Name,
    bool RequiresReference,
    bool RequiresProvider,
    bool AllowedForRefund,
    bool IsActive,
    IReadOnlyList<Guid> BranchIds,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a mode.</summary>
    public static PaymentModePayload From(PaymentMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);

        return new PaymentModePayload(
            mode.Id, mode.Code, mode.Name, mode.RequiresReference, mode.RequiresProvider, mode.AllowedForRefund, mode.IsActive,
            mode.Branches.Select(branch => branch.BranchId).OrderBy(id => id).ToList(), mode.UpdatedAt);
    }
}

/// <summary>A cashier session: its float, and once closed, its counts and what they came to.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="BranchId">The branch whose drawer it is.</param>
/// <param name="CashierId">The cashier accountable for it.</param>
/// <param name="Status">Open or Closed.</param>
/// <param name="OpeningFloat">The cash put in the drawer at opening.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="ClosedAt">When it closed; null while open.</param>
/// <param name="ClosedBy">Who closed it; null while open.</param>
/// <param name="ExpectedTotal">Over every mode, what the session should have held; zero while open.</param>
/// <param name="CountedTotal">Over every mode, what was counted; zero while open.</param>
/// <param name="Variance">Counted minus expected; zero while open.</param>
/// <param name="VarianceReason">Why the count differs, where it does.</param>
/// <param name="Currency">The currency of every figure on the session.</param>
/// <param name="Denominations">The count sheet, written at close.</param>
/// <param name="ModeTotals">Expected against counted per mode, written at close.</param>
public sealed record CashierSessionPayload(
    Guid Id,
    Guid BranchId,
    Guid CashierId,
    string Status,
    decimal OpeningFloat,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    Guid? ClosedBy,
    decimal ExpectedTotal,
    decimal CountedTotal,
    decimal Variance,
    string? VarianceReason,
    string Currency,
    IReadOnlyList<DenominationCountPayload> Denominations,
    IReadOnlyList<ModeTotalPayload> ModeTotals)
{
    /// <summary>Projects a session.</summary>
    public static CashierSessionPayload From(CashierSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new CashierSessionPayload(
            session.Id, session.BranchId, session.CashierId, session.Status.ToString(),
            session.OpeningFloat.Amount, session.OpenedAt, session.ClosedAt, session.ClosedBy,
            session.ExpectedTotal.Amount, session.CountedTotal.Amount, session.Variance.Amount, session.VarianceReason,
            session.OpeningFloat.Currency,
            session.Counts.OrderByDescending(count => count.Denomination).Select(count => new DenominationCountPayload(count.Denomination, count.Quantity, count.Value)).ToList(),
            session.ModeTotals.OrderBy(total => total.ModeCode, StringComparer.Ordinal).Select(total => new ModeTotalPayload(total.ModeCode, total.Expected, total.Counted, total.Variance)).ToList());
    }
}

/// <summary>One line of the count sheet.</summary>
/// <param name="Denomination">The note or coin, in rupees.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Value">What the line is worth.</param>
public sealed record DenominationCountPayload(decimal Denomination, int Quantity, decimal Value);

/// <summary>One mode's expected against counted.</summary>
/// <param name="ModeCode">The payment mode.</param>
/// <param name="Expected">What the session should hold in it.</param>
/// <param name="Counted">What was counted.</param>
/// <param name="Variance">Counted minus expected.</param>
public sealed record ModeTotalPayload(string ModeCode, decimal Expected, decimal Counted, decimal Variance);
