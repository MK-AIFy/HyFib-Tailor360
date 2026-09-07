using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Abuse;
using Tailor360.Modules.Identity.Application.Mfa;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Authentication;

/// <summary>
/// The second factor: check the answer, replace the session with one that has satisfied it, and — if
/// the holder asked and the deployment allows it — remember the device.
/// </summary>
/// <remarks>
/// <para>
/// The session is <b>rotated</b> rather than updated in place. A session that gains the right to reach
/// billing and administration is a different session in every sense that matters, and the value in the
/// browser changes with it, so a ticket captured while the account was only half signed in is worth
/// nothing once the challenge is answered.
/// </para>
/// <para>
/// Answering wrongly is counted against both the account and the address. There is no separate lockout
/// for the second factor: a caller who has the password and is guessing six digits is bounded by the
/// throttle, and locking the account instead would let anyone who had already stolen a password lock
/// its owner out at will.
/// </para>
/// </remarks>
/// <param name="challenges">Checks the answer and applies its effect on the account.</param>
/// <param name="directory">Account lookup.</param>
/// <param name="sessions">Session lifecycle.</param>
/// <param name="throttle">The per-account and per-address attempt counters.</param>
/// <param name="deviceTokens">Mints the remembered-device cookie value.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">Identifier generation.</param>
/// <param name="options">Whether and for how long a device may be remembered.</param>
/// <param name="logger">Logger. Never receives a code.</param>
public sealed class MultiFactorSignInHandler(
    IMfaChallengeService challenges,
    ISignInDirectory directory,
    ISessionService sessions,
    ICredentialThrottle throttle,
    IOpaqueTokenFactory deviceTokens,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    IOptions<TrustedDeviceOptions> options,
    ILogger<MultiFactorSignInHandler> logger)
{
    /// <summary>The audit action recorded when a challenge is answered correctly.</summary>
    public const string SatisfiedAction = "identity.mfa.satisfied";

    /// <summary>The audit action recorded when a challenge is refused.</summary>
    public const string RefusedAction = "identity.mfa.refused";

    /// <summary>The audit action recorded when a device is remembered.</summary>
    public const string DeviceRememberedAction = "identity.trusted-device.remembered";

    /// <summary>Answers a challenge on behalf of the session that passed the first factor.</summary>
    public async Task<Result<MultiFactorSatisfied>> AnswerAsync(
        MultiFactorAnswer answer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var accountKey = answer.UserId.ToString("n");

        var verified = await challenges.VerifyAsync(
            answer.UserId, answer.Factor, answer.Code, cancellationToken);

        if (verified.IsFailure)
        {
            throttle.RecordFailure(CredentialAction.MultiFactorChallenge, accountKey, answer.IpAddress);
            AuthenticationLog.MultiFactorRefused(logger, answer.UserId);

            await AuthenticationAudit.RecordAsync(
                audit,
                RefusedAction,
                answer.UserId,
                $"A second-factor answer was refused ({verified.Error.Code}).",
                cancellationToken,
                new { Factor = answer.Factor.ToString() });

            // The domain distinguishes a wrong authenticator code from a spent recovery code; the
            // caller is told neither, for the same reason a wrong password and an unknown account read
            // the same.
            return Result.Failure<MultiFactorSatisfied>(IdentityErrors.MfaCodeInvalid);
        }

        var rotated = await sessions.RotateAsync(
            answer.SessionId, SessionRotationReason.MultiFactorSatisfied, cancellationToken);

        if (rotated.IsFailure)
        {
            return Result.Failure<MultiFactorSatisfied>(rotated.Error);
        }

        var remembered = await RememberDeviceAsync(answer, cancellationToken);

        throttle.RecordSuccess(CredentialAction.MultiFactorChallenge, accountKey, answer.IpAddress);
        AuthenticationLog.MultiFactorSatisfied(logger, answer.UserId);

        await AuthenticationAudit.RecordAsync(
            audit,
            SatisfiedAction,
            answer.UserId,
            "A second factor was satisfied and the session was replaced.",
            cancellationToken,
            new
            {
                Factor = answer.Factor.ToString(),
                rotated.Value.SessionId,
                verified.Value.RemainingRecoveryCodes,
                DeviceRemembered = remembered is not null,
            });

        return Result.Success(new MultiFactorSatisfied(
            rotated.Value,
            verified.Value.RemainingRecoveryCodes,
            verified.Value.ShouldReissueRecoveryCodes,
            remembered?.Value,
            remembered?.ExpiresAt));
    }

    private async Task<RememberedDevice?> RememberDeviceAsync(
        MultiFactorAnswer answer,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!answer.RememberDevice || !settings.Enabled)
        {
            return null;
        }

        var user = await directory.FindAsync(answer.UserId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var minted = deviceTokens.Issue();
        var now = clock.UtcNow;

        var device = user.RememberDevice(
            ids.NewId(),
            minted.TokenHash,
            string.IsNullOrWhiteSpace(answer.DeviceLabel) ? "This device" : answer.DeviceLabel,
            now,
            settings.Lifetime);

        if (device.IsFailure)
        {
            // Reaching the device ceiling, or a lifetime the domain refuses, must not undo a
            // correctly answered challenge. The holder is signed in; the device is simply not
            // remembered, and the inventory shows them why.
            return null;
        }

        await directory.SaveChangesAsync(cancellationToken);
        AuthenticationLog.TrustedDeviceRemembered(logger, user.Id, device.Value.ExpiresAt);

        await AuthenticationAudit.RecordAsync(
            audit,
            DeviceRememberedAction,
            user.Id,
            "A device was remembered so that later sign-ins from it need only the password.",
            cancellationToken,
            new { device.Value.Id, device.Value.Label, device.Value.ExpiresAt });

        return new RememberedDevice(minted.Value, device.Value.ExpiresAt);
    }

    private sealed record RememberedDevice(string Value, DateTimeOffset ExpiresAt);
}
