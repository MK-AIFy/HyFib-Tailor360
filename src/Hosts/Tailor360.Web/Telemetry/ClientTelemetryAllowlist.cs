using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tailor360.Web.Telemetry;

/// <summary>
/// The closed set of client telemetry event types this server accepts, and, per type, the closed set of
/// attribute names and value shapes it accepts. This is an <b>allowlist, not a denylist</b>
/// (<c>docs/nfr/data-classification.md</c> section 5.18): anything not named here is dropped before it
/// reaches a log record or a metric, whatever it looks like. Values are constrained by shape, not
/// trusted — an enumeration, a number in a range, a stack <b>hash</b> rather than a stack, a route
/// <b>name</b> rather than a URL — so that a field surviving the filter can never itself become the leak
/// the filter exists to stop.
/// </summary>
/// <remarks>
/// The scanner event types (<c>decode.latency</c>, <c>decode.failure</c>, <c>permission.denied</c>,
/// <c>fallback.used</c>) are #36's to add, in #36's own pull request — this allowlist carries the six
/// generic event types only, and adding a seventh is meant to be exactly one entry here.
/// </remarks>
public static partial class ClientTelemetryAllowlist
{
    /// <summary>An unhandled JavaScript error reached the window's error handler.</summary>
    public const string UnhandledError = "unhandled_error";

    /// <summary>An unhandled promise rejection reached the window's handler.</summary>
    public const string UnhandledRejection = "unhandled_rejection";

    /// <summary>The service worker failed to install, activate, fetch or update.</summary>
    public const string ServiceWorkerFailure = "service_worker_failure";

    /// <summary>The client detected whether one capability from the support matrix is usable here.</summary>
    public const string CapabilityDetection = "capability_detection";

    /// <summary>A Core Web Vital measurement.</summary>
    public const string WebVital = "web_vital";

    /// <summary>A navigation-timing measurement.</summary>
    public const string NavigationTiming = "navigation_timing";

