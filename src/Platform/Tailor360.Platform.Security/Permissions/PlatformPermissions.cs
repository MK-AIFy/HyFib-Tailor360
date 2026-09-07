namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Platform shared kernel: the audit trail, the outbox, feature flags, health
/// and diagnostics, and the organisation-wide read reach that branch scoping is measured against.
/// </summary>
/// <remarks>
/// The endpoints that use most of these arrive with issue #25; the permissions are declared here so
/// that the matrix the owner approves is complete before the screens exist, which is the order the
/// authorisation model needs — a permission cannot be granted to a role it was not declared for.
/// </remarks>
public static class PlatformPermissions
{
    /// <summary>Read every branch's data, whatever the caller is assigned to.</summary>
    /// <remarks>
    /// <para>
    /// <b>For a person, this is reach and never authority.</b> It satisfies
    /// <c>BranchScope.Organisation</c> on a read; a write outside the caller's assigned branches is
    /// still refused, because a write endpoint declares <c>BranchScope.CurrentBranch</c> and that is
    /// satisfied only by the branch the caller's session is actually working in.
    /// </para>
    /// <para>
    /// <b>A background job is a different thing, deliberately.</b> A job declaring
    /// <c>WorkerBranchScope.Organisation</c> carries this key <em>and</em> answers
    /// <c>CanActInBranch</c> true for every branch — it acts in every branch by declaration, which is
    /// not the same as a person's reading reach. That is why <c>WorkerPrincipal</c> and
    /// <c>SessionCurrentUser</c> implement the method differently, and why a job's authority is bounded
    /// by its <c>[WorkerJob]</c> declaration rather than by a branch assignment it does not have. The
    /// two are never the same principal: no handler serves both a request and a job.
    /// </para>
    /// </remarks>
    public const string ReadAllBranches = "admin.organisation.read_all_branches";

    /// <summary>Replay an outbox message or drain the dead-letter queue.</summary>
    public const string OutboxReplay = "admin.outbox.replay";

    /// <summary>Create, change or remove a feature flag value.</summary>
    public const string FeatureFlags = "admin.feature_flags";

    /// <summary>Read the audit trail and run chain verification.</summary>
    public const string AuditRead = "admin.audit.read";

    /// <summary>Export audit events for an external review.</summary>
    public const string AuditExport = "audit.export";

    /// <summary>Read platform health and diagnostics beyond the unauthenticated probes.</summary>
    public const string DiagnosticsRead = "admin.diagnostics.read";

    /// <summary>Read the detailed health endpoint, including backup age and provider state.</summary>
    public const string HealthRead = "admin.health.read";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(ReadAllBranches,
            "Read data from every branch in the organisation, not only the assigned ones.",
            PermissionModules.Platform, PermissionScope.Organisation, RequiresMfa: true),
        new(OutboxReplay, "Replay a failed or dead-lettered outbox message.",
            PermissionModules.Platform, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(FeatureFlags, "Change a feature flag value for the organisation or a branch.",
            PermissionModules.Platform, PermissionScope.NotBranchOwned,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(AuditRead, "Read audit events and run audit chain verification.",
            PermissionModules.Platform, PermissionScope.Organisation, RequiresMfa: true),
        new(AuditExport, "Export audit events for an external review.",
            PermissionModules.Platform, PermissionScope.Organisation, RequiresMfa: true, RequiresReason: true),
        new(DiagnosticsRead, "Read platform diagnostics and background job state.",
            PermissionModules.Platform, PermissionScope.Organisation),
        new(HealthRead, "Read the detailed health report, including backup age and provider state.",
            PermissionModules.Platform, PermissionScope.Organisation),
    ];
}
