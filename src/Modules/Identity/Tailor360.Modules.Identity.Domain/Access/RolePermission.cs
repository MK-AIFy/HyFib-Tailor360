namespace Tailor360.Modules.Identity.Domain.Access;

/// <summary>
/// One permission key granted to one role.
/// </summary>
/// <remarks>
/// The key is a string here rather than a catalogue type on purpose: the catalogue lives in
/// <c>Platform.Security</c>, and a domain project references <c>Platform.Abstractions</c> and nothing
/// else (ARCH-001). The check that the key names a real permission is made where the catalogue is
/// visible — when a role is defined, and again by the matrix test — so a grant can never reach the
/// database naming a permission the application does not understand.
/// </remarks>
public sealed class RolePermission
{
    private RolePermission()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private RolePermission(Guid roleId, string permissionKey, DateTimeOffset now, Guid? by)
    {
        RoleId = roleId;
        PermissionKey = permissionKey;
        GrantedAt = now;
        GrantedBy = by;
    }

    /// <summary>The role the permission is granted to.</summary>
    public Guid RoleId { get; private set; }

    /// <summary>The permission key, as declared in the catalogue.</summary>
    public string PermissionKey { get; private set; } = string.Empty;

    /// <summary>When the grant was made.</summary>
    public DateTimeOffset GrantedAt { get; private set; }

    /// <summary>Who made it. Null for the grants written by reference-data seeding.</summary>
    public Guid? GrantedBy { get; private set; }

    internal static RolePermission Create(Guid roleId, string permissionKey, DateTimeOffset now, Guid? by)
        => new(roleId, permissionKey, now, by);
}