    /// <summary>The capabilities <see cref="CapabilityDetection"/> may report on, from support-matrix.md section 6.</summary>
    public static IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "cameraScanning", "barcodeDetectorApi", "hardwareScannerUsb", "hardwareScannerBluetooth",
        "thermalLabelPrinting", "documentPrinting", "installablePwa", "offlineShell",
        "offlineQueuedScans", "serviceWorker", "webAuthnPasskeys", "pushNotifications", "backgroundSync",
    };

    /// <summary>What a capability check answered.</summary>
    public static IReadOnlySet<string> CapabilityResults { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "supported", "unsupported", "fallback",
    };

    /// <summary>The reasons a service worker can fail, closed because the worker names its own failure.</summary>
    public static IReadOnlySet<string> ServiceWorkerFailureReasons { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "install_failed", "activate_failed", "fetch_failed", "update_failed", "unregistered",
    };

    /// <summary>The Core Web Vitals this server accepts a measurement for.</summary>
    public static IReadOnlySet<string> WebVitalMetrics { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "LCP", "CLS", "INP", "FCP", "TTFB",
    };

    /// <summary>The navigation-timing measurements this server accepts.</summary>
    public static IReadOnlySet<string> NavigationTimingMetrics { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "domContentLoaded", "loadEvent", "firstPaint", "firstContentfulPaint", "timeToInteractive",
    };

    /// <summary>The longest a bounded string attribute may be.</summary>
    private const int MaxAttributeStringLength = 200;

    /// <summary>The upper bound a timing-shaped numeric attribute (milliseconds) may report.</summary>
    private const double MaxTimingMilliseconds = 300_000;

    private static IReadOnlyDictionary<string, Func<JsonElement, bool>> ErrorAttributes { get; } =
        new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
        {
            // A code the client chose from its own closed catalogue, never a message a person typed or
            // the exception's own free-text message.
            ["messageCode"] = value => IsBoundedString(value, MessageCodePattern()),

            // The stack's SHA-256 digest, never the stack itself: enough to group occurrences of the
            // same fault without carrying a file path or a variable name that could be personal.
            ["stackHash"] = value => IsBoundedString(value, HexDigestPattern()),

            // The client route's name, for example "orders/draft", never the URL the person was on —
            // a URL can carry an identifier, and an identifier can be personal.
            ["routeName"] = value => IsBoundedString(value, RouteNamePattern()),
        };

    // Declared after ErrorAttributes on purpose: a static field initialiser runs in declaration order,
    // and this dictionary reads ErrorAttributes while building itself.
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Func<JsonElement, bool>>>
        EventTypes = new Dictionary<string, IReadOnlyDictionary<string, Func<JsonElement, bool>>>(StringComparer.Ordinal)
        {
            [UnhandledError] = ErrorAttributes,
            [UnhandledRejection] = ErrorAttributes,
            [ServiceWorkerFailure] = new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                ["reasonCode"] = value => IsEnumMember(value, ServiceWorkerFailureReasons),
            },
            [CapabilityDetection] = new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                ["capability"] = value => IsEnumMember(value, Capabilities),
                ["result"] = value => IsEnumMember(value, CapabilityResults),
            },
            [WebVital] = new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                ["metric"] = value => IsEnumMember(value, WebVitalMetrics),
                ["value"] = value => IsNumberInRange(value, 0, MaxTimingMilliseconds),
            },
            [NavigationTiming] = new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                ["metric"] = value => IsEnumMember(value, NavigationTimingMetrics),
                ["value"] = value => IsNumberInRange(value, 0, MaxTimingMilliseconds),
            },
        };

    /// <summary>Every event type this server accepts.</summary>
    public static IReadOnlySet<string> EventTypeNames { get; } = new HashSet<string>(EventTypes.Keys, StringComparer.Ordinal);

    /// <summary>
    /// Filters one event's attribute bag down to the names and shapes this event's type allows.
    /// </summary>
    /// <param name="eventType">The event's declared type.</param>
    /// <param name="attributes">The attribute bag as the client sent it, or null.</param>
    /// <returns>
    /// The surviving attributes, or null when <paramref name="eventType"/> is not in the allowlist at
    /// all — the caller's signal to drop the whole event rather than an event with an empty bag.
    /// </returns>
    public static IReadOnlyDictionary<string, JsonElement>? Filter(
        string? eventType,
        IReadOnlyDictionary<string, JsonElement>? attributes)
    {
        if (eventType is null || !EventTypes.TryGetValue(eventType, out var allowed))
        {
            return null;
        }

        if (attributes is null || attributes.Count == 0)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        var survivors = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var (name, value) in attributes)
        {
            if (allowed.TryGetValue(name, out var validate) && validate(value))
            {
                survivors[name] = value;
            }
        }

        return survivors;
    }

    private static bool IsEnumMember(JsonElement value, IReadOnlySet<string> members)
        => value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text
            && members.Contains(text);

    private static bool IsBoundedString(JsonElement value, Regex pattern)
        => value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 and <= MaxAttributeStringLength } text
            && pattern.IsMatch(text);

    private static bool IsNumberInRange(JsonElement value, double minimum, double maximum)
        => value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            && number >= minimum
            && number <= maximum
            && double.IsFinite(number);

    /// <summary>A dotted, lower-case code the client chose from its own catalogue — never free text.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z0-9]+)*$", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex MessageCodePattern();

    /// <summary>A hex digest — SHA-256 (64 characters) or a shorter digest the client may send.</summary>
    [GeneratedRegex(@"^[0-9a-f]{8,64}$", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex HexDigestPattern();

    /// <summary>
    /// A client route name: `/`-separated segments, each either a static word (letters and hyphens,
    /// for example <c>orders</c>) or a <c>:</c>-prefixed placeholder (for example <c>:orderId</c>).
    /// Deliberately rejects a segment that is purely numeric or otherwise looks like a resolved value —
    /// a route <b>template</b>, not the path a specific request actually took, which is what keeps a
    /// resolved identifier such as a phone number or an order number from riding through as a "name".
    /// </summary>
    [GeneratedRegex(
        @"^(?:[A-Za-z][A-Za-z-]*|:[A-Za-z][A-Za-z0-9]*)(?:/(?:[A-Za-z][A-Za-z-]*|:[A-Za-z][A-Za-z0-9]*))*$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex RouteNamePattern();

    /// <summary>
    /// Validates a route name against the same shape <see cref="RouteNamePattern"/> requires of the
    /// event-level <c>routeName</c> attribute, for the batch envelope's own
    /// <see cref="ClientTelemetryBatchRequest.RouteName"/>, which the allowlist otherwise never sees —
    /// it is not itself an event attribute, so nothing filters it before <c>ClientTelemetryHandler</c>
    /// logs it.
    /// </summary>
    /// <returns>The route name unchanged, or null when it is missing, overlong or not route-name-shaped.</returns>
    public static string? SanitizeRouteName(string? routeName)
        => routeName is { Length: > 0 and <= MaxAttributeStringLength } text && RouteNamePattern().IsMatch(text)
            ? text
            : null;
}
