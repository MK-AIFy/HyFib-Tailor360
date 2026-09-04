using Microsoft.AspNetCore.Authorization;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Requires that the caller holds a permission, and that the session satisfies whatever the permission
/// declares about multi-factor authentication and step-up freshness.
/// </summary>
/// <param name="permissionKey">The permission key the caller must hold.</param>
public sealed class PermissionRequirement(string permissionKey) : IAuthorizationRequirement
{
    /// <summary>The permission key the caller must hold.</summary>
    public string PermissionKey { get; } = permissionKey;
}
