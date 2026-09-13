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

    /// <summary>
    /// Holds the order's fact against change for the rest of the current transaction (a share lock), so a
    /// revision or a cancellation arriving through the outbox waits until the posting that read the fact
    /// has committed. Outside a transaction it throws.
    /// </summary>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    Task LockForReadAsync(Guid orderId, CancellationToken cancellationToken = default);
}
