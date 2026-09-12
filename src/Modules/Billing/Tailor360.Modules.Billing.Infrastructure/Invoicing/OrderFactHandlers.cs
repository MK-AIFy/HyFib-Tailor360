using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Modules.Billing.Infrastructure.Invoicing;

/// <summary>
/// The consumers of Orders' events, one per event type because that is the shape
/// <see cref="IOutboxMessageHandler"/> has and because the inbox de-duplicates on the handler's name.
/// Each reads the payload into a Billing-owned fact and hands it to the projector; the dispatcher
/// commits the projector's writes with the inbox row.
/// </summary>
public abstract class OrderFactHandler(OrderFactProjector projector) : IOutboxMessageHandler
{
    /// <inheritdoc />
    public abstract string EventType { get; }

    /// <inheritdoc />
    public abstract string HandlerName { get; }

    /// <inheritdoc />
    public string Schema => BillingDbContext.SchemaName;

    /// <summary>The projector the payload is handed to.</summary>
    protected OrderFactProjector Projector { get; } = projector;

    /// <inheritdoc />
    public abstract Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken);
}

/// <summary><c>orders.order-confirmed.v1</c>.</summary>
public sealed class OrderConfirmedFactHandler(OrderFactProjector projector) : OrderFactHandler(projector)
{
    /// <inheritdoc />
    public override string EventType => OrderFacts.OrderConfirmedType;

    /// <inheritdoc />
    public override string HandlerName => "billing.order-fact-on-order-confirmed";

    /// <inheritdoc />
    public override Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return Projector.ApplyConfirmedAsync(OrderFacts.Read<OrderConfirmedFact>(delivery.Payload), cancellationToken);
    }
}

/// <summary><c>orders.job-cancelled.v1</c>.</summary>
public sealed class GarmentJobCancelledFactHandler(OrderFactProjector projector) : OrderFactHandler(projector)
{
    /// <inheritdoc />
    public override string EventType => OrderFacts.GarmentJobCancelledType;

    /// <inheritdoc />
    public override string HandlerName => "billing.order-fact-on-garment-job-cancelled";

    /// <inheritdoc />
    public override Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return Projector.ApplyJobCancelledAsync(OrderFacts.Read<GarmentJobCancelledFact>(delivery.Payload), cancellationToken);
    }
}

/// <summary><c>orders.order-revised.v1</c>: the same fact shape as a confirmation, at a later revision.</summary>
public sealed class OrderRevisedFactHandler(OrderFactProjector projector) : OrderFactHandler(projector)
{
    /// <inheritdoc />
    public override string EventType => OrderFacts.OrderRevisedType;

    /// <inheritdoc />
    public override string HandlerName => "billing.order-fact-on-order-revised";

    /// <inheritdoc />
    public override Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return Projector.ApplyConfirmedAsync(OrderFacts.Read<OrderConfirmedFact>(delivery.Payload), cancellationToken);
    }
}

/// <summary><c>orders.order-cancelled.v1</c>.</summary>
public sealed class OrderCancelledFactHandler(OrderFactProjector projector) : OrderFactHandler(projector)
{
    /// <inheritdoc />
    public override string EventType => OrderFacts.OrderCancelledType;

    /// <inheritdoc />
    public override string HandlerName => "billing.order-fact-on-order-cancelled";

    /// <inheritdoc />
    public override Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return Projector.ApplyCancelledAsync(OrderFacts.Read<OrderCancelledFact>(delivery.Payload), cancellationToken);
    }
}

/// <summary><c>orders.garment-job-created.v1</c>.</summary>
public sealed class GarmentJobCreatedFactHandler(OrderFactProjector projector) : OrderFactHandler(projector)
{
    /// <inheritdoc />
    public override string EventType => OrderFacts.GarmentJobCreatedType;

    /// <inheritdoc />
    public override string HandlerName => "billing.order-fact-on-garment-job-created";

    /// <inheritdoc />
    public override Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return Projector.ApplyJobCreatedAsync(OrderFacts.Read<GarmentJobCreatedFact>(delivery.Payload), cancellationToken);
    }
}
