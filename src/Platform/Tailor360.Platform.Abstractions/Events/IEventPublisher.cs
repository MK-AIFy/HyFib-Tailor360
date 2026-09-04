namespace Tailor360.Platform.Abstractions.Events;

/// <summary>
/// Publishes an integration event. The implementation writes it to the outbox in the caller's
/// transaction, so the event is committed with the change it describes and discarded with it. Nothing
/// in the application sends an event directly to a consumer.
/// </summary>
public interface IEventPublisher
{
    /// <summary>Enqueues an event for delivery when the current transaction commits.</summary>
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
