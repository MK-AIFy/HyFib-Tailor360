using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Authentication;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Mfa;

/// <summary>
/// Enrolling an authenticator: start, confirm, and print a fresh sheet of recovery codes.
/// </summary>
/// <remarks>
/// <para>
/// Enrolment is two steps rather than one because a secret that has never produced a working code is
/// not a second factor — it is a way of locking someone out of their own account. Starting an
/// enrolment stores the secret and nothing else changes; only a code the holder reads off their own
/// authenticator turns it into a factor, and only then are recovery codes issued.
/// </para>
/// <para>
/// The recovery codes are issued at confirmation and returned exactly once, which is the only moment
/// they exist in a readable form anywhere. They are never emailed and never logged.
/// </para>
/// <para>
/// <b>Every method here takes the caller's session rather than a bare identifier</b>, because each of
/// them either mints credential material or replaces a factor, and the rule that governs all three is
/// about the session and not about the account: a caller who has proved only a password may enrol the
/// <em>first</em> factor on an account that has none, and may do nothing else. Deciding that from the
/// account's own state — <see cref="StaffUser.HasConfirmedSecondFactor"/> — rather than from a step the
/// client claims to have reached is what makes it a control instead of a suggestion.
/// </para>
/// </remarks>
/// <param name="store">Account reads and writes.</param>
/// <param name="totp">Secret generation and code checking.</param>
/// <param name="recoveryCodes">Recovery-code generation.</param>
/// <param name="protector">Wraps the shared secret before it is stored.</param>
/// <param name="sessions">Session lifecycle, for the rotation a confirmed enrolment earns.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">Identifier generation.</param>
/// <param name="options">Multi-factor configuration.</param>
/// <param name="logger">Logger. Never receives a secret or a code.</param>
public sealed class TotpEnrolmentHandler(
    IIdentityStore store,
    ITotpService totp,
    IRecoveryCodeService recoveryCodes,
    ISecretProtector protector,
    ISessionService sessions,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    IOptions<MfaOptions> options,
    ILogger<TotpEnrolmentHandler> logger)
{
    /// <summary>The audit action recorded when an enrolment starts.</summary>
    public const string EnrolmentStartedAction = "identity.mfa.enrolment-started";

    /// <summary>The audit action recorded when an enrolment is confirmed.</summary>
    public const string EnrolmentConfirmedAction = "identity.mfa.enrolment-confirmed";

    /// <summary>The audit action recorded when a fresh sheet of recovery codes is printed.</summary>
    public const string RecoveryCodesIssuedAction = "identity.mfa.recovery-codes-issued";

    private readonly MfaOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Starts an enrolment and returns what the screen shows. Calling it again before confirmation
    /// replaces the pending secret, which is what happens when someone abandons the screen and comes
    /// back.
    /// </summary>
    /// <remarks>
    /// It refuses on an account that already holds a confirmed factor unless the caller's session has
    /// satisfied one, so an attacker who has taken over a live session with a stolen password cannot
    /// quietly add an authenticator of their own beside the holder's — nor swap one in on an account
    /// whose only factor is a passkey, which the "already enrolled" check alone would have missed.
    /// </remarks>
    /// <param name="caller">The session making the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<TotpEnrolmentStarted>> BeginAsync(
        CallerSession caller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var user = await store.FindUserAsync(caller.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<TotpEnrolmentStarted>(IdentityErrors.UserNotFound);
        }

        var usable = user.EnsureCanAuthenticate(clock.UtcNow);
        if (usable.IsFailure)
        {
            return Result.Failure<TotpEnrolmentStarted>(usable.Error);
        }

        if (MayNotChangeFactors(user, caller))
        {
            return Result.Failure<TotpEnrolmentStarted>(IdentityErrors.SecondFactorNotSatisfied);
        }

        var secret = totp.Create(_options.Issuer, user.UserName, _options.Digits, _options.PeriodSeconds);

        var begun = user.BeginTotpEnrolment(
            ids.NewId(),
            protector.Protect(secret.SecretBase32),
            clock.UtcNow,
            _options.Digits,
            _options.PeriodSeconds);

        if (begun.IsFailure)
        {
            return Result.Failure<TotpEnrolmentStarted>(begun.Error);
        }

        // Replacing an enrolment retires the previous row, and that row now carries a concurrency
        // token, so a second request starting an enrolment at the same moment loses rather than
        // quietly overwriting. Nothing was written; the caller may start again and will find the
        // enrolment the winner created.
        var written = await store.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<TotpEnrolmentStarted>(written.Error);
        }

        IdentityLog.TotpEnrolmentStarted(logger, user.Id);

        await RecordAsync(
            EnrolmentStartedAction,
            user,
            caller,
            "An authenticator enrolment was started.",
            cancellationToken);

        return Result.Success(new TotpEnrolmentStarted(
            secret.OtpAuthUri,
            secret.ManualEntryKey,
            _options.Issuer,
            user.UserName,
            _options.Digits,
            _options.PeriodSeconds));
    }

    /// <summary>
    /// Confirms the enrolment with a code from the holder's authenticator and issues their recovery
    /// codes. The confirming code counts as used, so it cannot be turned round immediately into a
    /// sign-in.
    /// </summary>
    /// <remarks>
    /// A correct code off the holder's own authenticator is a second factor satisfied, so the session is
    /// rotated to record it. That is what lets someone whose sign-in demanded enrolment carry on working
    /// the moment they finish, rather than being stranded on the screen they have just completed, and it
    /// is why the caller must write the returned session to the cookie.
    /// </remarks>
    /// <param name="caller">The session making the request.</param>
    /// <param name="code">The code the holder read off their authenticator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<TotpEnrolmentConfirmed>> ConfirmAsync(
        CallerSession caller,
        string? code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var user = await store.FindUserAsync(caller.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<TotpEnrolmentConfirmed>(IdentityErrors.UserNotFound);
        }

        if (MayNotChangeFactors(user, caller))
        {
            return Result.Failure<TotpEnrolmentConfirmed>(IdentityErrors.SecondFactorNotSatisfied);
        }

        if (user.Totp is not { } enrolment)
        {
            return Result.Failure<TotpEnrolmentConfirmed>(IdentityErrors.MfaEnrolmentNotStarted);
        }

        var secret = protector.Unprotect(enrolment.ProtectedSecret);
        if (secret.IsFailure)
        {
            IdentityLog.TotpSecretUnreadable(logger, user.Id);
            return Result.Failure<TotpEnrolmentConfirmed>(secret.Error);
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
            return Result.Failure<TotpEnrolmentConfirmed>(IdentityErrors.MfaCodeInvalid);
        }

        var confirmed = user.ConfirmTotpEnrolment(verified.Step, clock.UtcNow);
        if (confirmed.IsFailure)
        {
            return Result.Failure<TotpEnrolmentConfirmed>(confirmed.Error);
        }

        var issued = IssueRecoveryCodes(user);
        if (issued.IsFailure)
        {
            return Result.Failure<TotpEnrolmentConfirmed>(issued.Error);
        }

        // Confirmation spends the code that proved the authenticator, so two requests carrying the
        // same code race exactly as two answers to a challenge do — and the loser is refused the same
        // way, rather than confirming twice and printing a second sheet of recovery codes.
        var confirmedWrite = await store.TrySaveChangesAsync(cancellationToken);
        if (confirmedWrite.IsFailure)
        {
            IdentityLog.ChallengeAnswerSuperseded(logger, user.Id, nameof(MfaFactor.Totp));
            return Result.Failure<TotpEnrolmentConfirmed>(IdentityErrors.MfaCodeInvalid);
        }

        IdentityLog.TotpEnrolmentConfirmed(logger, user.Id, issued.Value.RecoveryCodes.Count);

        var rotated = await RotateForSatisfiedFactorAsync(caller, cancellationToken);

        await RecordAsync(
            EnrolmentConfirmedAction,
            user,
            caller,
            $"An authenticator was confirmed and {issued.Value.RecoveryCodes.Count} recovery code(s) "
            + "were issued.",
            cancellationToken);

        return Result.Success(issued.Value with { Session = rotated });
    }

    /// <summary>
    /// Prints a fresh sheet, which destroys the previous one. That is the point rather than a side
    /// effect: a sheet somebody has photographed stops working the moment a new one is printed.
    /// </summary>
    /// <remarks>
    /// It is also why this refuses a session that has not satisfied a second factor. The response body
    /// is a working set of second factors, so a caller holding only the password could otherwise print
    /// themselves one, answer the challenge with it, and destroy the holder's own sheet on the way past.
    /// </remarks>
    /// <param name="caller">The session making the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<TotpEnrolmentConfirmed>> ReissueRecoveryCodesAsync(
        CallerSession caller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.SecondFactorSatisfied)
        {
            return Result.Failure<TotpEnrolmentConfirmed>(IdentityErrors.SecondFactorNotSatisfied);
        }

        var user = await store.FindUserAsync(caller.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<TotpEnrolmentConfirmed>(IdentityErrors.UserNotFound);
        }

        var issued = IssueRecoveryCodes(user);
        if (issued.IsFailure)
        {
            return Result.Failure<TotpEnrolmentConfirmed>(issued.Error);
        }

        // Printing a sheet destroys the previous one. Two requests printing at once would otherwise
        // leave the holder with two sheets and no way to tell which one works, so the loser is told
        // that nothing was printed and can ask again.
        var printed = await store.TrySaveChangesAsync(cancellationToken);
        if (printed.IsFailure)
        {
            return Result.Failure<TotpEnrolmentConfirmed>(printed.Error);
        }

        IdentityLog.RecoveryCodesReissued(logger, user.Id, issued.Value.RecoveryCodes.Count);

        await RecordAsync(
            RecoveryCodesIssuedAction,
            user,
            caller,
            $"A fresh sheet of {issued.Value.RecoveryCodes.Count} recovery code(s) was printed, which "
            + "destroyed the previous sheet.",
            cancellationToken);

        return issued;
    }

    /// <summary>
    /// True when this caller may not add or replace a factor: the account already holds one and the
    /// session making the request has not satisfied it. An account with no confirmed factor is the
    /// narrow exception, because somebody has to be able to enrol the first one.
    /// </summary>
    private static bool MayNotChangeFactors(StaffUser user, CallerSession caller)
        => user.HasConfirmedSecondFactor && !caller.SecondFactorSatisfied;

    private async Task<IssuedSession?> RotateForSatisfiedFactorAsync(
        CallerSession caller,
        CancellationToken cancellationToken)
    {
        if (caller.SessionId is not { } sessionId)
        {
            return null;
        }

        var rotated = await sessions.RotateAsync(
            sessionId, SessionRotationReason.MultiFactorSatisfied, cancellationToken);

        // A rotation that fails leaves the enrolment standing and the old session live. The holder is
        // enrolled either way, which is the part that had to be durable; they simply sign in again to
        // get a session that has satisfied the factor.
        return rotated.IsSuccess ? rotated.Value : null;
    }

    private Result<TotpEnrolmentConfirmed> IssueRecoveryCodes(StaffUser user)
    {
        var sheet = recoveryCodes.Issue(_options.RecoveryCodeCount);

        var stored = user.IssueRecoveryCodes(
            [.. sheet.Select(code => (ids.NewId(), code.CodeHash))],
            clock.UtcNow);

        return stored.IsFailure
            ? Result.Failure<TotpEnrolmentConfirmed>(stored.Error)
            : Result.Success(new TotpEnrolmentConfirmed([.. sheet.Select(code => code.Code)]));
    }

    private Task RecordAsync(
        string action,
        StaffUser user,
        CallerSession caller,
        string summary,
        CancellationToken cancellationToken)
        => AuthenticationAudit.RecordAsync(
            audit,
            action,
            user.Id,
            summary,
            cancellationToken,
            new { caller.SessionId },
            actorId: user.Id,
            actorDisplayName: user.DisplayName);
}
