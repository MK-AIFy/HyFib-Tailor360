namespace Tailor360.Modules.Identity.Application.Mfa;

/// <summary>
/// Decides whether an account must hold a second factor, from the roles and permissions it actually has.
/// </summary>
/// <remarks>
/// The decision is taken from the <em>effective</em> permission set rather than from the role name
/// alone, which is the difference that matters once custom roles exist (#24): a shop that invents a
/// "Senior cashier" role and grants it <c>billing.post_invoice</c> has created an account that must
/// have a second factor, and nobody had to remember to add the role to a list for that to be true.
/// </remarks>
public interface IMfaRequirementPolicy
{
    /// <summary>True when an account holding these roles and permissions must enrol a second factor.</summary>
    /// <param name="roles">The role names granted to the account.</param>
    /// <param name="permissions">The permission keys the account effectively holds.</param>
    bool IsRequiredFor(IEnumerable<string> roles, IEnumerable<string> permissions);

    /// <summary>
    /// Explains the decision in one short phrase, for the enrolment screen and the audit entry. Returns
    /// <see langword="null"/> when no second factor is required.
    /// </summary>
    string? ReasonFor(IEnumerable<string> roles, IEnumerable<string> permissions);
}
