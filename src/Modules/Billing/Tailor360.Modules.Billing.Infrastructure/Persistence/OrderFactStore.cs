using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain.Invoicing;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>The order-fact store over <see cref="BillingDbContext"/>. No save: the outbox dispatcher commits.</summary>
public sealed class OrderFactStore(BillingDbContext context) : IOrderFactStore
{
    /// <inheritdoc />
    public async Task<OrderFact?> FindAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.OrderFacts
            .SingleOrDefaultAsync(fact => fact.OrderId == orderId && fact.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public async Task LockForReadAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An order fact is locked for reading inside the transaction that posts against it.");
        }

        // FOR SHARE: the consumers' UPDATE of the fact waits for this transaction; other readers do not.
        await context.Database.ExecuteSqlAsync($"SELECT order_id FROM billing.order_facts WHERE order_id = {orderId} FOR SHARE", cancellationToken);
    }

    public void Add(OrderFact fact) => context.OrderFacts.Add(fact);
}
