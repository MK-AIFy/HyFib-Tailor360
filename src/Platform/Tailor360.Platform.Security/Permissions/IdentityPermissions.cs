namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Identity module: staff accounts, roles, and the branch register.
/// </summary>
/// <remarks>
/// All three change who may do what, so all three carry multi-factor authentication, step-up and a
/// mandatory reason — the rule stated for row 26 of <c>docs/prd/raci.md</c>, where the step-up and
/// reason columns both read "All". The screens that use them are issue #25.
/// </remarks>
public static class IdentityPermissions
{
    /// <summary>Invite, activate, suspend, reinstate and reset staff accounts.</summary>
    public const string Users = "admin.users";

    /// <summary>Create custom roles and change which permissions a role grants.</summary>
    public const string Roles = "admin.roles";

    /// <summary>Create and configure branches, their timezone and their working calendar.</summary>
    public const string Branches = "admin.branches";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(Users, "Administer staff accounts, their roles and their branch assignments.",
            PermissionModules.Identity, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(Roles, "Create roles and change the permissions a role grants.",
            PermissionModules.Identity, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(Branches, "Create and configure branches, timezones and working calendars.",
            PermissionModules.Identity, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
    ];
}
