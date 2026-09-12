using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.Modules.Billing.Contracts.Events;

/// <summary>
/// An invoice was posted: numbered, frozen, and from now on the document the customer owes against.
/// Published through the outbox in the transaction that posted it, so a consumer that hears it can rely
/// on the row (#154). Carries identifiers and the figures a consumer needs to react — never the customer's
/// details and never the barcode payload, which is resolved by its own route.
/// </summary>
/// <param name="EventId">The event's own identifier.</param>
/// <param name="OccurredAt">When it was posted.</param>
/// <param name="AggregateId">The invoice.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch that issued it.</param>
/// <param name="CustomerId">The customer it is issued to.</param>
/// <param name="OrderId">The order it charges for.</param>
/// <param name="InvoiceNumber">The display number, <c>INV-&lt;branch&gt;-&lt;FY&gt;-000001</c>.</param>
/// <param name="GrandTotal">What the customer owes, in <paramref name="Currency"/>.</param>
/// <param name="Currency">The currency of every amount.</param>
public sealed record InvoicePosted(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    Guid OrderId,
    string InvoiceNumber,
    decimal GrandTotal,
    string Currency)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name. Subscribe by this constant rather than by a literal.</summary>
    public const string Type = "billing.invoice-posted.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>
/// A posted invoice was cancelled by its compensating record: the number and the totals stand, a credit
/// note relieving the whole amount was posted with it, and the invoice's displayed status is now cancelled.
/// </summary>
/// <param name="EventId">The event's own identifier.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="AggregateId">The invoice.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="OrderId">The order.</param>
/// <param name="InvoiceNumber">The invoice's number, which it keeps.</param>
/// <param name="CreditDocumentId">The credit note posted with the cancellation, announced by its own event.</param>
public sealed record InvoiceCancelled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    Guid OrderId,
    string InvoiceNumber,
    Guid CreditDocumentId)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.invoice-cancelled.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>A credit note was posted against an invoice, relieving part or all of what the customer owes.</summary>
/// <param name="EventId">The event's own identifier.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="AggregateId">The credit note.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="InvoiceId">The invoice relieved.</param>
/// <param name="DocumentNumber">The note's number, <c>CN-&lt;branch&gt;-&lt;FY&gt;-000001</c>.</param>
/// <param name="GrandTotal">The amount relieved, tax included.</param>
/// <param name="Currency">The currency.</param>
public sealed record CreditNotePosted(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    Guid InvoiceId,
    string DocumentNumber,
    decimal GrandTotal,
    string Currency)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.credit-note-posted.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}

/// <summary>A debit note was posted against an invoice, adding to what the customer owes.</summary>
/// <param name="EventId">The event's own identifier.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="AggregateId">The debit note.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="InvoiceId">The invoice added to.</param>
/// <param name="DocumentNumber">The note's number, <c>DN-&lt;branch&gt;-&lt;FY&gt;-000001</c>.</param>
/// <param name="GrandTotal">The amount added, tax included.</param>
/// <param name="Currency">The currency.</param>
public sealed record DebitNotePosted(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    Guid OrganisationId,
    Guid BranchId,
    Guid CustomerId,
    Guid InvoiceId,
    string DocumentNumber,
    decimal GrandTotal,
    string Currency)
    : IntegrationEvent(EventId, OccurredAt, AggregateId)
{
    /// <summary>The wire name.</summary>
    public const string Type = "billing.debit-note-posted.v1";

    /// <inheritdoc />
    public override string EventType => Type;
}
