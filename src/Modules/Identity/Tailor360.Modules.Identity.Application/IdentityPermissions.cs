using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Application;

/// <summary>
/// The permissions the Identity module owns, covering users, roles, permissions, branch assignments, sessions and multi-factor authentication.
/// The catalogue is composed at startup and rejects a key claimed by two modules, so every permission
/// has exactly one owner. Issue #24 populates this set together with the role matrix that carries it.
/// </summary>
public sealed class IdentityPermissions : IPermissionSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<Permission> Permissions => [];
}
