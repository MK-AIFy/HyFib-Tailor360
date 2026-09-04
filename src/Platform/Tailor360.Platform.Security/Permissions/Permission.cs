namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// One entry in the permission catalogue. Permissions are declared in code so that the set is
/// reviewable and testable; roles that carry them are data (#24, #25).
/// </summary>
/// <param name="Key">Stable dotted key, for example <c>orders.order.confirm</c>. Never renamed once released.</param>
/// <param name="Description">What holding this permission allows, in operator language.</param>
/// <param name="Module">The module that owns the permission.</param>
/// <param name="RequiresMfa">
/// True when the holder's session must have completed multi-factor authentication. Set for anything
/// that moves money, changes access, or exports personal data.
/// </param>
/// <param name="RequiresStepUp">
/// True when the action additionally requires a fresh re-authentication immediately before it, even in
/// an already multi-factor session. Set for the smallest set of destructive or financially final actions.
/// </param>
/// <param name="RequiresReason">True when the caller must supply a reason that is written to the audit trail.</param>
public sealed record Permission(
    string Key,
    string Description,
    string Module,
    bool RequiresMfa = false,
    bool RequiresStepUp = false,
    bool RequiresReason = false)
{
    /// <summary>The policy name that maps to this permission in the authorisation pipeline.</summary>
    public string PolicyName => PermissionPolicy.NameFor(Key);
}
