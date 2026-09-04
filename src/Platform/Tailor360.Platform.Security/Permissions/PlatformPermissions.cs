namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Platform module. Operator actions such as replaying the outbox and
/// changing a feature flag are declared here in #21; the HTTP endpoints that use them arrive in #25.
/// </summary>
public sealed class PlatformPermissions : IPermissionSource
{
    /// <summary>Replay an outbox message or drain the dead-letter queue.</summary>
    public const string OutboxReplay = "admin.outbox.replay";

    /// <summary>Create, change or remove a feature flag value.</summary>
    public const string FeatureFlags = "admin.feature_flags";

    /// <summary>Read the audit trail and run chain verification.</summary>
    public const string AuditRead = "admin.audit.read";

    /// <summary>Read platform health and diagnostics beyond the unauthenticated probes.</summary>
    public const string DiagnosticsRead = "admin.diagnostics.read";

    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions =>
    [
        new(OutboxReplay, "Replay a failed or dead-lettered outbox message.", "Platform",
            RequiresMfa: true, RequiresReason: true),
        new(FeatureFlags, "Change a feature flag value for the organisation or a branch.", "Platform",
            RequiresMfa: true, RequiresReason: true),
        new(AuditRead, "Read audit events and run audit chain verification.", "Platform",
            RequiresMfa: true),
        new(DiagnosticsRead, "Read platform diagnostics and background job state.", "Platform"),
    ];
}
