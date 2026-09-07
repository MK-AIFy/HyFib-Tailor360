using Tailor360.Modules.Identity.Domain.Credentials;
using Tailor360.Modules.Identity.Domain.Lockout;
using Tailor360.Modules.Identity.Domain.Mfa;
using Tailor360.Modules.Identity.Domain.Passkeys;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Users;

/// <summary>
/// A member of shop staff and everything that proves they are them: their password, their
/// authenticator, their recovery codes, their passkeys and the devices they have asked the system to
/// remember. This is the aggregate root, so every rule that spans two of those lives here rather than
/// in whichever handler happened to need it.
/// </summary>
/// <remarks>
/// There are no customer accounts in this system. Customers reach their own information through
/// expiring, purpose-bound links (assumption A2, pending owner decision OD-12); everything modelled
/// here is staff.
/// <para>
/// The rules worth naming, because they are the ones a handler would otherwise be trusted to remember:
/// a recovery code is spent by the aggregate, never by a caller, so it cannot be spent twice; issuing a
/// new sheet of codes destroys the old sheet, so a printed page that has been photographed stops
/// working the moment a new one is printed; codes may only be issued once a second factor is confirmed,
/// because a recovery code for a factor that does not exist is simply a second password; and the last
/// remaining factor on an account that requires multi-factor cannot be removed, which is the difference
/// between a person losing a phone and a person locking themselves out of a shop's takings.
/// </para>
/// </remarks>
public sealed class StaffUser
{
    /// <summary>The fewest recovery codes issued in one sheet.</summary>
    public const int MinimumRecoveryCodes = 8;

    /// <summary>The most recovery codes issued in one sheet.</summary>
    public const int MaximumRecoveryCodes = 16;

    /// <summary>The most devices one account may remember at a time.</summary>
    public const int MaximumTrustedDevices = 10;

    /// <summary>The longest sign-in name the store accepts.</summary>
    public const int MaximumUserNameLength = 128;

    /// <summary>The longest email address the store accepts, per RFC 5321.</summary>
    public const int MaximumEmailLength = 320;

    /// <summary>The longest display name the store accepts.</summary>
    public const int MaximumDisplayNameLength = 200;

    private readonly List<RecoveryCode> _recoveryCodes = [];
    private readonly List<PasskeyCredential> _passkeys = [];
    private readonly List<TrustedDevice> _trustedDevices = [];

    private StaffUser()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private StaffUser(
        Guid id,
        Guid organisationId,
        Guid? homeBranchId,
        string userName,
        string email,
        string displayName,
        Guid? invitedBy,
        DateTimeOffset now)
    {
        Id = id;
        OrganisationId = organisationId;
        HomeBranchId = homeBranchId;
        UserName = userName;
        Email = email;
        NormalisedEmail = email.ToUpperInvariant();
        DisplayName = displayName;
        Status = UserStatus.Invited;
        MfaEnrolment = MfaEnrolmentState.NotEnrolled;
        MustChangePassword = true;
        CreatedAt = now;
        CreatedBy = invitedBy;
        UpdatedAt = now;
        UpdatedBy = invitedBy;
        Preferences = UserPreferences.CreateDefault(id, now);
    }

    /// <summary>Identity of the account.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the account belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch a session starts in by default. Branch assignment itself arrives with #24.</summary>
    public Guid? HomeBranchId { get; private set; }

    /// <summary>The sign-in name, stored folded to lower case so that it is unique case-insensitively.</summary>
    public string UserName { get; private set; } = string.Empty;

    /// <summary>The email address as the holder wrote it, which is what messages are addressed to.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>The upper-cased address, which is what uniqueness and lookup use.</summary>
    public string NormalisedEmail { get; private set; } = string.Empty;

    /// <summary>The name shown in the interface and in "who did this" references.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Whether the account may be used.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>How far the account has got with its second factor.</summary>
    public MfaEnrolmentState MfaEnrolment { get; private set; }

