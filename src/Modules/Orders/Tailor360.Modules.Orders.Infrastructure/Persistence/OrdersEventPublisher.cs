using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// The Orders module's event publisher, bound to the Orders context.
/// </summary>
/// <remarks>
/// Three lines of binding, and they are the load-bearing three: the type parameter is what decides
/// which schema's <c>outbox_messages</c> a published event lands in, and it is the same context the
/// module's stores hold, so the event and the change commit together (issue #77).
/// </remarks>
/// <param name="context">The module's context, shared with the module's stores.</param>
/// <param name="clock">The clock.</param>
/// <param name="correlation">The correlation identifier to stamp on the message.</param>
public sealed class OrdersEventPublisher(
    OrdersDbContext context,
    IClock clock,
    IOutboxCorrelation correlation)
    : ModuleEventPublisher<OrdersDbContext>(context, clock, correlation), IOrdersEventPublisher;
