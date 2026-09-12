using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Billing.Contracts.Events;

/// <summary>A payment was taken and recorded. Identifiers and figures only; the reference stays on the row.</summary>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="AggregateId">The payment.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch it was taken at.</param>
/// <param name="CashierSessionId">The session it was recorded in.</param>
/// <param name="CustomerId">The customer, by identifier.</param>
/// <param name="OrderId">The order it was taken against.</param>
/// <param name="ModeCode">The payment mode.</param>
/// <param name="Amount">How much, to the paisa.</param>
/// <param name="Currency">The currency.</param>
public sealed record PaymentRecorded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CashierSessionId,
    Guid CustomerId,
    Guid OrderId,
    string ModeCode,
    decimal Amount,
    string Currency)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.payment-recorded.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>Money from a payment was applied to an invoice: at recording, from an advance, or by hand.</summary>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="AggregateId">The payment.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch.</param>
/// <param name="AllocationId">The allocation row.</param>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="AdvanceId">The advance it was applied from, where the money was held first.</param>
/// <param name="Amount">How much, to the paisa.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Kind">Automatic, AdvanceApplied or Manual.</param>
public sealed record PaymentAllocated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid AllocationId,
    Guid InvoiceId,
    Guid? AdvanceId,
    decimal Amount,
    string Currency,
    string Kind)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.payment-allocated.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>Part of a payment found no posted invoice and is held against the order until one posts.</summary>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="AggregateId">The advance.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch.</param>
/// <param name="PaymentId">The payment it is the remainder of.</param>
/// <param name="CustomerId">The customer, by identifier.</param>
/// <param name="OrderId">The order it is held against.</param>
/// <param name="Amount">How much is held, to the paisa.</param>
/// <param name="Currency">The currency.</param>
public sealed record AdvanceReceived(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid PaymentId,
    Guid CustomerId,
    Guid OrderId,
    decimal Amount,
    string Currency)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.advance-received.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>Part of a held advance was applied to a posted invoice, by the rule or by hand.</summary>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="AggregateId">The advance.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch.</param>
/// <param name="PaymentId">The payment the advance belongs to.</param>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="Amount">How much was applied, to the paisa.</param>
/// <param name="Remaining">How much of the advance is still held.</param>
/// <param name="Currency">The currency.</param>
public sealed record AdvanceApplied(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid PaymentId,
    Guid InvoiceId,
    decimal Amount,
    decimal Remaining,
    string Currency)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.advance-applied.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>An invoice's derived paid status moved: Unpaid, PartlyPaid, Paid or Cancelled.</summary>
/// <param name="EventId">Identity of this occurrence.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="AggregateId">The invoice.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch.</param>
/// <param name="OrderId">The order.</param>
/// <param name="PreviousStatus">Where it stood.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Outstanding">What it still owes, to the paisa.</param>
/// <param name="Currency">The currency.</param>
public sealed record InvoicePaidStatusChanged(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid OrderId,
    string PreviousStatus,
    string Status,
    decimal Outstanding,
    string Currency)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.invoice-paid-status-changed.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