    /// <summary>True while the holder must set a new password before doing anything else.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>Consecutive failed sign-ins since the last successful one.</summary>
    public int FailedSignInCount { get; private set; }

    /// <summary>When the current lockout lapses, if the account is locked.</summary>
    public DateTimeOffset? LockedOutUntil { get; private set; }

    /// <summary>When the account last signed in successfully.</summary>
    public DateTimeOffset? LastSignInAt { get; private set; }

    /// <summary>When the password was last set.</summary>
    public DateTimeOffset? PasswordChangedAt { get; private set; }

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When the account last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When the account was deactivated.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>The current password, if one has been set.</summary>
    public PasswordCredential? Password { get; private set; }

    /// <summary>The authenticator enrolment, if one has been started.</summary>
    public TotpEnrolment? Totp { get; private set; }

    /// <summary>The holder's interface preferences.</summary>
    public UserPreferences? Preferences { get; private set; }

    /// <summary>The recovery codes issued to the holder, spent and unspent.</summary>
    public IReadOnlyCollection<RecoveryCode> RecoveryCodes => _recoveryCodes;

    /// <summary>The passkeys registered against the account.</summary>
    public IReadOnlyCollection<PasskeyCredential> Passkeys => _passkeys;

    /// <summary>The devices the holder has asked the system to remember.</summary>
    public IReadOnlyCollection<TrustedDevice> TrustedDevices => _trustedDevices;

    /// <summary>How many issued recovery codes remain unspent.</summary>
    public int UnusedRecoveryCodeCount => _recoveryCodes.Count(code => !code.IsConsumed);

    /// <summary>True when the account holds at least one confirmed second factor.</summary>
    public bool HasConfirmedSecondFactor
        => Totp is { IsConfirmed: true } || _passkeys.Count > 0;

    /// <summary>True while a lockout is in force.</summary>
    public bool IsLockedOut(DateTimeOffset now) => LockedOutUntil is { } until && until > now;

    /// <summary>Creates an invited account. The holder sets their own password from the invitation.</summary>
    /// <param name="id">Identity of the account, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="userName">The sign-in name.</param>
    /// <param name="email">The email address the invitation goes to.</param>
    /// <param name="displayName">The name shown in the interface.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="homeBranchId">The branch the holder works in, when they have one.</param>
    /// <param name="invitedBy">The administrator issuing the invitation.</param>
    public static Result<StaffUser> Invite(
        Guid id,
        Guid organisationId,
        string? userName,
        string? email,
        string? displayName,
        DateTimeOffset now,
        Guid? homeBranchId = null,
        Guid? invitedBy = null)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<StaffUser>(IdentityErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<StaffUser>(IdentityErrors.Required("organisationId"));
        }

