using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Mfa;

/// <summary>
/// The shipped challenge service: check the answer, then write down what checking it cost.
/// </summary>
/// <remarks>
/// Both factors fail with the same error whatever went wrong. An authenticator code that is simply
/// wrong, one from a step already accepted, and one for an account with no authenticator at all are
/// indistinguishable to the caller; so are an unknown recovery code and one already spent. The
/// distinctions are real and the audit trail keeps them, but handing them to whoever is typing would
/// tell an attacker which of their guesses was close.
/// </remarks>
/// <param name="store">Account reads and writes.</param>
/// <param name="totp">Code checking.</param>
/// <param name="recoveryCodes">Recovery-code digesting.</param>
/// <param name="protector">Unwraps the stored shared secret.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Multi-factor configuration.</param>
/// <param name="logger">Logger. Never receives a code.</param>
public sealed class MfaChallengeService(
    IIdentityStore store,
    ITotpService totp,
    IRecoveryCodeService recoveryCodes,
    ISecretProtector protector,
    IClock clock,
    IOptions<MfaOptions> options,
    ILogger<MfaChallengeService> logger) : IMfaChallengeService
{
    private readonly MfaOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<Result<MfaChallengeOutcome>> VerifyAsync(
        Guid userId,
        MfaFactor factor,
        string? code,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<MfaChallengeOutcome>(IdentityErrors.UserNotFound);
        }

        var accepted = factor switch
        {
            MfaFactor.Totp => VerifyTotp(user, code),
            MfaFactor.RecoveryCode => user.RedeemRecoveryCode(recoveryCodes.DigestOf(code), clock.UtcNow),
            _ => Result.Failure(IdentityErrors.MfaCodeInvalid),
        };

        if (accepted.IsFailure)
        {
            return Result.Failure<MfaChallengeOutcome>(accepted.Error);
        }

        // The answer was accepted against the row as it was read. Another request carrying the same
        // answer may have written first, in which case this one accepted a code that has already been
        // spent — so it is refused here, with the same error a wrong code gets, rather than at the
        // point where it would have become a second use of a one-time credential.
        var written = await store.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            IdentityLog.ChallengeAnswerSuperseded(logger, user.Id, factor.ToString());
            return Result.Failure<MfaChallengeOutcome>(IdentityErrors.MfaCodeInvalid);
        }

        var remaining = user.UnusedRecoveryCodeCount;
        var reissue = user.HasConfirmedSecondFactor && remaining <= _options.RecoveryCodeReissueThreshold;

        if (reissue)
        {
            IdentityLog.RecoveryCodesRunningLow(logger, user.Id, remaining);
        }

        return Result.Success(new MfaChallengeOutcome(factor, remaining, reissue));
    }

    private Result VerifyTotp(StaffUser user, string? code)
    {
        if (user.Totp is not { IsConfirmed: true } enrolment)
        {
            return Result.Failure(IdentityErrors.MfaCodeInvalid);
        }

        var secret = protector.Unprotect(enrolment.ProtectedSecret);
        if (secret.IsFailure)
        {
            IdentityLog.TotpSecretUnreadable(logger, user.Id);
            return Result.Failure(secret.Error);
        }

        var verified = totp.Verify(
            secret.Value,
            code,
            clock.UtcNow,
            enrolment.Digits,
            enrolment.PeriodSeconds,
            _options.DriftSteps);

        if (!verified.IsValid)
        {
            return Result.Failure(IdentityErrors.MfaCodeInvalid);
        }

        // A time-based code is valid for a whole step, so accepting one twice would make it a
        // password rather than a one-time password. The enrolment refuses a step it has already seen.
        var stepAccepted = enrolment.AcceptStep(verified.Step, clock.UtcNow);
        return stepAccepted.IsFailure ? Result.Failure(IdentityErrors.MfaCodeInvalid) : Result.Success();
    }
}
