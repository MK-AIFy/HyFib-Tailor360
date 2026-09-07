using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Authentication;
using Tailor360.Modules.Identity.Application.Notifications;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Application.Passwords;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Application.Timing;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Recovery;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Recovery;

/// <summary>
/// Email-verified password recovery: ask for a link, then use it once to set a new password.
/// </summary>
/// <remarks>
/// <b>The rule that matters most is what recovery does not do.</b> Completing a reset changes the
/// password and nothing else. It does not clear the authenticator, does not spend or reissue recovery
/// codes, does not remove a passkey and does not mark the account as having satisfied multi-factor. A
/// person who has just proved they can read an inbox has proved exactly that; the sign-in that follows
/// still asks for the second factor. Making recovery clear the second factor would turn control of one
/// mailbox into full control of an account, which is precisely the attack multi-factor exists to stop.
/// Losing the second factor itself is a different journey with different evidence: an administrator
/// resets it under step-up, with a reason and out-of-band identity checks, per #23.
/// <para>
/// <b>The second rule is that a request tells the caller nothing.</b> Whether or not the address
/// belongs to an account, the response is the same object, the interface shows the same sentence, and
/// the call takes the same time — the timing floor is what closes the gap that identical wording leaves
/// open, and it works only because the message is queued rather than sent while the caller waits.
/// </para>
/// </remarks>
/// <param name="store">Account and token reads and writes.</param>
/// <param name="tokens">Recovery-token minting and digesting.</param>
/// <param name="passwords">The password policy.</param>
/// <param name="hashing">The password hasher.</param>
/// <param name="sessions">Session lifecycle, for ending every session the reset invalidates.</param>
/// <param name="mailer">Message rendering and queueing.</param>
/// <param name="uniformTime">The timing floor that hides which branch ran.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">Identifier generation.</param>
/// <param name="options">Recovery configuration.</param>
/// <param name="logger">Logger. Never receives an address, a token or a password.</param>
public sealed class PasswordRecoveryHandler(
    IIdentityStore store,
    IRecoveryTokenService tokens,
    IPasswordPolicyService passwords,
    IPasswordHashingService hashing,
    ISessionService sessions,
    IIdentityMailer mailer,
    IUniformResponseTime uniformTime,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    IOptions<RecoveryOptions> options,
    ILogger<PasswordRecoveryHandler> logger)
{
    /// <summary>The audit action recorded when a recovery link is issued.</summary>
    public const string RequestedAction = "identity.recovery.requested";

    /// <summary>The audit action recorded when a reset is completed.</summary>
    public const string CompletedAction = "identity.recovery.completed";

    private readonly RecoveryOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Issues a recovery link, if the address belongs to an account that can be recovered. Answers the
    /// same way if it does not.
    /// </summary>
    public Task<Result<RecoveryRequestAccepted>> RequestAsync(
        RequestPasswordRecovery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return uniformTime.RunAsync(
            _options.UniformResponseTime,
            token => IssueAsync(request.Email, token),
            cancellationToken);
    }

    /// <summary>
    /// Completes a reset. The password is judged before the link is spent, so that a password the
    /// policy rejects costs the person a retype rather than the whole link.
    /// </summary>
    /// <remarks>
    /// A rejected password reports the first rule it broke rather than all of them, because
    /// <c>Result</c> carries one error. The screen therefore states the whole policy up front, from
    /// <see cref="IPasswordPolicyService.Policy"/>, rather than relying on being told one rule at a
    /// time. Issue #53 owns the RFC 9457 field-error shape that carries several failures at once.
    /// </remarks>
    public async Task<Result<RecoveryCompleted>> ConfirmAsync(
        ConfirmPasswordRecovery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var digest = tokens.DigestOf(request.Token);
        if (digest is null)
        {
            return Result.Failure<RecoveryCompleted>(IdentityErrors.RecoveryTokenInvalid);
        }

        var token = await store.FindRecoveryTokenAsync(digest, cancellationToken);
        if (token is null || !token.IsUsable(clock.UtcNow))
        {
            return Result.Failure<RecoveryCompleted>(IdentityErrors.RecoveryTokenInvalid);
        }

        var user = await store.FindUserAsync(token.UserId, cancellationToken);
        if (user is null || user.Status is UserStatus.Deactivated)
        {
            return Result.Failure<RecoveryCompleted>(IdentityErrors.RecoveryTokenInvalid);
        }

        var judged = await passwords.CheckAsync(
            request.NewPassword, user.PasswordContext(), cancellationToken);

        if (!judged.IsAcceptable)
        {
            return Result.Failure<RecoveryCompleted>(judged.Failures[0]);
        }

        var spent = token.Consume(RecoveryPurpose.PasswordReset, clock.UtcNow);
        if (spent.IsFailure)
        {
            return Result.Failure<RecoveryCompleted>(IdentityErrors.RecoveryTokenInvalid);
        }

        var set = user.SetPassword(
            ids.NewId(),
            hashing.Hash(user, request.NewPassword!),
            hashing.AlgorithmName,
            clock.UtcNow,
            user.Id);

        if (set.IsFailure)
        {
            return Result.Failure<RecoveryCompleted>(set.Error);
        }

        // Whoever was signed in before the reset must not still be signed in after it. Note what is
        // not here: nothing touches the authenticator, the recovery codes or the passkeys.
        var revoked = await sessions.RevokeAllForUserAsync(
            user.Id, SessionEndReason.PasswordChanged, cancellationToken: cancellationToken);

        // The revocation and the new password commit together: the session service saves through the
        // same scoped context this store writes to, so there is no window in which the password has
        // changed and the old tickets are still live.
        await store.SaveChangesAsync(cancellationToken);

        var sessionsEnded = revoked.IsSuccess ? revoked.Value : 0;

        mailer.SendPasswordChangedAlert(user, clock.UtcNow);
        IdentityLog.RecoveryCompleted(logger, user.Id, sessionsEnded);

        // Recorded against the account itself. Nobody is authenticated on this request — completing a
        // reset is what proves anything at all — so an entry that deferred to the request's audit
        // context would attribute a password change to "system" and leave it out of the actor index.
        await AuthenticationAudit.RecordAsync(
            audit,
            CompletedAction,
            user.Id,
            $"A password was set through a recovery link; {sessionsEnded} session(s) were ended. The "
            + "account's second factor was not altered.",
            cancellationToken,
            new { SessionsEnded = sessionsEnded },
            actorId: user.Id,
            actorDisplayName: user.DisplayName);

        return Result.Success(new RecoveryCompleted(
            user.Id,
            user.HasConfirmedSecondFactor,
            sessionsEnded,
            user.MustChangePassword));
    }

    private async Task<Result<RecoveryRequestAccepted>> IssueAsync(
        string? email,
        CancellationToken cancellationToken)
    {
        // The token is minted before the account is known to exist, so the two paths do the same
        // cryptographic work. The floor hides the rest; this removes the largest thing under it.
        var minted = tokens.Issue();
        var normalised = email?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(normalised))
        {
            return Result.Success(new RecoveryRequestAccepted());
        }

        var user = await store.FindUserByNormalisedEmailAsync(normalised, cancellationToken);

        // Only an active account is recoverable. An invited one is completed through its invitation, and
        // a suspended or deactivated one is not something a link should bring back to life. All three
        // are answered exactly as an unknown address is.
        if (user is null || user.Status is not UserStatus.Active)
        {
            IdentityLog.RecoveryRequestedForUnknownAddress(logger);
            return Result.Success(new RecoveryRequestAccepted());
        }

        await store.InvalidateOutstandingRecoveryTokensAsync(
            user.Id, RecoveryPurpose.PasswordReset, clock.UtcNow, cancellationToken);

        var issued = RecoveryToken.Issue(
            ids.NewId(),
            user.Id,
            minted.TokenHash,
            RecoveryPurpose.PasswordReset,
            clock.UtcNow,
            _options.TokenLifetime);

        if (issued.IsFailure)
        {
            // A misconfigured lifetime is an operator error, not something to tell the caller about:
            // saying "we could not issue a link" for one address and nothing for another would be the
            // very difference this endpoint exists to hide.
            IdentityLog.TemplateRenderFailed(
                logger, IdentityMailer.PasswordRecoveryTemplate, issued.Error.Code);
            return Result.Success(new RecoveryRequestAccepted());
        }

        store.AddRecoveryToken(issued.Value);
        await store.SaveChangesAsync(cancellationToken);

        mailer.SendPasswordRecovery(user, minted.Value, _options.TokenLifetime);
        IdentityLog.RecoveryLinkIssued(logger, user.Id, _options.TokenLifetime.TotalMinutes);

        // Only the branch that actually issued a link writes an entry. An address nobody holds is not a
        // recovery request against any account, so there is no entity to record it against and nothing
        // an investigator could do with the row; the branch is still held to the timing floor, so the
        // extra write here is not what distinguishes the two.
        await AuthenticationAudit.RecordAsync(
            audit,
            RequestedAction,
            user.Id,
            "A password recovery link was issued for this account and emailed to its registered "
            + "address.",
            cancellationToken,
            new { LifetimeMinutes = _options.TokenLifetime.TotalMinutes },
            actorId: user.Id,
            actorDisplayName: user.DisplayName);

        return Result.Success(new RecoveryRequestAccepted());
    }
}