        var normalisedUserName = userName?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalisedUserName))
        {
            return Result.Failure<StaffUser>(IdentityErrors.Required("userName"));
        }

        if (normalisedUserName.Length > MaximumUserNameLength)
        {
            return Result.Failure<StaffUser>(
                IdentityErrors.TooLong("userName", MaximumUserNameLength));
        }

        if (normalisedUserName.Any(char.IsWhiteSpace))
        {
            return Result.Failure<StaffUser>(IdentityErrors.Required("userName"));
        }

        var trimmedEmail = email?.Trim();
        if (string.IsNullOrEmpty(trimmedEmail))
        {
            return Result.Failure<StaffUser>(IdentityErrors.Required("email"));
        }

        if (trimmedEmail.Length > MaximumEmailLength)
        {
            return Result.Failure<StaffUser>(IdentityErrors.TooLong("email", MaximumEmailLength));
        }

        if (!IsDeliverable(trimmedEmail))
        {
            return Result.Failure<StaffUser>(IdentityErrors.EmailNotUsable);
        }

        var trimmedDisplayName = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmedDisplayName))
        {
            return Result.Failure<StaffUser>(IdentityErrors.Required("displayName"));
        }

        if (trimmedDisplayName.Length > MaximumDisplayNameLength)
        {
            return Result.Failure<StaffUser>(
                IdentityErrors.TooLong("displayName", MaximumDisplayNameLength));
        }

        return new StaffUser(
            id, organisationId, homeBranchId, normalisedUserName, trimmedEmail, trimmedDisplayName,
            invitedBy, now);
    }

    /// <summary>Renames the account holder.</summary>
    public Result Rename(string? displayName, DateTimeOffset now, Guid? by)
    {
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure(IdentityErrors.Required("displayName"));
        }

        if (trimmed.Length > MaximumDisplayNameLength)
        {
            return Result.Failure(IdentityErrors.TooLong("displayName", MaximumDisplayNameLength));
        }

        DisplayName = trimmed;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Moves the account's default branch.</summary>
    public void AssignHomeBranch(Guid? branchId, DateTimeOffset now, Guid? by)
    {
        HomeBranchId = branchId;
        Touch(now, by);
    }

    /// <summary>Stops the account temporarily. Existing sessions are revoked by the caller.</summary>
    public Result Suspend(DateTimeOffset now, Guid? by)
    {
        if (Status is UserStatus.Deactivated)
        {
            return Result.Failure(
                IdentityErrors.StatusTransitionNotAllowed(nameof(UserStatus.Deactivated), nameof(UserStatus.Suspended)));
        }

        Status = UserStatus.Suspended;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Lifts a suspension.</summary>
    public Result Reinstate(DateTimeOffset now, Guid? by)
    {
        if (Status is not UserStatus.Suspended)
        {
            return Result.Failure(
                IdentityErrors.StatusTransitionNotAllowed(Status.ToString(), nameof(UserStatus.Active)));
        }

        Status = UserStatus.Active;
        FailedSignInCount = 0;
        LockedOutUntil = null;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Stops the account for good. Nothing is deleted — the audit trail and every "who did this"
    /// reference must stay resolvable — so this is how an account leaves the shop.
    /// </summary>
    public Result Deactivate(DateTimeOffset now, Guid? by)
    {
        if (Status is UserStatus.Deactivated)
        {
            return Result.Success();
        }

        Status = UserStatus.Deactivated;
        DeactivatedAt = now;
        LockedOutUntil = null;

        // A departing account keeps nothing that could let a device sign in on its behalf.
        foreach (var device in _trustedDevices)
        {
            device.Revoke(now);
        }

        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Brings a deactivated account back. Everything that proved identity starts again: the password
    /// must be reset and the second factor re-enrolled, because the person returning has to prove they
    /// are the person who left.
    /// </summary>
    public Result Reactivate(DateTimeOffset now, Guid by, string? reason)
    {
        if (Status is not UserStatus.Deactivated)
        {
            return Result.Failure(
                IdentityErrors.StatusTransitionNotAllowed(Status.ToString(), nameof(UserStatus.Active)));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(IdentityErrors.ReasonRequired);
        }

        Status = UserStatus.Invited;
        DeactivatedAt = null;
        MustChangePassword = true;
        FailedSignInCount = 0;
        LockedOutUntil = null;
        Password = null;

        ClearSecondFactors(now);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Checks whether the account is in a state that could authenticate at all. The caller collapses
    /// these reasons into one generic answer for an anonymous request; they stay distinct here because
    /// the audit trail and the administration screens need to know which it was.
    /// </summary>
    public Result EnsureCanAuthenticate(DateTimeOffset now) => Status switch
    {
        UserStatus.Deactivated => Result.Failure(IdentityErrors.UserDeactivated),
        UserStatus.Suspended => Result.Failure(IdentityErrors.UserSuspended),
        UserStatus.Invited when Password is null => Result.Failure(IdentityErrors.UserNotActivated),
        _ when IsLockedOut(now) => Result.Failure(IdentityErrors.UserLockedOut),
        _ => Result.Success(),
    };

    /// <summary>Counts a failed sign-in and applies the progressive lockout.</summary>
    public void RecordFailedSignIn(LockoutPolicy policy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);

        FailedSignInCount++;
        LockedOutUntil = policy.ComputeLockoutEnd(FailedSignInCount, now);
        UpdatedAt = now;
    }

    /// <summary>Clears the failure count after a successful sign-in.</summary>
    public void RecordSuccessfulSignIn(DateTimeOffset now)
    {
        FailedSignInCount = 0;
        LockedOutUntil = null;
        LastSignInAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Sets or replaces the password from an already-encoded hash. An invited account becomes active
    /// the moment it has a password. Changing a password never touches the second factor: after a
    /// reset the holder still completes their authenticator or passkey challenge.
    /// </summary>
    /// <param name="credentialId">Identity for a newly created credential, from <c>IIdGenerator</c>.</param>
    /// <param name="encodedHash">The encoded hash produced by the password hasher.</param>
    /// <param name="algorithm">The algorithm token, for example <c>argon2id</c>.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="by">Who is setting it — the holder, or an administrator issuing a reset.</param>
    /// <param name="mustChange">True when the holder must replace it at the next sign-in.</param>
    public Result SetPassword(
        Guid credentialId,
        string? encodedHash,
        string? algorithm,
        DateTimeOffset now,
        Guid? by,
        bool mustChange = false)
    {
        if (Status is UserStatus.Deactivated)
        {
            return Result.Failure(IdentityErrors.UserDeactivated);
        }

        if (Password is null)
        {
            var created = PasswordCredential.Create(credentialId, Id, encodedHash, algorithm, now);
            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            Password = created.Value;
        }
        else
        {
            var replaced = Password.Replace(encodedHash, algorithm, now);
            if (replaced.IsFailure)
            {
                return replaced;
            }
        }

        PasswordChangedAt = now;
        MustChangePassword = mustChange;

        if (Status is UserStatus.Invited)
        {
            Status = UserStatus.Active;
        }

        FailedSignInCount = 0;
        LockedOutUntil = null;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Starts an authenticator enrolment from an already-protected shared secret.</summary>
    public Result BeginTotpEnrolment(
        Guid enrolmentId,
        string? protectedSecret,
        DateTimeOffset now,
        int digits = TotpEnrolment.DefaultDigits,
        int periodSeconds = TotpEnrolment.DefaultPeriodSeconds)
    {
        if (Totp is { IsConfirmed: true })
        {
            return Result.Failure(IdentityErrors.MfaAlreadyEnrolled);
        }

        var begun = TotpEnrolment.Begin(enrolmentId, Id, protectedSecret, now, digits, periodSeconds);
        if (begun.IsFailure)
        {
            return Result.Failure(begun.Error);
        }

        Totp = begun.Value;
        MfaEnrolment = MfaEnrolmentState.PendingConfirmation;
        Touch(now, Id);

        return Result.Success();
    }

    /// <summary>Confirms the authenticator with the step whose code the holder just typed.</summary>
    public Result ConfirmTotpEnrolment(long acceptedStep, DateTimeOffset now)
    {
        if (Totp is null)
        {
            return Result.Failure(IdentityErrors.MfaEnrolmentNotStarted);
        }

        var confirmed = Totp.Confirm(acceptedStep, now);
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        MfaEnrolment = MfaEnrolmentState.Enrolled;
        Touch(now, Id);

        return Result.Success();
    }

    /// <summary>
    /// Clears the second factor after an administrator has verified the holder's identity out of band.
    /// The reason is mandatory because it is what the audit entry carries; the caller revokes the
    /// holder's sessions and sends the security alert.
    /// </summary>
    public Result ResetMfa(DateTimeOffset now, Guid by, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(IdentityErrors.ReasonRequired);
        }

        ClearSecondFactors(now);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Issues a fresh sheet of recovery codes, destroying any previous sheet. Codes arrive already
    /// hashed; the plaintext is shown to the holder once by the caller and never stored.
    /// </summary>
    /// <param name="codes">Identity and digest of each code.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    public Result IssueRecoveryCodes(
        IReadOnlyCollection<(Guid Id, string CodeHash)> codes,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(codes);

        if (!HasConfirmedSecondFactor)
        {
            return Result.Failure(IdentityErrors.MfaNotEnrolled);
        }

        if (codes.Count is < MinimumRecoveryCodes or > MaximumRecoveryCodes)
        {
            return Result.Failure(
                IdentityErrors.RecoveryCodeCountOutOfRange(MinimumRecoveryCodes, MaximumRecoveryCodes));
        }

        if (codes.Select(code => code.CodeHash).Distinct(StringComparer.Ordinal).Count() != codes.Count)
        {
            return Result.Failure(IdentityErrors.RecoveryCodesNotDistinct);
        }

        var created = new List<RecoveryCode>(codes.Count);
        foreach (var (id, codeHash) in codes)
        {
            var code = RecoveryCode.Create(id, Id, codeHash, now);
            if (code.IsFailure)
            {
                return Result.Failure(code.Error);
            }

            created.Add(code.Value);
        }

        // The previous sheet stops working as a whole. Leaving old unspent codes alive would mean a
        // photographed sheet stayed valid after the holder had deliberately reprinted it.
        _recoveryCodes.Clear();
        _recoveryCodes.AddRange(created);
        Touch(now, Id);

        return Result.Success();
    }

    /// <summary>
    /// Spends a recovery code. An unknown code and an already-spent one fail identically, so a caller
    /// learns nothing from the difference.
    /// </summary>
    public Result RedeemRecoveryCode(string? codeHash, DateTimeOffset now)
    {
        if (!HashedSecret.IsWellFormed(codeHash))
        {
            return Result.Failure(IdentityErrors.RecoveryCodeInvalid);
        }

        var match = _recoveryCodes.FirstOrDefault(code => !code.IsConsumed && code.Matches(codeHash));
        if (match is null)
        {
            return Result.Failure(IdentityErrors.RecoveryCodeInvalid);
        }

        var consumed = match.Consume(now);
        if (consumed.IsFailure)
        {
            return consumed;
        }

        Touch(now, Id);

        return Result.Success();
    }

    /// <summary>Registers a passkey produced by a completed WebAuthn registration ceremony.</summary>
    public Result RegisterPasskey(PasskeyCredential credential, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (_passkeys.Any(existing => existing.HasCredentialId(credential.CredentialId)))
        {
            return Result.Failure(IdentityErrors.PasskeyAlreadyRegistered);
        }

        _passkeys.Add(credential);
        MfaEnrolment = MfaEnrolmentState.Enrolled;
        Touch(now, Id);

        return Result.Success();
    }

    /// <summary>Finds a registered passkey by the credential identifier the browser presented.</summary>
    public PasskeyCredential? FindPasskey(byte[]? credentialId)
        => credentialId is null
            ? null
            : _passkeys.FirstOrDefault(passkey => passkey.HasCredentialId(credentialId));

    /// <summary>
    /// Removes a passkey, refusing to leave the account with no second factor at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is "not to zero", not "not to zero when a policy says so". An earlier form asked the
    /// caller whether multi-factor was required for this holder and only refused then; with roles and
    /// effective permissions not yet modelled, every caller answered "no", so the guard never fired and
    /// an account could be stripped of its protection one passkey at a time. A rule that depends on a
    /// value nobody can supply yet is not a rule.
    /// </para>
    /// <para>
    /// Refusing outright is also the better rule on its own terms. Someone who has enrolled a factor has
    /// chosen to have one, and removing the last one from a self-service screen is far more often a
    /// mistake — or somebody else's deliberate act — than an intention. The way back to zero is an
    /// administrator's reset, which takes a mandatory reason, revokes the holder's sessions and leaves
    /// an audit entry naming who did it.
    /// </para>
    /// </remarks>
    /// <param name="passkeyId">The passkey to remove.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    public Result RemovePasskey(Guid passkeyId, DateTimeOffset now)
    {
        var passkey = _passkeys.FirstOrDefault(candidate => candidate.Id == passkeyId);
        if (passkey is null)
        {
            return Result.Failure(IdentityErrors.PasskeyNotFound);
        }

        var remainingFactors = _passkeys.Count - 1 + (Totp is { IsConfirmed: true } ? 1 : 0);
        if (remainingFactors == 0)
        {
            return Result.Failure(IdentityErrors.LastFactorCannotBeRemoved);
        }

        _passkeys.Remove(passkey);
        Touch(now, Id);

        return Result.Success();
    }

    /// <summary>
    /// Remembers a device so that later sign-ins from it need only the password. Only ever called after
    /// a second factor has been satisfied on this sign-in.
    /// </summary>
    public Result<TrustedDevice> RememberDevice(
        Guid deviceId,
        string? tokenHash,
        string? label,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        if (!HasConfirmedSecondFactor)
        {
            return Result.Failure<TrustedDevice>(IdentityErrors.MfaNotEnrolled);
        }

        var usable = _trustedDevices.Count(device => device.IsUsable(now));
        if (usable >= MaximumTrustedDevices)
        {
            return Result.Failure<TrustedDevice>(
                IdentityErrors.TooManyTrustedDevices(MaximumTrustedDevices));
        }

        var remembered = TrustedDevice.Remember(deviceId, Id, tokenHash, label, now, lifetime);
        if (remembered.IsFailure)
        {
            return remembered;
        }

        _trustedDevices.Add(remembered.Value);
        Touch(now, Id);

        return remembered;
    }

    /// <summary>Finds a remembered device by the digest of the cookie value presented.</summary>
    public TrustedDevice? FindUsableDevice(string? tokenHash, DateTimeOffset now)
        => !HashedSecret.IsWellFormed(tokenHash)
            ? null
            : _trustedDevices.FirstOrDefault(device => device.IsUsable(now) && device.Matches(tokenHash));

    /// <summary>Forgets a remembered device.</summary>
    public Result ForgetDevice(Guid deviceId, DateTimeOffset now)
    {
        var device = _trustedDevices.FirstOrDefault(candidate => candidate.Id == deviceId);
        if (device is null)
        {
            return Result.Failure(IdentityErrors.TrustedDeviceNotFound);
        }

        device.Revoke(now);
        Touch(now, Id);

        return Result.Success();
    }

    /// <summary>The account's own text, for the password policy to screen a candidate against.</summary>
    public PasswordContext PasswordContext() => new(UserName, Email, DisplayName);

    private void ClearSecondFactors(DateTimeOffset now)
    {
        Totp = null;
        _recoveryCodes.Clear();
        _passkeys.Clear();
        MfaEnrolment = MfaEnrolmentState.ResetRequired;

        foreach (var device in _trustedDevices)
        {
            device.Revoke(now);
        }
    }

    /// <summary>
    /// Records that an administrator changed something about this account that is stored beside it
    /// rather than on it.
    /// </summary>
    /// <remarks>
    /// Role and branch assignments are rows of their own, and neither carries a concurrency token — so
    /// two administrators editing the same person's access at the same moment would both succeed and
    /// the second would silently discard the first. Touching the account makes the two edits contend
    /// for the one row that does carry a token, which is what turns "last writer wins" into a refusal
    /// the loser can see and act on.
    /// </remarks>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator making the change.</param>
    public void RecordAdministrativeChange(DateTimeOffset now, Guid by) => Touch(now, by);

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>
    /// A deliberately shallow check: exactly one <c>@</c> with something either side and no whitespace.
    /// Deciding whether an address exists is the invitation email's job, not a regular expression's.
    /// </summary>
    private static bool IsDeliverable(string email)
    {
        var parts = email.Split('@');
        return parts.Length == 2
               && parts[0].Length > 0
               && parts[1].Length > 2
               && parts[1].Contains('.', StringComparison.Ordinal)
               && !email.Any(char.IsWhiteSpace);
    }
}
