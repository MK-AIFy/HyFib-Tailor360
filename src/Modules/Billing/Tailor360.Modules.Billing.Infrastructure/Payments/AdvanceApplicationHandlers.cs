using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Modules.Billing.Infrastructure.Payments;

/// <summary>
/// The consumer of <c>billing.invoice-posted.v1</c> that applies the order's held advances to the invoice
/// just posted (INV-PAY-05), by the same rule that allocates at recording. It stages its allocations on
/// the module's context and does not save: the dispatcher commits them with the inbox row, so the rule
/// runs once per posting however many times the message is delivered.
/// </summary>
public sealed class InvoicePostedAdvanceHandler(PaymentHandler payments) : IOutboxMessageHandler
{
    /// <inheritdoc />
    public string EventType => InvoicePosted.Type;

    /// <inheritdoc />
    public string HandlerName => "billing.advances-on-invoice-posted";

    /// <inheritdoc />
    public string Schema => BillingDbContext.SchemaName;

    /// <inheritdoc />
    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var posted = OrderFacts.Read<PostedInvoiceFact>(delivery.Payload);
        return payments.ApplyAdvancesOnPostingAsync(posted.AggregateId, posted.OrganisationId, cancellationToken);
    }

    /// <summary>What the posting event carries that the rule needs: the invoice and its organisation.</summary>
    private sealed record PostedInvoiceFact(Guid AggregateId, Guid OrganisationId);
}
