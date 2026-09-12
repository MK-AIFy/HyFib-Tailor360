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
    public void Add(OrderFact fact) => context.OrderFacts.Add(fact);
}
