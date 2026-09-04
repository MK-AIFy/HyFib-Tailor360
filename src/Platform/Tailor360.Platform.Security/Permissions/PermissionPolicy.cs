namespace Tailor360.Platform.Security.Permissions;

/// <summary>Naming for the dynamically generated permission policies.</summary>
public static class PermissionPolicy
{
    /// <summary>The prefix every permission-backed policy name carries.</summary>
    public const string Prefix = "perm:";

    /// <summary>The policy name for a permission key.</summary>
    public static string NameFor(string permissionKey) => Prefix + permissionKey;

    /// <summary>True when a policy name refers to a permission.</summary>
    public static bool IsPermissionPolicy(string policyName)
        => policyName.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Extracts the permission key from a permission policy name.</summary>
    public static string KeyFrom(string policyName) => policyName[Prefix.Length..];
}
