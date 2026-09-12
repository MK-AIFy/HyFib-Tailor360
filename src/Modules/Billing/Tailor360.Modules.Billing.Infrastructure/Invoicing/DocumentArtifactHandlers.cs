using Microsoft.Extensions.Logging;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Modules.Billing.Infrastructure.Invoicing;

/// <summary>
/// The consumers of Billing's own posting events (#155): each asks for a rendering of the document the
/// event names, once. The dispatcher commits the request with the inbox row; the worker renders later,
/// outside any transaction, because the object store is another system.
/// </summary>
public abstract class DocumentArtifactRequestHandler(DocumentArtifactHandler artifacts) : IOutboxMessageHandler
{
    /// <inheritdoc />
    public abstract string EventType { get; }

    /// <inheritdoc />
    public abstract string HandlerName { get; }

    /// <inheritdoc />
    public string Schema => BillingDbContext.SchemaName;

    /// <summary>What kind of document the event posts.</summary>
    protected abstract DocumentKind Kind { get; }

    /// <inheritdoc />
    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var posted = OrderFacts.Read<PostedDocumentFact>(delivery.Payload);
        return artifacts.RequestAsync(Kind, posted.AggregateId, posted.OrganisationId, cancellationToken);
    }

    /// <summary>What every posting event carries that a request needs: the document and its organisation.</summary>
    protected sealed record PostedDocumentFact(Guid AggregateId, Guid OrganisationId);
}

/// <summary><c>billing.invoice-posted.v1</c>.</summary>
public sealed class InvoicePostedArtifactHandler(DocumentArtifactHandler artifacts) : DocumentArtifactRequestHandler(artifacts)
{
    /// <inheritdoc />
    public override string EventType => InvoicePosted.Type;

    /// <inheritdoc />
    public override string HandlerName => "billing.document-artifact-on-invoice-posted";

    /// <inheritdoc />
    protected override DocumentKind Kind => DocumentKind.Invoice;
}

/// <summary><c>billing.credit-note-posted.v1</c>.</summary>
public sealed class CreditNotePostedArtifactHandler(DocumentArtifactHandler artifacts) : DocumentArtifactRequestHandler(artifacts)
{
    /// <inheritdoc />
    public override string EventType => CreditNotePosted.Type;

    /// <inheritdoc />
    public override string HandlerName => "billing.document-artifact-on-credit-note-posted";

    /// <inheritdoc />
    protected override DocumentKind Kind => DocumentKind.CreditNote;
}

/// <summary><c>billing.debit-note-posted.v1</c>.</summary>
public sealed class DebitNotePostedArtifactHandler(DocumentArtifactHandler artifacts) : DocumentArtifactRequestHandler(artifacts)
{
    /// <inheritdoc />
    public override string EventType => DebitNotePosted.Type;

    /// <inheritdoc />
    public override string HandlerName => "billing.document-artifact-on-debit-note-posted";

    /// <inheritdoc />
    protected override DocumentKind Kind => DocumentKind.DebitNote;
}

/// <summary>
/// <c>billing.payment-recorded.v1</c>: the receipt issued with the payment is rendered on the roll template.
/// The event names the payment; the receipt is found from it, because the receipt is what is rendered. A
/// payment recorded by the version before receipts existed — a message committed before the upgrade and
/// dispatched after it, or written by an instance still running the old code — has no receipt to render:
/// the message is consumed and the fact logged, never retried into the dead-letter queue, because nothing
/// a retry could do would issue one.
/// </summary>
public sealed partial class PaymentRecordedArtifactHandler(DocumentArtifactHandler artifacts, IPaymentStore payments, ILogger<PaymentRecordedArtifactHandler> logger) : IOutboxMessageHandler
{
    /// <inheritdoc />
    public string EventType => PaymentRecorded.Type;

    /// <inheritdoc />
    public string HandlerName => "billing.document-artifact-on-payment-recorded";

    /// <inheritdoc />
    public string Schema => BillingDbContext.SchemaName;

    /// <inheritdoc />
    public async Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var recorded = OrderFacts.Read<RecordedPaymentFact>(delivery.Payload);
        var receipt = await payments.FindReceiptForPaymentAsync(recorded.AggregateId, recorded.OrganisationId, cancellationToken);
        if (receipt is null)
        {
            LogNoReceipt(logger, recorded.AggregateId);
            return;
        }

        await artifacts.RequestAsync(DocumentKind.Receipt, receipt.Id, recorded.OrganisationId, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment {PaymentId} was recorded without a receipt, before receipts were issued with payments; there is no receipt document to render for it.")]
    private static partial void LogNoReceipt(ILogger logger, Guid paymentId);

    private sealed record RecordedPaymentFact(Guid AggregateId, Guid OrganisationId);
}
