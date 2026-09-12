using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>Where an invoice stands against the money taken for it.</summary>
public enum InvoicePaidStatus
{
    /// <summary>Posted, owed in full, nothing allocated.</summary>
    Unpaid = 0,

    /// <summary>Something allocated, something still owed.</summary>
    PartlyPaid = 1,

    /// <summary>Nothing owed: the allocations and the credits cover the charges.</summary>
    Paid = 2,

    /// <summary>Cancelled: the credit note relieved it in full, and nothing was ever allocated.</summary>
    Cancelled = 3,
}

/// <summary>
/// The balance of one posted invoice, computed from rows and nowhere else (INV-PAY-06): posted charges
/// minus the credit notes, plus the debit notes, minus what has been allocated to it, plus what has
/// been refunded against it. Custody, Delivery and Reporting never compute this; they ask.
/// </summary>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="Charges">The invoice's grand total as posted.</param>
/// <param name="Credits">The sum of its credit notes, the cancellation's included.</param>
/// <param name="Debits">The sum of its debit notes.</param>
/// <param name="Allocated">What payments and advances have been allocated to it.</param>
/// <param name="Refunds">What has been refunded against it.</param>
/// <param name="Outstanding">Charges minus credits plus debits minus allocated plus refunds; never below zero.</param>
/// <param name="Refundable">What the invoice holds beyond what it charges — the same sum below zero, as a positive figure — and has not paid back; never below zero.</param>
/// <param name="Status">Where it stands.</param>
public sealed record InvoiceBalance(
    Guid InvoiceId,
    Money Charges,
    Money Credits,
    Money Debits,
    Money Allocated,
    Money Refunds,
    Money Outstanding,
    Money Refundable,
    InvoicePaidStatus Status)
{
    /// <summary>Computes the balance of a posted invoice given what has been allocated to it.</summary>
    /// <param name="invoice">The invoice, with its notes and its cancellation loaded.</param>
    /// <param name="allocated">The sum of the allocations against it.</param>
    /// <param name="refunds">The sum of the refunds paid back against it.</param>
    public static InvoiceBalance Of(Invoice invoice, Money allocated, Money refunds)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var charges = invoice.Totals.GrandTotal;
        var credits = Money.Zero;
        var debits = Money.Zero;
        foreach (var note in invoice.Notes)
        {
            if (note.Kind == AdjustmentNoteKind.Credit)
            {
                credits += note.Totals.GrandTotal;
            }
            else
            {
                debits += note.Totals.GrandTotal;
            }
        }

        var owed = charges - credits + debits - allocated + refunds;
        var outstanding = owed.IsNegative ? Money.Zero : owed;
        var refundable = owed.IsNegative ? -owed : Money.Zero;

        // Cancelled means settled, not merely "the credit note was issued and nothing is currently
        // allocated": a payment that funded this invoice can be reversed after the invoice was cancelled
        // and its surplus refunded, which zeroes `allocated` (a reversed payment's allocations count for
        // nothing) while `refunds` still holds what was already paid back — reopening a debt the credit
        // note no longer covers. `outstanding.IsZero` is the guard: a cancelled invoice with money still
        // owed is Unpaid or PartlyPaid, never Cancelled, whatever emptied its allocation.
        var status = invoice.IsCancelled && allocated.IsZero && outstanding.IsZero
            ? InvoicePaidStatus.Cancelled
            : outstanding.IsZero
                ? InvoicePaidStatus.Paid
                : allocated.IsZero && refunds.IsZero
                    ? InvoicePaidStatus.Unpaid
                    : InvoicePaidStatus.PartlyPaid;

        return new InvoiceBalance(invoice.Id, charges, credits, debits, allocated, refunds, outstanding, refundable, status);
    }
}
