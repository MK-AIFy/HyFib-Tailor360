using System.Text.Json;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Outbox;

/// <summary>
/// Writes integration events to the outbox in the caller's unit of work. Nothing is sent here: the row
/// is saved with the state change that produced it, and the dispatcher delivers it afterwards. That is
/// what makes it impossible for a consumer to hear about work that was rolled back.
/// </summary>
/// <param name="context">The platform context.</param>
/// <param name="clock">The clock.</param>
/// <param name="correlation">The correlation identifier to stamp on the message, when there is one.</param>
public sealed class OutboxWriter(
    PlatformDbContext context,
    IClock clock,
    IOutboxCorrelation correlation)
    : IEventPublisher
{
    /// <summary>Serialisation settings shared by the writer and the dispatcher.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <inheritdoc />
    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            OccurredAt = integrationEvent.OccurredAt,
            AggregateId = integrationEvent.AggregateId,
            EventType = integrationEvent.EventType,
            SchemaVersion = integrationEvent.SchemaVersion,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions),
            CorrelationId = correlation.CorrelationId,
            AvailableAt = clock.UtcNow,
            AttemptCount = 0,
        });

        return Task.CompletedTask;
    }
}

/// <summary>
/// Supplies the correlation identifier stamped on outbox messages. Kept as its own small interface so
/// that the persistence layer does not depend on the web request pipeline, and so a worker can stamp
/// the correlation of the job that caused the write.
/// </summary>
public interface IOutboxCorrelation
{
    /// <summary>The correlation identifier, or null when there is none.</summary>
    string? CorrelationId { get; }
}

/// <summary>The correlation used when nothing supplied one.</summary>
public sealed class NullOutboxCorrelation : IOutboxCorrelation
{
    /// <inheritdoc />
    public string? CorrelationId => null;
}
