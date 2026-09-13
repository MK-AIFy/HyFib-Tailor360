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

/// <summary>Record a payment against an order in the caller's open cashier session.</summary>
/// <param name="OrderId">The order the money is taken against; the customer is the order's.</param>
/// <param name="ModeCode">The payment mode's code, one of the modes available at the branch.</param>
/// <param name="Amount">How much, in rupees to the paisa.</param>
/// <param name="Reference">The terminal's or the bank's reference, where the mode requires one. Never a card number.</param>
public sealed record RecordPaymentRequest(Guid OrderId, string? ModeCode, decimal Amount, string? Reference);

/// <summary>Apply part of a payment's held advance to a posted invoice of the same order, by hand.</summary>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="Amount">How much, in rupees to the paisa; never more than is held or than the invoice owes.</param>
/// <param name="Reason">Why the rule is not being left to do it.</param>
public sealed record AllocateAdvanceRequest(Guid InvoiceId, decimal Amount, string? Reason);

/// <summary>Reverse a payment recorded in error.</summary>
/// <param name="Reason">Why the money never cleared.</param>
public sealed record ReversePaymentRequest(string? Reason);

/// <summary>Pay money back to the customer against one source.</summary>
/// <param name="PaymentId">The payment whose advance is paid back; omit when the source is an invoice.</param>
/// <param name="InvoiceId">The posted invoice whose surplus is paid back; omit when the source is an advance.</param>
/// <param name="ModeCode">The mode it is paid through, one allowed for refunds.</param>
/// <param name="Amount">How much, in rupees to the paisa; never more than the source still holds.</param>
/// <param name="Reference">The reference the mode requires, where it does.</param>
/// <param name="Reason">Why.</param>
public sealed record RecordRefundRequest(Guid? PaymentId, Guid? InvoiceId, string? ModeCode, decimal Amount, string? Reference, string? Reason);

/// <summary>Approve a cashier session's variance, by someone other than who closed it.</summary>
/// <param name="Reason">Why the variance is accepted.</param>
public sealed record ApproveReconciliationRequest(string? Reason);

/// <summary>Approve a single-use dispatch exception for named jobs of an order.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="JobIds">Exactly the garment jobs it covers; every one must be a live job of the order.</param>
/// <param name="MaxOutstandingAmount">The most the order may still owe when it is consumed, in rupees to the paisa.</param>
/// <param name="ReasonCode">The configured reason code.</param>
/// <param name="ReasonText">Why, in the approver's own words.</param>
/// <param name="ExpiresAt">When it stops being consumable; at most 72 hours from now.</param>
public sealed record CreateDispatchExceptionRequest(
    Guid OrderId, IReadOnlyList<Guid>? JobIds, decimal MaxOutstandingAmount, string? ReasonCode, string? ReasonText, DateTimeOffset ExpiresAt);
