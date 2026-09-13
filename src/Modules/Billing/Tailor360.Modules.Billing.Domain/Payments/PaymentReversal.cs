using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>
/// The compensating record of a payment recorded in error — money that never cleared (E09-F03-3). The
/// payment's row is untouched (INV-PAY-01); this row says its allocations and its advance count for
/// nothing from here on, and the balance is recomputed from rows (INV-PAY-06). One per payment, and a
/// payment with a refund against it is not reversed: the refund already moved money the reversal would
/// say was never there.
/// </summary>
public sealed class PaymentReversal
{
    private PaymentReversal()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private PaymentReversal(Guid id, Guid organisationId, Guid branchId, Guid paymentId, string reason, DateTimeOffset now, Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        PaymentId = paymentId;
        Reason = reason;
        ReversedAt = now;
        ReversedBy = by;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch the payment was taken at.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The payment reversed.</summary>
    public Guid PaymentId { get; private set; }

    /// <summary>Why, as the trail records it.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>When.</summary>
    public DateTimeOffset ReversedAt { get; private set; }

    /// <summary>Who.</summary>
    public Guid? ReversedBy { get; private set; }

    /// <summary>Reverses a payment that has no reversal and no refund against it.</summary>
    public static Result<PaymentReversal> Of(Guid id, Payment payment, string? reason, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (payment.IsReversed)
        {
            return Result.Failure<PaymentReversal>(BillingErrors.PaymentAlreadyReversed);
        }

        if (!payment.RefundedFromAdvance.IsZero)
        {
            return Result.Failure<PaymentReversal>(BillingErrors.PaymentRefunded);
        }

        var checkedReason = Invoice.CheckReason(reason);
        if (checkedReason.IsFailure)
        {
            return Result.Failure<PaymentReversal>(checkedReason.Error);
        }

        return Result.Success(new PaymentReversal(id, payment.OrganisationId, payment.BranchId, payment.Id, reason!.Trim(), now, by));
    }
}
