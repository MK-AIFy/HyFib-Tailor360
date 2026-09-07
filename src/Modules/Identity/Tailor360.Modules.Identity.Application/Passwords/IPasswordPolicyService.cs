using Tailor360.Modules.Identity.Domain.Credentials;

namespace Tailor360.Modules.Identity.Application.Passwords;

/// <summary>
/// Applies the whole password policy — the rules the domain can decide alone, plus the breached-list
/// check that needs a data source — and reports every failure at once.
/// </summary>
public interface IPasswordPolicyService
{
    /// <summary>The rules currently in force, so a screen can state them before anyone types.</summary>
    PasswordPolicy Policy { get; }

    /// <summary>Checks a candidate password against every rule.</summary>
    /// <param name="candidate">The proposed password. Never logged, never stored.</param>
    /// <param name="account">The account's own text, which a password must not simply repeat.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<PasswordPolicyResult> CheckAsync(
        string? candidate,
        PasswordContext account,
        CancellationToken cancellationToken = default);
}
