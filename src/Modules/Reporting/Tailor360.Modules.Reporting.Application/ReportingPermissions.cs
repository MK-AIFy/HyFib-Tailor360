using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Reporting.Application;

/// <summary>
/// The permissions the Reporting module owns, covering read models, projections, reconciliation, scheduled reports and governed exports.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class ReportingPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
