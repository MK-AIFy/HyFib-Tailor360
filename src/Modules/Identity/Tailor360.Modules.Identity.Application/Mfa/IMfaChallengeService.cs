using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Mfa;

/// <summary>
/// Answers a multi-factor challenge with an authenticator code or a recovery code, and records the
/// consequences on the account.
/// </summary>
/// <remarks>
/// The sign-in flow calls this rather than checking a code itself, because "was the code right" is only
/// half the question. A time-based code that is right must also not be one already accepted, and a
/// recovery code that is right must be spent so it cannot be used twice. Both of those are writes, and
/// leaving them to the caller would mean the difference between a one-time password and a password was
/// whichever caller remembered.
/// </remarks>
public interface IMfaChallengeService
{
    /// <summary>Checks one answer to a challenge and applies its effect on the account.</summary>
    /// <param name="userId">The account being challenged.</param>
    /// <param name="factor">Which factor the answer is for.</param>
    /// <param name="code">The code as typed. Never logged.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<MfaChallengeOutcome>> VerifyAsync(
        Guid userId,
        MfaFactor factor,
        string? code,
        CancellationToken cancellationToken = default);
}
