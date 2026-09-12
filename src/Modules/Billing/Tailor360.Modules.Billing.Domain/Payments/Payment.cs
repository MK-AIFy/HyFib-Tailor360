using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>Where a payment is in its one-way life. A reversal or a refund is a new record (E09-F03-3).</summary>
public enum PaymentStatus
{
    /// <summary>Taken and recorded; the only status this slice writes.</summary>
    Recorded = 0,
}

/// <summary>
/// Money taken at the counter against an order: in one mode, in the cashier's open session, allocated
/// at once to the order's posted invoices oldest first (INV-PAY-04), the rest held as an advance until
/// an invoice posts (INV-PAY-05). Append-only (INV-PAY-01): nothing on the row moves after recording, an
/// allocation is only ever added, and a mistake is a compensating record. There is no row version
/// because there is no update path.
/// </summary>
public sealed class Payment
{
    /// <summary>The largest amount a payment may carry: what a <c>numeric(18, 4)</c> column holds.</summary>
    public const decimal MaximumAmount = CashierSession.MaximumAmount;

    /// <summary>The longest client key kept with the row.</summary>
    public const int MaximumClientKeyLength = 64;

    private readonly List<PaymentAllocation> _allocations = [];

    private Payment()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private Payment(
        Guid id, Guid organisationId, Guid branchId, Guid cashierSessionId, Guid cashierId, Guid customerId, Guid orderId,
        string modeCode, Money amount, string? reference, string? clientKey, DateTimeOffset now, Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CashierSessionId = cashierSessionId;
        CashierId = cashierId;
        CustomerId = customerId;
        OrderId = orderId;
        ModeCode = modeCode;
        Amount = amount;
        Reference = reference;
        ClientKey = clientKey;
        Status = PaymentStatus.Recorded;
        RecordedAt = now;
        RecordedBy = by;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch it was taken at.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The open cashier session it was recorded in (INV-CSH-02).</summary>
    public Guid CashierSessionId { get; private set; }

    /// <summary>The cashier accountable for the money.</summary>
    public Guid CashierId { get; private set; }

    /// <summary>The customer, the order's.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The order it was taken against.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The payment mode's code.</summary>
    public string ModeCode { get; private set; } = string.Empty;

    /// <summary>How much.</summary>
    public Money Amount { get; private set; }

    /// <summary>The external reference the mode required — a transaction identifier, never a card number.</summary>
    public string? Reference { get; private set; }

    /// <summary>The idempotency key the request arrived with (INV-PAY-02), the row's own guard against a twin.</summary>
    public string? ClientKey { get; private set; }

    /// <summary>Recorded, always, in this slice.</summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Who.</summary>
    public Guid? RecordedBy { get; private set; }

    /// <summary>Where the money went, in the order it went there.</summary>
    public IReadOnlyList<PaymentAllocation> Allocations => _allocations;

    /// <summary>The remainder held at recording, or null when every rupee found an invoice.</summary>
    public Advance? Advance { get; private set; }

    /// <summary>What has been allocated in all.</summary>
    public Money Allocated => _allocations.Aggregate(Money.Zero, (sum, allocation) => sum + allocation.Amount);

    /// <summary>What of the advance is still held: its amount minus the allocations that name it.</summary>
    public Money UnappliedAdvance
        => Advance is null
            ? Money.Zero
            : Advance.Amount - _allocations.Where(allocation => allocation.AdvanceId == Advance.Id).Aggregate(Money.Zero, (sum, allocation) => sum + allocation.Amount);

    /// <summary>
    /// What a payment must be before anything is looked up for it: a well-formed mode code, a positive
    /// amount to the paisa in rupees within the column, and a reference that is not a card number. The
    /// handler asks this first so a malformed request is answered before a missing session is.
    /// </summary>
    public static Result Validate(string modeCode, Money amount, string? reference)
    {
        if (!BillingCode.IsWellFormed(modeCode))
        {
            return Result.Failure(BillingErrors.CodeNotWellFormed("modeCode"));
        }

        if (amount.IsNegative || amount.IsZero || decimal.Round(amount.Amount, Money.DocumentScale) != amount.Amount || amount.Amount > MaximumAmount)
        {
            return Result.Failure(BillingErrors.AmountNotWellFormed("amount"));
        }

        if (!string.Equals(amount.Currency, Money.IndianRupee, StringComparison.Ordinal))
        {
            return Result.Failure(BillingErrors.AmountNotWellFormed("amount"));
        }

        var checkedReference = PaymentReferences.Check(reference, "reference");
        return checkedReference.IsFailure ? Result.Failure(checkedReference.Error) : Result.Success();
    }

    /// <summary>Records a payment: a positive amount to the paisa, in rupees, under a mode whose rule for a reference the caller has already applied.</summary>
    public static Result<Payment> Record(
        Guid id, Guid organisationId, Guid branchId, Guid cashierSessionId, Guid cashierId, Guid customerId, Guid orderId,
        string modeCode, Money amount, string? reference, string? clientKey, DateTimeOffset now, Guid? by)
    {
        var valid = Validate(modeCode, amount, reference);
        if (valid.IsFailure)
        {
            return Result.Failure<Payment>(valid.Error);
        }

        var checkedReference = PaymentReferences.Check(reference, "reference");

        var key = clientKey?.Trim();
        if (key is { Length: > MaximumClientKeyLength })
        {
            key = key[..MaximumClientKeyLength];
        }

        return Result.Success(new Payment(
            id, organisationId, branchId, cashierSessionId, cashierId, customerId, orderId,
            modeCode, amount, checkedReference.Value, string.IsNullOrEmpty(key) ? null : key, now, by));
    }

    /// <summary>
    /// Allocates the payment to the invoices given, in the order given — the caller passes the order's
    /// posted invoices oldest first with what each still owes — until the money or the invoices run out.
    /// What is left is held as an advance under <paramref name="advanceId"/>. Called once, at recording.
    /// </summary>
    /// <param name="outstanding">The invoices and what each still owes, oldest first.</param>
    /// <param name="ids">Identifiers for the allocation rows, one per invoice at most, then one for the advance.</param>
    /// <param name="advanceId">The identifier the advance takes if any money is left.</param>
    /// <param name="now">When.</param>
    /// <param name="by">Who.</param>
    public Result<IReadOnlyList<PaymentAllocation>> AllocateAtRecording(
        IReadOnlyList<(Guid InvoiceId, Money Outstanding)> outstanding,
        Func<Guid> ids,
        Guid advanceId,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(outstanding);
        ArgumentNullException.ThrowIfNull(ids);

        if (_allocations.Count > 0 || Advance is not null)
        {
            return Result.Failure<IReadOnlyList<PaymentAllocation>>(BillingErrors.PaymentAlreadyAllocated);
        }

        var remaining = Amount;
        var added = new List<PaymentAllocation>();
        foreach (var (invoiceId, owed) in outstanding)
        {
            if (remaining.IsZero)
            {
                break;
            }

            if (owed.IsZero || owed.IsNegative)
            {
                continue;
            }

            var applied = owed < remaining ? owed : remaining;
            var allocation = PaymentAllocation.Of(ids(), Id, invoiceId, null, applied, AllocationKind.Automatic, now, by);
            _allocations.Add(allocation);
            added.Add(allocation);
            remaining -= applied;
        }

        if (!remaining.IsZero)
        {
            Advance = Advance.Of(advanceId, Id, remaining, now);
        }

        return Result.Success<IReadOnlyList<PaymentAllocation>>(added);
    }

    /// <summary>
    /// Applies part of the held advance to an invoice, by the rule when the order posts one or by hand
    /// under authority. Never more than is held, never more than the invoice owes.
    /// </summary>
    public Result<PaymentAllocation> ApplyAdvance(Guid allocationId, Guid invoiceId, Money outstanding, Money amount, AllocationKind kind, DateTimeOffset now, Guid? by)
    {
        if (Advance is null)
        {
            return Result.Failure<PaymentAllocation>(BillingErrors.NoAdvanceHeld);
        }

        if (kind == AllocationKind.Automatic)
        {
            return Result.Failure<PaymentAllocation>(BillingErrors.AllocationKindNotForAnAdvance);
        }

        if (amount.IsNegative || amount.IsZero || decimal.Round(amount.Amount, Money.DocumentScale) != amount.Amount)
        {
            return Result.Failure<PaymentAllocation>(BillingErrors.AmountNotWellFormed("amount"));
        }

        if (amount > UnappliedAdvance)
        {
            return Result.Failure<PaymentAllocation>(BillingErrors.AdvanceExceeded("amount"));
        }

        if (amount > outstanding)
        {
            return Result.Failure<PaymentAllocation>(BillingErrors.AllocationExceedsInvoice("amount"));
        }

        var allocation = PaymentAllocation.Of(allocationId, Id, invoiceId, Advance.Id, amount, kind, now, by);
        _allocations.Add(allocation);
        return Result.Success(allocation);
    }
}
