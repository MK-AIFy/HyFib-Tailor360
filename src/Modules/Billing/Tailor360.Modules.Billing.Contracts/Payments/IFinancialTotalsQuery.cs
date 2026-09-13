namespace Tailor360.Modules.Billing.Contracts.Payments;

/// <summary>
/// The money position, answered by Billing alone (INV-PAY-06, `INV-ORD-07`): what an invoice or an order
/// still owes, computed from posted charges, credit and debit notes, allocations and refunds, plus the
/// advances held against the order and not yet applied. Custody, Delivery, Orders and Reporting display
/// what this returns and compute nothing.
/// </summary>
public interface IFinancialTotalsQuery
{
    /// <summary>The balance of one posted invoice, or null when it is not posted or not the organisation's.</summary>
    Task<InvoiceBalanceSummary?> GetInvoiceBalanceAsync(Guid invoiceId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The balance of an order across its posted invoices, or null when Billing has never heard of the order.</summary>
    Task<OrderBalanceSummary?> GetOrderBalanceAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default);
}

/// <summary>Where one invoice stands.</summary>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="InvoiceNumber">Its display number.</param>
/// <param name="Charges">The grand total as posted.</param>
/// <param name="Credits">The credit notes against it, the cancellation's included.</param>
/// <param name="Debits">The debit notes against it.</param>
/// <param name="Allocated">The payments and advances applied to it.</param>
/// <param name="Refunds">The refunds against it; zero until E09-F03-3.</param>
/// <param name="Outstanding">What it still owes; never below zero.</param>
/// <param name="Currency">The currency of every figure.</param>
/// <param name="Status">Unpaid, PartlyPaid, Paid or Cancelled.</param>
public sealed record InvoiceBalanceSummary(
    Guid InvoiceId,
    string InvoiceNumber,
    decimal Charges,
    decimal Credits,
    decimal Debits,
    decimal Allocated,
    decimal Refunds,
    decimal Outstanding,
    string Currency,
    string Status);

/// <summary>Where an order stands across its posted invoices.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="Charges">The posted invoices' grand totals.</param>
/// <param name="Credits">Their credit notes.</param>
/// <param name="Debits">Their debit notes.</param>
/// <param name="Allocated">What has been allocated to them.</param>
/// <param name="Refunds">What has been refunded against them.</param>
/// <param name="UnappliedAdvances">What is held against the order and not yet applied.</param>
/// <param name="Outstanding">What the invoices still owe in all; never below zero.</param>
/// <param name="Currency">The currency of every figure.</param>
/// <param name="Invoices">Each posted invoice's own balance, oldest first.</param>
public sealed record OrderBalanceSummary(
    Guid OrderId,
    decimal Charges,
    decimal Credits,
    decimal Debits,
    decimal Allocated,
    decimal Refunds,
    decimal UnappliedAdvances,
    decimal Outstanding,
    string Currency,
    IReadOnlyList<InvoiceBalanceSummary> Invoices);
