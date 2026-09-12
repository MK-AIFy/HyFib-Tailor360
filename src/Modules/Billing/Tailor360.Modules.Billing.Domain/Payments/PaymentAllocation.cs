using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>How an allocation came to be.</summary>
public enum AllocationKind
{
    /// <summary>The deterministic rule at recording: oldest posted invoice of the order first.</summary>
    Automatic = 0,

    /// <summary>An advance applied by the same rule when the order posted an invoice.</summary>
    AdvanceApplied = 1,

    /// <summary>An advance applied by hand, under <c>payments.allocate_manual</c>.</summary>
    Manual = 2,
}

/// <summary>
/// Money from one payment applied to one invoice. Append-only: an allocation is never edited; a mistake is
/// reversed by a compensating record (E09-F03-3).
/// </summary>
public sealed class PaymentAllocation
{
    private PaymentAllocation()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private PaymentAllocation(Guid id, Guid paymentId, Guid invoiceId, Guid? advanceId, Money amount, AllocationKind kind, DateTimeOffset now, Guid? by)
    {
        Id = id;
        PaymentId = paymentId;
        InvoiceId = invoiceId;
        AdvanceId = advanceId;
        Amount = amount;
        Kind = kind;
        AllocatedAt = now;
        AllocatedBy = by;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The payment.</summary>
    public Guid PaymentId { get; private set; }

    /// <summary>The invoice.</summary>
    public Guid InvoiceId { get; private set; }

    /// <summary>The advance it was applied from, where the money was held first.</summary>
    public Guid? AdvanceId { get; private set; }

    /// <summary>How much.</summary>
    public Money Amount { get; private set; }

    /// <summary>How it came to be.</summary>
    public AllocationKind Kind { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset AllocatedAt { get; private set; }

    /// <summary>Who; null for the rule applying an advance when an invoice posted.</summary>
    public Guid? AllocatedBy { get; private set; }

    internal static PaymentAllocation Of(Guid id, Guid paymentId, Guid invoiceId, Guid? advanceId, Money amount, AllocationKind kind, DateTimeOffset now, Guid? by)
        => new(id, paymentId, invoiceId, advanceId, amount, kind, now, by);
}

/// <summary>
/// The part of a payment that no posted invoice could take at recording, held against the order and
/// its customer until an invoice posts (INV-PAY-05). Append-only: what remains unapplied is the amount
/// minus the allocations that name this advance, never a column that moves.
/// </summary>
public sealed class Advance
{
    private Advance()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private Advance(Guid id, Guid paymentId, Money amount, DateTimeOffset now)
    {
        Id = id;
        PaymentId = paymentId;
        Amount = amount;
        ReceivedAt = now;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The payment it is the remainder of.</summary>
    public Guid PaymentId { get; private set; }

    /// <summary>How much was held.</summary>
    public Money Amount { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    internal static Advance Of(Guid id, Guid paymentId, Money amount, DateTimeOffset now) => new(id, paymentId, amount, now);
}
