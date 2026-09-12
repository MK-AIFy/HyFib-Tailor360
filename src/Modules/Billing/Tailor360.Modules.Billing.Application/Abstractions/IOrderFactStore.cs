using Tailor360.Modules.Billing.Domain.Invoicing;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>
/// Reads and writes what Billing knows about orders. Written from the outbox consumers only, whose
/// changes the dispatcher commits with the inbox row that records the message ran — so there is no
/// save here: a consumer leaves its change in the context and the dispatcher commits both or neither.
/// </summary>
public interface IOrderFactStore
{
    /// <summary>The order's fact, tracked for change, or null when none has been heard.</summary>
    Task<OrderFact?> FindAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>Adds a fact to the context.</summary>
    void Add(OrderFact fact);
}
