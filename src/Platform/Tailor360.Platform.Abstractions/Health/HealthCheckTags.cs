namespace Tailor360.Platform.Abstractions.Health;

/// <summary>
/// Tags that decide which probe a health check answers. The split matters operationally: a readiness
/// probe that fails takes the instance out of rotation, so only a dependency the instance genuinely
/// cannot serve without belongs there.
/// </summary>
public static class HealthCheckTags
{
    /// <summary>
    /// The process is running and not deadlocked. Liveness checks perform no dependency calls at all,
    /// because restarting an instance never fixes a shared dependency and a cascade of restarts makes
    /// an outage worse.
    /// </summary>
    public const string Live = "live";

    /// <summary>
    /// The instance can serve traffic. Only the database carries this tag: without it no request can be
    /// answered correctly, whereas object storage or the malware scanner being down degrades a subset
    /// of features and must not remove the instance from rotation.
    /// </summary>
    public const string Ready = "ready";

    /// <summary>
    /// One-time startup work has completed, including the check that no migration in this build is
    /// unapplied. A failing startup probe holds traffic back without restarting the container.
    /// </summary>
    public const string Startup = "startup";

    /// <summary>
    /// A dependency whose loss degrades rather than disables the instance. Checks tagged this way report
    /// <c>Degraded</c>, which is visible on the diagnostics endpoint and alerts, but keeps readiness green.
    /// </summary>
    public const string NonEssential = "non-essential";
}
