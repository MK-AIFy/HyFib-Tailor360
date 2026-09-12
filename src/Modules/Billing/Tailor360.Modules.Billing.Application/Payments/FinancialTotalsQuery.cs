using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Payments;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>
/// Answers <see cref="IFinancialTotalsQuery"/> from rows and nothing else (INV-PAY-06): the posted
/// invoices, their notes, the allocations against them and the advances held against the order, read
/// from one snapshot so that an allocation committing mid-read cannot be counted on one side and not the
/// other. No figure is cached and no other module computes one.
/// </summary>
/// <param name="invoices">The invoices.</param>
/// <param name="payments">The payments, allocations and advances.</param>
/// <param name="orders">What Billing knows about orders.</param>
public sealed class FinancialTotalsQuery(IInvoiceStore invoices, IPaymentStore payments, IOrderFactStore orders) : IFinancialTotalsQuery
{
    /// <inheritdoc />
    public Task<InvoiceBalanceSummary?> GetInvoiceBalanceAsync(Guid invoiceId, Guid organisationId, CancellationToken cancellationToken = default)
        => payments.ReadConsistentlyAsync(async token =>
        {
            var invoice = await invoices.FindAsync(invoiceId, organisationId, token);
            if (invoice is null || !invoice.IsPosted)
            {
                return null;
            }

            var allocated = await payments.AllocatedByInvoiceAsync([invoice.Id], token);
            var refunded = await payments.RefundedByInvoiceAsync([invoice.Id], token);
            return Summarise(invoice, InvoiceBalance.Of(invoice, allocated.GetValueOrDefault(invoice.Id, Money.Zero), refunded.GetValueOrDefault(invoice.Id, Money.Zero)));
        }, cancellationToken);

    /// <inheritdoc />
    public Task<OrderBalanceSummary?> GetOrderBalanceAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default)
        => payments.ReadConsistentlyAsync(async token =>
        {
            if (await orders.FindAsync(orderId, organisationId, token) is null)
            {
                return null;
            }

            return await ComposeAsync(orderId, organisationId, token);
        }, cancellationToken);

    /// <summary>The order's balance from the rows as one snapshot shows them: the invoices, their notes, the allocations and the advances.</summary>
    private async Task<OrderBalanceSummary> ComposeAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken)
    {
        var posted = await invoices.ListPostedForOrderAsync(orderId, organisationId, cancellationToken);
        var ids = posted.Select(invoice => invoice.Id).ToList();
        var allocated = await payments.AllocatedByInvoiceAsync(ids, cancellationToken);
        var refunded = await payments.RefundedByInvoiceAsync(ids, cancellationToken);
        var balances = posted.Select(invoice => Summarise(invoice, InvoiceBalance.Of(invoice, allocated.GetValueOrDefault(invoice.Id, Money.Zero), refunded.GetValueOrDefault(invoice.Id, Money.Zero)))).ToList();
        var unapplied = (await payments.ListForOrderAsync(orderId, organisationId, cancellationToken))
            .Aggregate(Money.Zero, (sum, payment) => sum + payment.UnappliedAdvance);

        return new OrderBalanceSummary(
            orderId,
            balances.Sum(balance => balance.Charges),
            balances.Sum(balance => balance.Credits),
            balances.Sum(balance => balance.Debits),
            balances.Sum(balance => balance.Allocated),
            balances.Sum(balance => balance.Refunds),
            unapplied.Amount,
            balances.Sum(balance => balance.Outstanding),
            posted.Count > 0 ? posted[0].Totals.GrandTotal.Currency : Money.IndianRupee,
            balances);
    }

    private static InvoiceBalanceSummary Summarise(Invoice invoice, InvoiceBalance balance)
        => new(
            invoice.Id, invoice.InvoiceNumber ?? string.Empty,
            balance.Charges.Amount, balance.Credits.Amount, balance.Debits.Amount, balance.Allocated.Amount, balance.Refunds.Amount, balance.Outstanding.Amount,
            balance.Charges.Currency, balance.Status.ToString());
}
