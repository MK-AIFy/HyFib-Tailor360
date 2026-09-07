using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Credentials;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Passwords;

/// <summary>
/// The one place a password is judged, so that setting a password at an invitation, at a recovery and
/// at a routine change cannot drift into three different standards.
/// </summary>
/// <remarks>
/// The order of the two checks matters for cost, not for correctness: the local rules are free, the
/// breached-list lookup is not, so a password that is already too short is never looked up.
/// </remarks>
/// <param name="options">The configured policy.</param>
/// <param name="breachedPasswords">The breached-password list, or the no-op default.</param>
public sealed class PasswordPolicyService(
    IOptions<PasswordPolicyOptions> options,
    IBreachedPasswordChecker breachedPasswords) : IPasswordPolicyService
{
    private readonly PasswordPolicyOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public PasswordPolicy Policy => _options.ToPolicy();

    /// <inheritdoc />
    public async ValueTask<PasswordPolicyResult> CheckAsync(
        string? candidate,
        PasswordContext account,
        CancellationToken cancellationToken = default)
    {
        var local = Policy.Check(candidate, account);
        if (local.IsAcceptable is false || !_options.CheckBreachedPasswords || candidate is null)
        {
            return local;
        }

        var breached = await breachedPasswords.IsBreachedAsync(candidate, cancellationToken);
        if (!breached)
        {
            return local;
        }

        return new PasswordPolicyResult([.. local.Failures, IdentityErrors.PasswordBreached]);
    }
}
