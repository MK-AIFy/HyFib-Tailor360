using System.Text.Json;
using System.Text.Json.Serialization;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Outbox;

/// <summary>
/// Writes a module's integration events to that module's own outbox.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Generic over the module's context, which is the whole design.</strong> The publisher and
/// the module's stores are both scoped, and both receive the same <typeparamref name="TContext"/>
/// instance from the container, so the row this adds is tracked by the same change tracker as the
/// aggregate and goes out on the same <c>SaveChangesAsync</c>. A publisher that took a shared context
/// could not do that at any price: a second context is a second connection and a second transaction,
/// so one ordering commits work whose event is lost and the other announces work that rolled back
/// (<see href="https://github.com/MK-AIFy/HyFib-Tailor360/issues/77">#77</see>).
/// </para>
/// <para>
/// The type parameter also bounds what a module can reach. A publisher constructed over
/// <c>CustomersDbContext</c> can write to <c>customers.outbox_messages</c> and to nothing else, so
/// publishing cannot become the cross-schema write ARCH-005 forbids.
/// </para>
/// <para>
/// A module registers it behind a port of its own — <c>ICustomersEventPublisher</c> and its
/// equivalents — because <see cref="IEventPublisher"/> is one interface and the web host composes
/// every module at once, so a single registration of it would leave whichever module registered last
/// serving all of them.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The module's context, and therefore the module's outbox.</typeparam>
/// <param name="context">The module's context, shared with the module's stores.</param>
/// <param name="clock">The clock.</param>
/// <param name="correlation">The correlation identifier to stamp on the message, when there is one.</param>
public class ModuleEventPublisher<TContext>(
    TContext context,
    IClock clock,
    IOutboxCorrelation correlation)
    : IEventPublisher
    where TContext : ModuleDbContext
{
    /// <inheritdoc />
    public void Publish(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            OccurredAt = integrationEvent.OccurredAt,
            AggregateId = integrationEvent.AggregateId,
            EventType = integrationEvent.EventType,
            SchemaVersion = integrationEvent.SchemaVersion,
            Payload = JsonSerializer.Serialize(
                integrationEvent, integrationEvent.GetType(), OutboxPayload.SerializerOptions),
            CorrelationId = correlation.CorrelationId,
            AvailableAt = clock.UtcNow,
            AttemptCount = 0,
        });
    }
}

/// <summary>
/// How an integration event is written to and read from an outbox row.
/// </summary>
/// <remarks>
/// <para>
/// Non-generic and shared, so that every module's publisher and the dispatcher that reads them all
/// agree on one encoding. A payload written one way and read another would fail at the consumer, in
/// the worker, long after the request that produced it.
/// </para>
/// <para>
/// <strong>An enumeration goes out as its name, not its number.</strong>
/// <c>docs/architecture/conventions.md</c> section 5.5 admits "identifiers, codes, statuses…" to a
/// payload, and <c>"status": 1</c> is an ordinal rather than a status: it tells an operator reading a
/// dead-lettered row nothing, and it silently re-points every stored payload the day somebody inserts
/// a member. Names cost a few bytes and survive that. The converter is set here, once, rather than
/// per module, because two modules encoding the same enumeration differently is the failure this class
/// exists to prevent — and it is set now because the first events (issue #26) are the first payloads
/// there are. Changing it later is a breaking wire change for every subscriber; changing it while
/// every outbox is empty costs nothing.
/// </para>
/// </remarks>
public static class OutboxPayload
{
    /// <summary>Serialisation settings shared by every publisher and the dispatcher.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };
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
