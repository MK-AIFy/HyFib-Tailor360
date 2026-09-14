using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>One posted invoice that still owes money, as this read found it.</summary>
/// <param name="Invoice">The invoice, its notes and its cancellation loaded.</param>
/// <param name="Balance">Its balance, computed the one way <see cref="InvoiceBalance.Of"/> computes it (INV-PAY-06).</param>
public sealed record OutstandingBalanceRow(Invoice Invoice, InvoiceBalance Balance);

/// <summary>One page of a branch's outstanding balances.</summary>
/// <param name="Rows">The posted invoices found with money still owed.</param>
/// <param name="NextCursor">
/// Where the next request should resume scanning the branch's posted invoices, or null once they are
/// exhausted. Non-null does not mean <paramref name="Rows"/> is non-empty: the scan bound can be
/// reached, or a source page can settle, before a single outstanding row is found.
/// </param>
public sealed record OutstandingBalancePage(IReadOnlyList<OutstandingBalanceRow> Rows, string? NextCursor);

/// <summary>
/// Answers "what does this branch still owe" (#421), the aggregate #216 asked a decision on rather
/// than the client's own fan-out: a page of <c>ListInvoices</c> followed by one <c>GetOrderBalance</c>
/// per invoice on it. This reads the same rows through the same stores instead —
/// <see cref="IInvoiceStore.ListPostedWithNotesAsync"/> a source page at a time (notes loaded, unlike
/// the summary <c>ListInvoices</c> itself reads, because <see cref="InvoiceBalance.Of"/> needs them),
/// one <see cref="IPaymentStore.AllocatedByInvoiceAsync"/> and one
/// <see cref="IPaymentStore.RefundedByInvoiceAsync"/> per source page — and keeps only what
/// <see cref="InvoiceBalance.Of"/> says still owes. No new formula: the figure this answers for one
/// invoice is the same figure <c>GetOrderBalance</c> answers for it, from the same rows.
/// </summary>
/// <param name="invoices">The invoice store, paged newest-updated first.</param>
/// <param name="payments">Allocations, refunds and the snapshot read opens.</param>
public sealed class OutstandingBalanceQuery(IInvoiceStore invoices, IPaymentStore payments)
{
    /// <summary>
    /// How many source pages of posted invoices one request may scan before it hands back a cursor
    /// rather than keep looking: at <see cref="InvoiceListQuery.MaximumLimit"/> per page, at most 500
    /// invoices and 30 queries (the list, the allocations and the refunds, per page) scanned by one
    /// request — the bound protects the "API p95, reads" row of
    /// docs/nfr/capacity-and-performance.md section 3.3, line 220. An engineering bound on one
    /// request's work, not a product number: it changes no user-visible rule, only how much scanning
    /// one request may do before returning a cursor.
    /// </summary>
    private const int MaximumSourcePagesPerRequest = 10;

    /// <summary>
    /// The branch's posted invoices that still owe money, newest-updated first. Reads inside one
    /// snapshot (INV-PAY-06), so an allocation committing mid-read cannot make an invoice's balance
    /// disagree with itself between two rows of the same answer.
    /// </summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchId">The branch whose posted invoices are read.</param>
    /// <param name="cursor">
    /// Where to resume, from a previous page's <see cref="OutstandingBalancePage.NextCursor"/>, or
    /// null for the first page. A cursor this query never issued is ignored and the first page is
    /// returned — <see cref="IInvoiceStore.ListPostedWithNotesAsync"/>'s own behaviour, the same
    /// <c>ListInvoices</c> already has.
    /// </param>
    /// <param name="limit">Stop once at least this many outstanding rows have been found, the branch's posted invoices are exhausted, or the scan bound is reached — whichever comes first.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<OutstandingBalancePage> GetAsync(
        Guid organisationId, Guid branchId, string? cursor, int limit, CancellationToken cancellationToken = default)
        => payments.ReadConsistentlyAsync(async token =>
        {
            var rows = new List<OutstandingBalanceRow>();
            var sourceCursor = cursor;

            // Fills the page rather than filtering one: a source page of posted invoices that are all
            // settled yields no rows on its own, and stopping there would show the reader an empty
            // table with a Show more under it before an unpaid invoice further back ever appeared. So
            // this loops whole source pages until it has `limit` rows or the source is exhausted, and
            // returns the source cursor it stopped at — `NextCursor` is null only when the branch's
            // posted invoices are exhausted, which is what makes the screen's empty state honest.
            for (var scanned = 0; scanned < MaximumSourcePagesPerRequest; scanned++)
            {
                var page = await invoices.ListPostedWithNotesAsync(
                    new InvoiceListQuery(organisationId, branchId, InvoiceStatus.Posted, sourceCursor, InvoiceListQuery.MaximumLimit),
                    token);

                if (page.Invoices.Count > 0)
                {
                    var ids = page.Invoices.Select(invoice => invoice.Id).ToList();
                    var allocated = await payments.AllocatedByInvoiceAsync(ids, token);
                    var refunded = await payments.RefundedByInvoiceAsync(ids, token);

                    foreach (var invoice in page.Invoices)
                    {
                        var balance = InvoiceBalance.Of(
                            invoice, allocated.GetValueOrDefault(invoice.Id, Money.Zero), refunded.GetValueOrDefault(invoice.Id, Money.Zero));
                        if (!balance.Outstanding.IsZero)
                        {
                            rows.Add(new OutstandingBalanceRow(invoice, balance));
                        }
                    }
                }

                sourceCursor = page.NextCursor;
                if (rows.Count >= limit || sourceCursor is null)
                {
                    break;
                }
            }

            return new OutstandingBalancePage(rows, sourceCursor);
        }, cancellationToken);
}
