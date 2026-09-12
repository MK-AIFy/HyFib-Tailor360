using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>Payments, their allocations and their advances, as persistence answers for them.</summary>
public interface IPaymentStore
{
    /// <summary>One payment with its allocations and its advance, or null when it is not the organisation's.</summary>
    Task<Payment?> FindAsync(Guid paymentId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The payments taken against an order, oldest first, with their allocations and advances.</summary>
    Task<IReadOnlyList<Payment>> ListForOrderAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>What has been allocated to each of the invoices named, from every payment; an invoice with nothing is absent.</summary>
    Task<IReadOnlyDictionary<Guid, Money>> AllocatedByInvoiceAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default);

    /// <summary>What a cashier session took per mode, in all: the expected totals a close counts against.</summary>
    Task<IReadOnlyDictionary<string, Money>> TakenByModeAsync(Guid cashierSessionId, CancellationToken cancellationToken = default);

    /// <summary>Tracks a new payment.</summary>
    void Add(Payment payment);

    /// <summary>Tracks a new receipt, issued with its payment and committed with it.</summary>
    void AddReceipt(Receipt receipt);

    /// <summary>One receipt, or null when it is not the organisation's.</summary>
    Task<Receipt?> FindReceiptAsync(Guid receiptId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The receipt of a payment, or null when the payment has none or is not the organisation's.</summary>
    Task<Receipt?> FindReceiptForPaymentAsync(Guid paymentId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The receipt an <c>R-</c> payload was printed on, or null.</summary>
    Task<Receipt?> FindReceiptByBarcodeAsync(string barcodePayload, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draws the next receipt number inside the current transaction, holding the sequence's row until the
    /// transaction ends, so two receipts of one branch are numbered one after the other and a payment that
    /// rolls back returns its number (gapless, as the invoice is). Outside a transaction it throws.
    /// </summary>
    Task<long> AllocateAsync(string sequenceKey, string scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Holds every invoice row of the order against change for the rest of the current transaction, so
    /// two payments against one order, or a payment racing the rule that applies an advance, serialise
    /// on the invoices' allocation state (INV-PAY-03). A draft being posted is held by its own posting,
    /// so a payment that arrives during the post waits and then sees it posted. Outside a transaction
    /// it throws.
    /// </summary>
    Task LockOrderInvoicesAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same hold, taken through one invoice for a caller that knows the invoice and not yet the
    /// order: the rows are held before the invoice is read. Answers the order, or null when the invoice
    /// is not known. Outside a transaction it throws.
    /// </summary>
    Task<Guid?> LockOrderInvoicesOfAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the work in one transaction that first takes the lock above. A refused outcome rolls the
    /// attempt back. The work reads everything it changes, because a retry replays it from an empty
    /// change tracker; when the commit's outcome is unknown, <paramref name="committed"/> says whether
    /// the attempt is done rather than the work running again over money already recorded.
    /// </summary>
    Task<Result<TOutcome>> InAllocationTransactionAsync<TOutcome>(
        Guid orderId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        Func<CancellationToken, Task<bool>> committed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs reads over one snapshot of the database (a repeatable-read transaction that writes nothing),
    /// so a balance composed from several tables — invoices, notes, allocations, advances — is composed
    /// from one instant, and an allocation committing between two of its reads cannot make money vanish.
    /// </summary>
    Task<TResult> ReadConsistentlyAsync<TResult>(Func<CancellationToken, Task<TResult>> read, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits. A reference recorded before in the same mode comes back as
    /// <c>billing.payment-reference-duplicated</c>; the cashier's own client key recorded before as
    /// <c>billing.payment-duplicated</c>; a receipt number or payload already taken as
    /// <c>billing.receipt-number-taken</c>, which no caller should see because the sequence row is held.
    /// </summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
