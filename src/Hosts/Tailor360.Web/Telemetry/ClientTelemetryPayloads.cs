using System.Text.Json;

namespace Tailor360.Web.Telemetry;

/// <summary>
/// A batch of client telemetry events, exactly as the browser posts it. Declared here rather than in a
/// module: ARCH-013's allowed exceptions permit a host-composed endpoint's payload types to live beside
/// the endpoint that owns them.
/// </summary>
/// <param name="BatchId">A client-generated identifier for this flush, for de-duplication at the log level.</param>
/// <param name="ClientVersion">The build the client is running.</param>
/// <param name="RouteName">
/// The client route the batch was flushed from, by name — never a URL. Sanitised by
/// <see cref="ClientTelemetryAllowlist.SanitizeRouteName"/> before it is logged, since it is the
/// envelope's own field and never passes through the per-event attribute allowlist.
/// </param>
/// <param name="DeviceClass">The device class the client detected itself as, for example <c>shop-floor-phone</c>.</param>
/// <param name="Engine">The rendering engine, for example <c>blink</c>.</param>
/// <param name="OperatingSystemFamily">The operating-system family, for example <c>android</c>.</param>
/// <param name="Events">The events flushed in this batch, bounded by <c>ClientTelemetry:MaxEventsPerBatch</c>.</param>
public sealed record ClientTelemetryBatchRequest(
    Guid? BatchId,
    string? ClientVersion,
    string? RouteName,
    string? DeviceClass,
    string? Engine,
    string? OperatingSystemFamily,
    IReadOnlyList<ClientTelemetryEventRequest>? Events);

/// <summary>One event inside a batch, before the allowlist is applied.</summary>
/// <param name="Type">
/// The event type. Only a closed set of names survives <see cref="ClientTelemetryAllowlist"/>; anything
/// else drops the whole event.
/// </param>
/// <param name="Timestamp">When the client observed it.</param>
/// <param name="Attributes">
/// A bounded attribute bag. Only the names <see cref="ClientTelemetryAllowlist"/> declares for this
/// event's <see cref="Type"/> survive, and only when the value matches the declared shape; everything
/// else is dropped before it reaches a sink.
/// </param>
public sealed record ClientTelemetryEventRequest(
    string? Type,
    DateTimeOffset? Timestamp,
    IReadOnlyDictionary<string, JsonElement>? Attributes);
