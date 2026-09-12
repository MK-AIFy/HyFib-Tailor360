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
