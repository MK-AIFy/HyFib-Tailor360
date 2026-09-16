namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// The names of the rate-limit policies an endpoint may declare.
/// </summary>
/// <remarks>
/// <para>
/// The names live here rather than in the web host because a module's endpoints declare them and a
/// module may not reference a host (architecture rule ARCH-012). The host owns the numbers behind each
/// name; a module chooses which shape of traffic its endpoint is, never how much of it is allowed.
/// </para>
/// <para>
/// The catalogue is the one in plan section 4.4. Issue #23 needs the first three and the two defaults;
/// #53 (closed) added ARCH-017, which fails an endpoint that declares none, and the shape of the rest.
/// The customer-link, provider-callback and telemetry-ingest policies arrive with the endpoints that
/// need them — #47, #55 and #52 (<c>docs/architecture/components.md</c> line 172) — because #53 closed
/// before any of the three existed to be sized.
/// </para>
/// </remarks>
public static class RateLimitPolicyNames
{
    /// <summary>
    /// Sign-in and other unauthenticated credential endpoints. Keyed on the client address, because
    /// before a session exists there is nothing else to key on.
    /// </summary>
    public const string AuthenticationAnonymous = "auth-anon";

    /// <summary>
    /// Answering a second factor, and spending a recovery code. Separate from sign-in because the
    /// caller has already proved a password, so the traffic shape and the abuse are different.
    /// </summary>
    public const string MultiFactorChallenge = "mfa-challenge";

    /// <summary>
    /// Asking for or spending a recovery link. Tighter than sign-in, because each accepted request may
    /// send a message to somebody who did not ask for it.
    /// </summary>
    public const string RecoveryAnonymous = "recovery-anon";

    /// <summary>Ordinary authenticated reads.</summary>
    public const string DefaultUser = "default-user";

    /// <summary>Ordinary unauthenticated requests that are not credential endpoints.</summary>
    public const string DefaultIp = "default-ip";

    /// <summary>Authenticated state-changing requests.</summary>
    public const string Write = "write";

    /// <summary>Barcode scanning, which arrives in bursts as a rack is worked through.</summary>
    public const string ScanBurst = "scan-burst";

    /// <summary>Report and export generation, which is expensive per call.</summary>
    public const string ExportHeavy = "export-heavy";

    /// <summary>
    /// Client telemetry ingest. Anonymous and keyed on the client address, because the caller may have no
    /// account — the unsupported-configuration page and a batch flushed by <c>navigator.sendBeacon</c> on
    /// page hide both predate a session. Shaped as a steady trickle rather than a burst: one flush per
    /// client roughly every <c>[E12-F03-5]</c>'s configured interval, not a scan-style spike.
    /// </summary>
    public const string ClientTelemetryIngest = "telemetry-ingest";
}
