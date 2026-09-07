using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Abuse;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Application.Mfa;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Application.Timing;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Authentication;

/// <summary>
/// The first factor: check the password, decide what is still owed, and issue the session the rest of
/// the sign-in happens under.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal is the same refusal.</b> An unknown sign-in name, a wrong password, a suspended
/// account, an account that never completed its invitation and one that is locked out all return
/// <c>identity.invalid-credentials</c> with the same wording. The distinctions are real, and the audit
/// trail and the administration screens keep them; handing them to whoever is typing turns the sign-in
/// form into a directory of who works here and which of them are currently locked out.
/// </para>
/// <para>
/// <b>The unknown account costs the same as the known one.</b> When no account matches, the handler
/// still verifies the submitted password against a decoy hash made with the configured parameters.
/// Without that, an unknown name returns in under a millisecond and a known one takes as long as
/// Argon2id takes, and the difference is a reliable account oracle no amount of identical wording will
/// hide. The decoy is derived once for the process rather than once per request, so it equalises the
/// cost instead of inverting it; and because equal is not the same as indistinguishable — the known
/// branch still loads an aggregate and may write a failure count — the whole call is held to a floor,
/// exactly as the recovery request is.
/// </para>
/// <para>
/// <b>Three controls, in order.</b> The rate-limit policy on the endpoint bounds requests from one
/// address; the throttle bounds this address against this account and runs <em>before</em> the hash, so
/// that a caller cannot spend the server's CPU on passwords that were never going to be right; the
/// progressive lockout on the account survives a restart and bounds the attack that comes from many
/// addresses at once. Removing any one of the three leaves a gap the other two do not cover.
/// </para>
/// </remarks>
/// <param name="directory">Account lookup for the sign-in path.</param>
/// <param name="access">The account's effective roles and permissions, for the multi-factor decision.</param>
/// <param name="hashing">Password hashing and verification.</param>
/// <param name="sessions">Session lifecycle.</param>
/// <param name="throttle">The per-account and per-address attempt counters.</param>
/// <param name="mfaPolicy">Whether this account must hold a second factor.</param>
/// <param name="deviceTokens">Digests the remembered-device cookie value presented by the browser.</param>
/// <param name="decoy">The hash an unusable account is verified against, so both paths cost the same.</param>
/// <param name="uniformTime">The timing floor that hides which branch ran.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="lockoutOptions">The progressive lockout policy.</param>
/// <param name="trustedDeviceOptions">Whether a device may be remembered.</param>
/// <param name="signInOptions">The sign-in timing floor.</param>
/// <param name="logger">Logger. Never receives a credential.</param>
public sealed class SignInHandler(
    ISignInDirectory directory,
    IUserAccessQuery access,
    IPasswordHashingService hashing,
    ISessionService sessions,
    ICredentialThrottle throttle,
    IMfaRequirementPolicy mfaPolicy,
    IOpaqueTokenFactory deviceTokens,
    IDecoyCredential decoy,
    IUniformResponseTime uniformTime,
    IAuditWriter audit,
    IClock clock,
    IOptions<AccountLockoutOptions> lockoutOptions,
    IOptions<TrustedDeviceOptions> trustedDeviceOptions,
    IOptions<SignInTimingOptions> signInOptions,
    ILogger<SignInHandler> logger)
{
    /// <summary>The audit action recorded for a successful first factor.</summary>
    public const string SucceededAction = "identity.sign-in.succeeded";

    /// <summary>The audit action recorded for a refused sign-in.</summary>
    public const string FailedAction = "identity.sign-in.failed";

    /// <summary>Answers the first factor.</summary>
    /// <param name="command">What was submitted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<SignInSucceeded>> SignInAsync(
        SignInCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return uniformTime.RunAsync(
            signInOptions.Value.UniformResponseTime,
            token => AuthenticateAsync(command, token),
            cancellationToken);
    }

    private async Task<Result<SignInSucceeded>> AuthenticateAsync(
        SignInCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var accountKey = SignInIdentifier.Normalise(command.Identifier);
        var clientKey = command.IpAddress;

        var user = await directory.FindForSignInAsync(command.Identifier, cancellationToken);

        // Counted again on the account the directory resolved, because a sign-in name and an address
        // are two spellings of one account and the endpoint could only count what was typed. Without
        // this an attacker alternating the two spellings gets twice the configured budget.
        if (user is not null && !throttle.Check(
                CredentialAction.SignIn, user.Id.ToString("n"), clientKey).IsAllowed)
        {
            // Answered as a refused credential rather than as a refusal to try, for the same reason
            // every other branch here is: which accounts are being throttled is not something an
            // anonymous caller may learn.
            VerifyAgainstDecoy(command.Password);
            return Result.Failure<SignInSucceeded>(IdentityErrors.InvalidCredentials);
        }

        if (user?.Password is not { } credential)
        {
            // The decoy verification runs whether the account is missing, still invited or somehow
            // without a credential, so all three cost what a real verification costs.
            VerifyAgainstDecoy(command.Password);
            RecordAttemptFailure(accountKey, user, clientKey);
            AuthenticationLog.SignInRejectedWithoutAccount(logger);

            await RecordAsync(
                FailedAction,
                user?.Id ?? Guid.Empty,
                "A sign-in attempt named an account that does not exist or cannot sign in.",
                cancellationToken);

            return Result.Failure<SignInSucceeded>(IdentityErrors.InvalidCredentials);
        }

        var usable = user.EnsureCanAuthenticate(now);
        if (usable.IsFailure)
        {
            VerifyAgainstDecoy(command.Password);
            RecordAttemptFailure(accountKey, user, clientKey);

            if (user.IsLockedOut(now))
            {
                AuthenticationLog.AccountLockedOut(logger, user.Id);
            }

            await RecordAsync(
                FailedAction,
                user.Id,
                $"A sign-in attempt was refused because the account is not usable ({usable.Error.Code}).",
                cancellationToken,
                actor: user);

            return Result.Failure<SignInSucceeded>(IdentityErrors.InvalidCredentials);
        }

        var verification = hashing.Verify(user, credential.EncodedHash, command.Password ?? string.Empty);

        if (verification is PasswordVerification.Failed)
        {
            user.RecordFailedSignIn(lockoutOptions.Value.ToPolicy(), now);
            await directory.SaveChangesAsync(cancellationToken);

            RecordAttemptFailure(accountKey, user, clientKey);
            AuthenticationLog.SignInFailed(logger, user.Id, user.FailedSignInCount);

            await RecordAsync(
                FailedAction,
                user.Id,
                $"A sign-in attempt supplied the wrong password; {user.FailedSignInCount} consecutive "
                + "failure(s) recorded.",
                cancellationToken,
                actor: user);

            return Result.Failure<SignInSucceeded>(IdentityErrors.InvalidCredentials);
        }

        if (verification is PasswordVerification.SucceededButNeedsRehash)
        {
            // The hash is not replaced here. A rehash is a write on the credential and sign-in is the
            // one path where an attacker chooses how often it runs; marking it lets the next password
            // change do the work, which is the only place the new hash can be produced from a password
            // the holder has actually just typed for that purpose.
            credential.MarkRehashRequired();
            AuthenticationLog.PasswordRehashRequired(logger, user.Id);
        }

        credential.RecordVerification(now);
        return await EstablishAsync(user, command, now, cancellationToken);
    }

    private async Task<Result<SignInSucceeded>> EstablishAsync(
        StaffUser user,
        SignInCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var remembered = FindRememberedDevice(user, command.TrustedDeviceToken, now);
        var step = await NextStepAsync(user, remembered is not null, cancellationToken);

        user.RecordSuccessfulSignIn(now);

        var started = await sessions.StartAsync(
            new StartSessionRequest(
                user.Id,
                command.DeviceLabel,
                command.IpAddress,
                command.UserAgent,
                MfaSatisfied: false,
                TrustedDeviceId: remembered?.Id,
                SupersedesSessionId: command.PresentedSessionId,

                // What the sign-in still owes is recorded on the session, not left to the client to
                // remember. A session that owes a step reaches only the endpoints that answer it, so a
                // caller who simply stops following the flow gets a session that can do nothing rather
                // than one that can do everything.
                PendingStep: Pending(step)),
            cancellationToken);

        if (started.IsFailure)
        {
            return Result.Failure<SignInSucceeded>(started.Error);
        }

        throttle.RecordSuccess(
            CredentialAction.SignIn, SignInIdentifier.Normalise(command.Identifier), command.IpAddress);
        throttle.RecordSuccess(CredentialAction.SignIn, user.Id.ToString("n"), command.IpAddress);

        if (remembered is not null)
        {
            AuthenticationLog.TrustedDeviceUsed(logger, user.Id);
        }

        AuthenticationLog.SignInSucceeded(logger, user.Id, step);

        await RecordAsync(
            SucceededAction,
            user.Id,
            step is SignInStep.Complete
                ? "Signed in."
                : $"Answered the first factor; {Describe(step)} still required.",
            cancellationToken,
            new
            {
                started.Value.SessionId,
                Step = step.ToString(),
                UsedRememberedDevice = remembered is not null,
            },
            actor: user);

        return Result.Success(new SignInSucceeded(
            step,
            started.Value,
            user.Id,
            user.DisplayName,
            user.MustChangePassword,
            Factors(user)));
    }

    /// <summary>
    /// What the account still owes. A confirmed second factor is always challenged, whether or not the
    /// account's permissions would have required one: someone who has taken the trouble to enrol must
    /// not find that the challenge is skipped because their role does not demand it.
    /// </summary>
    /// <remarks>
    /// The enrolment demand is decided from the account's <em>effective</em> permissions, not from its
    /// role names, which is what makes a custom role safe: a shop that invents a role and grants it a
    /// flagged permission has created an account that must enrol, and nobody had to remember to add the
    /// new role to a configuration list for that to be true. The access read costs one query and only
    /// on the branch where the account has no factor yet.
    /// </remarks>
    private async Task<SignInStep> NextStepAsync(
        StaffUser user,
        bool deviceRemembered,
        CancellationToken cancellationToken)
    {
        if (user.HasConfirmedSecondFactor)
        {
            return deviceRemembered ? SignInStep.Complete : SignInStep.MultiFactorRequired;
        }

        var effective = await access.ResolveAsync(user.Id, cancellationToken);

        return mfaPolicy.IsRequiredFor(effective.RoleNames, effective.Permissions)
            ? SignInStep.MultiFactorEnrolmentRequired
            : SignInStep.Complete;
    }

    /// <summary>What the session owes, from what the caller was told to do next.</summary>
    private static SessionPendingStep Pending(SignInStep step) => step switch
    {
        SignInStep.MultiFactorRequired => SessionPendingStep.MultiFactorChallenge,
        SignInStep.MultiFactorEnrolmentRequired => SessionPendingStep.MultiFactorEnrolment,
        _ => SessionPendingStep.None,
    };

    private TrustedDevice? FindRememberedDevice(
        StaffUser user,
        string? presentedToken,
        DateTimeOffset now)
    {
        if (!trustedDeviceOptions.Value.Enabled
            || presentedToken is null
            || !user.HasConfirmedSecondFactor)
        {
            return null;
        }

        var device = user.FindUsableDevice(deviceTokens.DigestOf(presentedToken), now);
        device?.RecordUse(now);

        return device;
    }

    private static AvailableFactors Factors(StaffUser user) => new(
        user.Totp is { IsConfirmed: true },
        user.UnusedRecoveryCodeCount > 0,
        user.Passkeys.Count > 0);

    private static string Describe(SignInStep step) => step switch
    {
        SignInStep.MultiFactorRequired => "a second factor",
        SignInStep.MultiFactorEnrolmentRequired => "second-factor enrolment",
        _ => "nothing",
    };

    /// <summary>
    /// Records one entry, naming the account as the actor.
    /// </summary>
    /// <remarks>
    /// The actor is passed explicitly because on a login request nobody is authenticated yet — the
    /// session is created inside this handler — so an entry that deferred to the request's audit context
    /// would be attributed to <c>system</c> and would not be found by a query for everything a given
    /// person did, which is the query an investigation starts from. An attempt against an identifier
    /// that matches no account stays anonymous, because there is nobody to name.
    /// </remarks>
    private Task RecordAsync(
        string action,
        Guid userId,
        string summary,
        CancellationToken cancellationToken,
        object? after = null,
        StaffUser? actor = null)
        => AuthenticationAudit.RecordAsync(
            audit,
            action,
            userId,
            summary,
            cancellationToken,
            after,
            actorId: actor?.Id,
            actorDisplayName: actor?.DisplayName);

    /// <summary>
    /// Counts one failure against what was typed and, when the account is known, against the account
    /// itself. Counting both is what makes a sign-in name and an address share one budget rather than
    /// hold two.
    /// </summary>
    private void RecordAttemptFailure(string? accountKey, StaffUser? user, string? clientKey)
    {
        throttle.RecordFailure(CredentialAction.SignIn, accountKey, clientKey);

        if (user is not null)
        {
            // The address counter would otherwise be incremented twice for one attempt, so the second
            // call names no client.
            throttle.RecordFailure(CredentialAction.SignIn, user.Id.ToString("n"), clientKey: null);
        }
    }

    private void VerifyAgainstDecoy(string? password)
        => _ = hashing.Verify(decoy.User, decoy.EncodedHash, password ?? string.Empty);
}
