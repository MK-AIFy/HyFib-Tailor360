namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// One entry in the permission catalogue. Permissions are declared in code so that the set is
/// reviewable and testable; roles that carry them are data (#24, #25).
/// </summary>
/// <param name="Key">Stable dotted key, for example <c>orders.order.confirm</c>. Never renamed once released.</param>
/// <param name="Description">What holding this permission allows, in operator language.</param>
/// <param name="Module">The module that owns the permission.</param>
/// <param name="Scope">
/// Whether the action is about one branch's operational data or about the organisation as a whole.
/// It describes the permission; what an endpoint actually demands is its own
/// <c>BranchScope</c> declaration, and the two are related but not the same — a branch-scoped
/// permission is declared <c>CurrentBranch</c> on a write and <c>AssignedBranches</c> on a read.
/// </param>
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
    PermissionScope Scope = PermissionScope.Branch,
    bool RequiresMfa = false,
    bool RequiresStepUp = false,
    bool RequiresReason = false)
{
    /// <summary>The policy name that maps to this permission in the authorisation pipeline.</summary>
    public string PolicyName => PermissionPolicy.NameFor(Key);
}

/// <summary>What a permission is about: one branch's data, or the organisation as a whole.</summary>
/// <remarks>
/// The distinction is what makes one rule checkable: an organisation-scoped permission is granted only
/// to a role whose reach is the organisation. A shop that granted its Branch Manager the right to
/// publish a price list would have granted a branch role an organisation-wide power, and the matrix
/// test says so rather than leaving it to be noticed.
/// </remarks>
public enum PermissionScope
{
    /// <summary>The action touches the operational data of one branch.</summary>
    Branch = 0,

    /// <summary>The action configures or governs the organisation as a whole.</summary>
    Organisation = 1,

    /// <summary>
    /// The action is about the installation rather than about any branch's data — a feature flag, a
    /// module toggle, a system setting.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Organisation"/>, which reaches every branch's rows and is right to
    /// demand organisation-wide reach for it. This reaches none of them, so demanding that reach does
    /// not make the action safer; it only excludes principals who hold the permission and are assigned
    /// to no branch — which is exactly what the vendor super-user role is.
    /// </remarks>
    NotBranchOwned = 2,
}
