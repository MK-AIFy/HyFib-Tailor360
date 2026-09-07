using Microsoft.Extensions.Logging;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Abuse;
using Tailor360.Modules.Identity.Application.Authentication;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Passkeys;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Passkeys;

/// <summary>
/// Registering and asserting passkeys — the phishing-resistant factor, and the only one that is both
/// factors at once.
/// </summary>
/// <remarks>
/// <para>
/// A completed assertion starts a session with the second factor already satisfied, and no challenge
/// follows. That is not a shortcut: the authenticator verified the person (a fingerprint, a face, a
/// device PIN) and the browser verified the origin, so the assertion carries strictly more evidence
/// than a password followed by a six-digit code, which proves possession of a phone and nothing about
/// who is holding it.
/// </para>
/// <para>
/// Registration requires an established session. There is no path here that both registers a new
/// credential and signs the holder in with it, because that path would let anyone who reached the
/// endpoint enrol their own authenticator against somebody else's account.
/// </para>
/// <para>
/// It requires more than a session on an account that already holds a factor. A passkey is both factors
/// at once, so registering one against somebody else's account is the whole attack in a single request:
/// a caller with a stolen password would register their own authenticator and then assert with it,
/// arriving at a session that has satisfied everything. Registration therefore refuses unless the
/// session making the request has satisfied a second factor — with the one exception that an account
/// holding no confirmed factor at all must be able to register its first.
/// </para>
/// </remarks>
/// <param name="ceremony">The WebAuthn ceremonies.</param>
/// <param name="directory">Account lookup.</param>
/// <param name="sessions">Session lifecycle.</param>
/// <param name="throttle">The per-address attempt counters.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">Identifier generation.</param>
/// <param name="logger">Logger. Never receives credential material beyond an identifier.</param>
public sealed class PasskeyHandler(
    IPasskeyCeremony ceremony,
    ISignInDirectory directory,
    ISessionService sessions,
    ICredentialThrottle throttle,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    ILogger<PasskeyHandler> logger)
{
    /// <summary>The audit action recorded when a passkey is registered.</summary>
    public const string RegisteredAction = "identity.passkey.registered";

    /// <summary>The audit action recorded when a passkey is removed.</summary>
    public const string RemovedAction = "identity.passkey.removed";

    /// <summary>The audit action recorded when a passkey signs someone in.</summary>
    public const string SignedInAction = "identity.passkey.signed-in";

    /// <summary>The audit action recorded when an assertion is refused.</summary>
    public const string RefusedAction = "identity.passkey.refused";

    /// <summary>Starts a registration for a signed-in holder.</summary>
    /// <param name="caller">The session making the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<PasskeyChallenge>> BeginRegistrationAsync(
        CallerSession caller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!ceremony.IsAvailable)
        {
            return Result.Failure<PasskeyChallenge>(PasskeyErrors.Unavailable);
        }

        var user = await directory.FindAsync(caller.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<PasskeyChallenge>(IdentityErrors.UserNotFound);
        }

        var usable = user.EnsureCanAuthenticate(clock.UtcNow);
        if (usable.IsFailure)
        {
            return Result.Failure<PasskeyChallenge>(usable.Error);
        }

        if (MayNotChangeFactors(user, caller))
        {
            return Result.Failure<PasskeyChallenge>(IdentityErrors.SecondFactorNotSatisfied);
        }

        return ceremony.BeginRegistration(
            new PasskeyUserDescriptor(user.Id, user.UserName, user.DisplayName),
            [.. user.Passkeys.Select(passkey => passkey.CredentialId)],
            caller.SessionId);
    }

    /// <summary>Completes a registration and stores the credential.</summary>
    /// <remarks>
    /// The factor check is repeated here rather than trusted from the ceremony that started it. The two
    /// requests are minutes apart and the account can change in between — an administrator can reset the
    /// second factor, another session can remove a passkey — and a ceremony handle is not evidence about
    /// the state of the account at the moment the credential is actually stored.
    /// </remarks>
    /// <param name="caller">The session making the request.</param>
    /// <param name="ceremonyId">The handle the challenge was issued under.</param>
    /// <param name="credentialJson">The browser's response, verbatim. Never logged.</param>
    /// <param name="label">What the holder will recognise the passkey by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<RegisteredPasskey>> CompleteRegistrationAsync(
        CallerSession caller,
        string? ceremonyId,
        string? credentialJson,
        string? label,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!ceremony.IsAvailable)
        {
            return Result.Failure<RegisteredPasskey>(PasskeyErrors.Unavailable);
        }

        var user = await directory.FindAsync(caller.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<RegisteredPasskey>(IdentityErrors.UserNotFound);
        }

        if (MayNotChangeFactors(user, caller))
        {
            return Result.Failure<RegisteredPasskey>(IdentityErrors.SecondFactorNotSatisfied);
        }

        var verified = await ceremony.CompleteRegistrationAsync(
            ceremonyId, credentialJson, caller.SessionId, cancellationToken);

        if (verified.IsFailure)
        {
            return Result.Failure<RegisteredPasskey>(verified.Error);
        }

        var now = clock.UtcNow;
        var credential = PasskeyCredential.Register(
            ids.NewId(),
            user.Id,
            verified.Value.CredentialId,
            verified.Value.PublicKey,
            verified.Value.AuthenticatorGuid,
            string.IsNullOrWhiteSpace(label) ? "Passkey" : label,
            verified.Value.SignatureCounter,
            now,
            verified.Value.Transports,
            verified.Value.IsBackupEligible,
            verified.Value.IsBackedUp);

        if (credential.IsFailure)
        {
            return Result.Failure<RegisteredPasskey>(credential.Error);
        }

        var registered = user.RegisterPasskey(credential.Value, now);
        if (registered.IsFailure)
        {
            return Result.Failure<RegisteredPasskey>(registered.Error);
        }

        await directory.SaveChangesAsync(cancellationToken);
        AuthenticationLog.PasskeyRegistered(logger, user.Id);

        await AuthenticationAudit.RecordAsync(
            audit,
            RegisteredAction,
            user.Id,
            "A passkey was registered against the account.",
            cancellationToken,
            new { credential.Value.Id, credential.Value.Label, credential.Value.IsBackedUp });

        return Result.Success(Describe(credential.Value));
    }

    /// <summary>Starts an assertion. No account is named: the authenticator chooses the credential.</summary>
    public Result<PasskeyChallenge> BeginAssertion(Guid? sessionId)
        => ceremony.IsAvailable
            ? ceremony.BeginAssertion([], sessionId)
            : Result.Failure<PasskeyChallenge>(PasskeyErrors.Unavailable);

    /// <summary>Completes an assertion and signs the holder in with both factors satisfied.</summary>
    public async Task<Result<PasskeySignInSucceeded>> CompleteAssertionAsync(
        PasskeyAssertionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ceremony.IsAvailable)
        {
            return Result.Failure<PasskeySignInSucceeded>(PasskeyErrors.Unavailable);
        }

        var asserted = ceremony.ReadAssertedCredentialId(command.CredentialJson);
        if (asserted.IsFailure)
        {
            return Refuse<PasskeySignInSucceeded>(command, asserted.Error);
        }

        var user = await directory.FindByPasskeyAsync(asserted.Value, cancellationToken);
        var credential = user?.FindPasskey(asserted.Value);

        if (user is null || credential is null)
        {
            return Refuse<PasskeySignInSucceeded>(command, PasskeyErrors.VerificationFailed);
        }

        var now = clock.UtcNow;
        var usable = user.EnsureCanAuthenticate(now);
        if (usable.IsFailure)
        {
            // Same answer as a signature that did not verify: whether an account exists and whether it
            // is suspended are not things an anonymous caller may learn.
            return Refuse<PasskeySignInSucceeded>(command, PasskeyErrors.VerificationFailed);
        }

        var verified = await ceremony.CompleteAssertionAsync(
            command.CeremonyId,
            command.CredentialJson,
            new StoredPasskey(
                credential.CredentialId, credential.PublicKey, credential.SignatureCounter, user.Id),
            command.PresentedSessionId,
            cancellationToken);

        if (verified.IsFailure)
        {
            return Refuse<PasskeySignInSucceeded>(command, verified.Error);
        }

        var used = credential.RecordUse(
            verified.Value.SignatureCounter, now, verified.Value.IsBackedUp);

        if (used.IsFailure)
        {
            // A counter that did not advance is the documented signal of a cloned authenticator. It is
            // audited under its own action, because it is the one passkey failure an operator should
            // look at rather than dismiss as a fumbled sign-in.
            await AuthenticationAudit.RecordAsync(
                audit,
                RefusedAction,
                user.Id,
                "A passkey assertion was refused because its use counter did not advance, which can "
                + "mean the credential has been copied out of its authenticator.",
                cancellationToken);

            return Result.Failure<PasskeySignInSucceeded>(used.Error);
        }

        user.RecordSuccessfulSignIn(now);

        var started = await sessions.StartAsync(
            new StartSessionRequest(
                user.Id,
                command.DeviceLabel,
                command.IpAddress,
                command.UserAgent,
                MfaSatisfied: true,
                TrustedDeviceId: null,
                SupersedesSessionId: command.PresentedSessionId),
            cancellationToken);

        if (started.IsFailure)
        {
            return Result.Failure<PasskeySignInSucceeded>(started.Error);
        }

        throttle.RecordSuccess(CredentialAction.SignIn, user.Id.ToString("n"), command.IpAddress);

        await AuthenticationAudit.RecordAsync(
            audit,
            SignedInAction,
            user.Id,
            "Signed in with a passkey, which satisfies both factors.",
            cancellationToken,
            new { started.Value.SessionId, PasskeyId = credential.Id });

        return Result.Success(new PasskeySignInSucceeded(
            started.Value,
            user.Id,
            user.DisplayName,
            user.MustChangePassword,
            new AvailableFactors(
                user.Totp is { IsConfirmed: true },
                user.UnusedRecoveryCodeCount > 0,
                user.Passkeys.Count > 0)));
    }

    /// <summary>Lists the account's registered passkeys.</summary>
    public async Task<IReadOnlyList<RegisteredPasskey>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await directory.FindAsync(userId, cancellationToken);
        return user is null ? [] : [.. user.Passkeys.Select(Describe)];
    }

    /// <summary>Removes one passkey, refusing to leave the account with no second factor at all.</summary>
    /// <param name="userId">The account, taken from the session and never from the request.</param>
    /// <param name="passkeyId">The passkey to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> RemoveAsync(
        Guid userId,
        Guid passkeyId,
        CancellationToken cancellationToken = default)
    {
        var user = await directory.FindAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        var removed = user.RemovePasskey(passkeyId, clock.UtcNow);
        if (removed.IsFailure)
        {
            return removed;
        }

        await directory.SaveChangesAsync(cancellationToken);

        await AuthenticationAudit.RecordAsync(
            audit,
            RemovedAction,
            user.Id,
            "A passkey was removed from the account.",
            cancellationToken,
            new { PasskeyId = passkeyId });

        return Result.Success();
    }

    /// <summary>
    /// True when this caller may not add a factor: the account already holds one and the session making
    /// the request has not satisfied it. An account with no confirmed factor is the narrow exception,
    /// because somebody has to be able to register the first passkey.
    /// </summary>
    private static bool MayNotChangeFactors(StaffUser user, CallerSession caller)
        => user.HasConfirmedSecondFactor && !caller.SecondFactorSatisfied;

    private Result<TValue> Refuse<TValue>(PasskeyAssertionCommand command, Error error)
    {
        throttle.RecordFailure(CredentialAction.SignIn, accountKey: null, command.IpAddress);
        AuthenticationLog.PasskeyAssertionRefused(logger);

        return Result.Failure<TValue>(error);
    }

    private static RegisteredPasskey Describe(PasskeyCredential credential) => new(
        credential.Id,
        credential.Label,
        credential.CreatedAt,
        credential.LastUsedAt,
        credential.IsBackedUp);
}

/// <summary>What completing a passkey assertion supplies.</summary>
/// <param name="CeremonyId">The handle the challenge was issued under.</param>
/// <param name="CredentialJson">The browser's response, verbatim. Never logged.</param>
/// <param name="DeviceLabel">What the holder will recognise the session by.</param>
/// <param name="IpAddress">The client address, for the inventory and the throttle.</param>
/// <param name="UserAgent">The user agent, for the inventory.</param>
/// <param name="PresentedSessionId">The session held beforehand, revoked as the new one is created.</param>
public sealed record PasskeyAssertionCommand(
    string? CeremonyId,
    string? CredentialJson,
    string DeviceLabel,
    string? IpAddress = null,
    string? UserAgent = null,
    Guid? PresentedSessionId = null);
