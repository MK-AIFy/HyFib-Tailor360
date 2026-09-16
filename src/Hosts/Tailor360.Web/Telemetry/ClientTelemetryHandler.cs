using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tailor360.Platform.Observability.Correlation;
using Tailor360.Platform.Observability.Telemetry;

namespace Tailor360.Web.Telemetry;

/// <summary>
/// Applies the allowlist to a validated batch and emits every surviving event as one structured log
/// record and one counter increment. No persistence: a client event is a log record and a metric, never
/// a row (<c>docs/nfr/data-classification.md</c> section 5.18).
/// </summary>
/// <remarks>
/// Deliberately takes an already-parsed, already-size-checked batch: content type, body size and
/// malformed JSON are properties of the raw HTTP request and are refused by the endpoint before this
/// type is ever reached. What this type owns is the one thing that needs the allowlist — deciding which
/// events, and which of their attributes, are safe to put anywhere.
/// </remarks>
/// <param name="logger">The host's own logger, so every client event lands beside the host's own log lines.</param>
/// <param name="correlation">The request's correlation identifier, stamped onto every emitted event.</param>
public sealed class ClientTelemetryHandler(
    ILogger<ClientTelemetryHandler> logger,
    ICorrelationContext correlation)
{
    private static readonly Counter<long> EventsAccepted = Tailor360Diagnostics.Meter.CreateCounter<long>(
        "client_telemetry.events_accepted",
        description: "Client telemetry events that passed the allowlist and were emitted.");

    /// <summary>
    /// Filters and emits every event in the batch, and reports how many survived.
    /// </summary>
    /// <remarks>
    /// A batch whose every event's type is outside the allowlist emits nothing and returns zero — the
    /// caller's signal to treat the batch as refused, per the acceptance criterion that an unknown event
    /// type is a refusal rather than a silently accepted no-op.
    /// </remarks>
    /// <param name="batch">The parsed batch.</param>
    /// <returns>How many events were emitted.</returns>
    public int Ingest(ClientTelemetryBatchRequest batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var emitted = 0;

        foreach (var candidate in batch.Events ?? [])
        {
            var attributes = ClientTelemetryAllowlist.Filter(candidate.Type, candidate.Attributes);
            if (attributes is null)
            {
                // The type itself is not in the allowlist. The whole event is dropped rather than
                // emitted with an empty attribute bag, which would misrepresent what the client sent.
                continue;
            }

            Emit(batch, candidate, attributes);
            emitted++;
        }

        return emitted;
    }

    private void Emit(
        ClientTelemetryBatchRequest batch,
        ClientTelemetryEventRequest candidate,
        IReadOnlyDictionary<string, JsonElement> attributes)
    {
        EventsAccepted.Add(1, new KeyValuePair<string, object?>("type", candidate.Type));

        if (logger.IsEnabled(LogLevel.Information))
        {
            // {@Attributes} destructures the dictionary as its own structured object rather than a
            // string, so a reader — and the redaction test — can inspect each surviving attribute by
            // name. Only names and shapes the allowlist has already accepted ever reach this call.
            logger.LogInformation(
                "Client telemetry event {EventType} in batch {BatchId} on route {RouteName}, client "
                + "{ClientVersion}, correlation {CorrelationId}, attributes {@Attributes}",
                candidate.Type,
                batch.BatchId,
                batch.RouteName,
                batch.ClientVersion,
                correlation.CorrelationId,
                ToLoggable(attributes));
        }
    }

    /// <summary>
    /// Converts the survivors to plain CLR values so the log sink serialises a number as a number and a
    /// string as a string, rather than the raw <see cref="JsonElement"/> structure.
    /// </summary>
    private static Dictionary<string, object?> ToLoggable(IReadOnlyDictionary<string, JsonElement> attributes)
    {
        var result = new Dictionary<string, object?>(attributes.Count, StringComparer.Ordinal);

        foreach (var (name, value) in attributes)
        {
            result[name] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }

        return result;
    }
}
