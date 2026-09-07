using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain;

/// <summary>
/// Every failure the Identity domain can report, in one place, so that the wire codes are reviewable
/// as a set rather than scattered across call sites. Codes follow <c>identity.&lt;kebab-case-reason&gt;</c>
/// (conventions section 4.3) and are stable: a client may branch on them.
/// </summary>
/// <remarks>
/// Two conventions matter more here than anywhere else in the system. First, a message never contains
/// a secret, a token, a recovery code or a password — these strings reach logs and problem details.
/// Second, the domain reports the <em>precise</em> reason while the authentication endpoints collapse
/// several of them into one generic answer, because telling an anonymous caller whether an account
/// exists, is locked or merely typed the wrong password is an enumeration oracle. Precision here,
/// discretion at the edge.
/// </remarks>
public static class IdentityErrors
{
    /// <summary>The supplied credentials did not authenticate. Deliberately says nothing more.</summary>
    public static Error InvalidCredentials { get; } = Error.Forbidden(
        "identity.invalid-credentials",
        "The sign-in details supplied are not valid.");

    /// <summary>A required value was missing or malformed.</summary>
    public static Error Required(string field) => Error.Validation(
        "identity.value-required",
        "A required value was not supplied.",
        field);

    /// <summary>A value exceeded the length the store accepts.</summary>
    public static Error TooLong(string field, int maximum) => Error.Validation(
        "identity.value-too-long",
        $"The value is longer than the {maximum} characters this field accepts.",
        field);

    /// <summary>The supplied address is not a usable email address.</summary>
    public static Error EmailNotUsable { get; } = Error.Validation(
        "identity.email-not-usable",
        "The email address is not in a form this system can deliver to.",
        "email");

    /// <summary>The user account is suspended.</summary>
    public static Error UserSuspended { get; } = Error.Forbidden(
        "identity.user-suspended",
        "This account is suspended.");

    /// <summary>The user account is deactivated and can no longer authenticate.</summary>
    public static Error UserDeactivated { get; } = Error.Forbidden(
        "identity.user-deactivated",
        "This account is deactivated.");

    /// <summary>The user has been invited but has not yet set a password.</summary>
    public static Error UserNotActivated { get; } = Error.Forbidden(
        "identity.user-not-activated",
        "This account has not completed its invitation.");

    /// <summary>Too many failed attempts; the account is locked until the lockout expires.</summary>
    public static Error UserLockedOut { get; } = Error.Forbidden(
        "identity.user-locked-out",
        "This account is temporarily locked after repeated failed sign-in attempts.");

    /// <summary>The requested status change is not legal from the account's current status.</summary>
    public static Error StatusTransitionNotAllowed(string from, string to) => Error.Conflict(
        "identity.status-transition-not-allowed",
        $"An account cannot move from {from} to {to}.");

    /// <summary>A stored credential hash was not in the encoded form the store requires.</summary>
    public static Error CredentialNotEncoded { get; } = Error.Validation(
        "identity.credential-not-encoded",
        "A credential must be stored in its encoded, hashed form.",
        "passwordHash");

    /// <summary>No password has been set on this account.</summary>
    public static Error PasswordNotSet { get; } = Error.Conflict(
        "identity.password-not-set",
        "This account has no password set.");

    /// <summary>The candidate password is shorter than the policy allows.</summary>
    public static Error PasswordTooShort(int minimum) => Error.Validation(
        "identity.password-too-short",
        $"A password must be at least {minimum} characters long.",
        "password");

    /// <summary>The candidate password is longer than the policy accepts.</summary>
    public static Error PasswordTooLong(int maximum) => Error.Validation(
        "identity.password-too-long",
        $"A password may be at most {maximum} characters long.",
        "password");

    /// <summary>The candidate password is made of too few distinct characters.</summary>
    public static Error PasswordTooRepetitive(int minimumDistinct) => Error.Validation(
        "identity.password-too-repetitive",
        $"A password must use at least {minimumDistinct} different characters.",
        "password");

    /// <summary>The candidate password repeats the account's own name or address.</summary>
    public static Error PasswordContainsAccountDetail { get; } = Error.Validation(
        "identity.password-contains-account-detail",
        "A password must not contain your name, sign-in name or email address.",
        "password");

    /// <summary>The candidate password appears in the configured breached-password list.</summary>
    public static Error PasswordBreached { get; } = Error.Validation(
        "identity.password-breached",
        "This password appears in a public list of exposed passwords. Choose a different one.",
        "password");

    /// <summary>No account matches the identifier supplied.</summary>
    public static Error UserNotFound { get; } = Error.NotFound(
        "identity.user-not-found",
        "No account matches that identifier.");

    /// <summary>
    /// The submitted authenticator code did not match. Says nothing about which digit was wrong, how
    /// far out the clock is, or how many attempts remain.
    /// </summary>
    public static Error MfaCodeInvalid { get; } = Error.Forbidden(
        "identity.mfa-code-invalid",
        "That code is not valid. Check your authenticator and try the next one.");

    /// <summary>
    /// The stored authenticator secret could not be read with the current data-protection keys. It is
    /// an operator failure, not the holder's: the key ring was not persisted, or was replaced.
    /// </summary>
    public static Error MfaSecretUnreadable { get; } = Error.Unavailable(
        "identity.mfa-secret-unreadable",
        "This account's authenticator cannot be read on this server. An administrator must reset it.");

    /// <summary>Multi-factor enrolment cannot start because a confirmed factor already exists.</summary>
    public static Error MfaAlreadyEnrolled { get; } = Error.Conflict(
        "identity.mfa-already-enrolled",
        "This account already has a confirmed authenticator. Reset it before enrolling again.");

    /// <summary>An enrolment step was attempted before the enrolment was started.</summary>
    public static Error MfaEnrolmentNotStarted { get; } = Error.Conflict(
        "identity.mfa-enrolment-not-started",
        "No authenticator enrolment is in progress for this account.");

    /// <summary>The authenticator is enrolled but not yet confirmed with a valid code.</summary>
    public static Error MfaNotConfirmed { get; } = Error.Conflict(
        "identity.mfa-not-confirmed",
        "The authenticator has not been confirmed with a valid code.");

    /// <summary>The account holds no confirmed second factor.</summary>
    public static Error MfaNotEnrolled { get; } = Error.Conflict(
        "identity.mfa-not-enrolled",
        "This account has no confirmed second factor.");

    /// <summary>A time-based code from a step that has already been accepted was presented again.</summary>
    public static Error TotpCodeReplayed { get; } = Error.Conflict(
        "identity.totp-code-replayed",
        "That code has already been used. Wait for the next code.");

    /// <summary>
    /// The caller's session has not satisfied a second factor, and the action would alter or disclose
    /// credential material on an account that already holds one.
    /// </summary>
    /// <remarks>
    /// This is the refusal that keeps a stolen password from becoming a stolen account. Somebody who
    /// knows only the password holds a session that has answered the first factor and nothing else; on
    /// an account with a confirmed factor, that session may finish signing in and may sign out, and it
    /// may not print a fresh sheet of recovery codes, enrol a second authenticator or register a passkey
    /// — each of which would hand the attacker the very factor they do not have.
    /// </remarks>
    public static Error SecondFactorNotSatisfied { get; } = Error.Forbidden(
        "identity.second-factor-not-satisfied",
        "Answer your second factor before changing this account's security settings.");

    /// <summary>Removing this factor would leave an account that requires multi-factor without one.</summary>
    public static Error LastFactorCannotBeRemoved { get; } = Error.Conflict(
        "identity.last-factor-cannot-be-removed",
        "This is the only second factor on this account. Enrol another factor first, or ask an "
        + "administrator to reset the second factor.");

    /// <summary>
    /// The recovery code did not match an unused code. The same error is returned for an unknown code
    /// and for one already spent, so an attacker learns nothing from the difference.
    /// </summary>
    public static Error RecoveryCodeInvalid { get; } = Error.Forbidden(
        "identity.recovery-code-invalid",
        "That recovery code is not valid.");

    /// <summary>A recovery code was supplied in a form other than its stored hash.</summary>
    public static Error RecoveryCodeNotHashed { get; } = Error.Validation(
        "identity.recovery-code-not-hashed",
        "A recovery code is stored only as its hash.",
        "recoveryCode");

    /// <summary>The number of recovery codes issued is outside the permitted range.</summary>
    public static Error RecoveryCodeCountOutOfRange(int minimum, int maximum) => Error.Validation(
        "identity.recovery-code-count-out-of-range",
        $"Between {minimum} and {maximum} recovery codes are issued at a time.",
        "recoveryCodes");

    /// <summary>Two recovery codes in one issue hashed to the same value.</summary>
    public static Error RecoveryCodesNotDistinct { get; } = Error.Validation(
        "identity.recovery-codes-not-distinct",
        "Recovery codes issued together must all differ.",
        "recoveryCodes");

    /// <summary>
    /// The recovery link is not usable. An unknown token, one already spent, one withdrawn by a later
    /// request, one that has expired and one presented for the wrong purpose all report this, because
    /// telling the holder of a link which of those it was would tell an attacker holding a stolen link
    /// the same thing.
    /// </summary>
    public static Error RecoveryTokenInvalid { get; } = Error.Forbidden(
        "identity.recovery-token-invalid",
        "That recovery link is no longer usable. Request a new one.");

    /// <summary>The configured recovery-token lifetime is outside the permitted range.</summary>
    public static Error RecoveryTokenLifetimeInvalid(TimeSpan minimum, TimeSpan maximum) => Error.Validation(
        "identity.recovery-token-lifetime-invalid",
        $"A recovery link must live for between {minimum.TotalMinutes:0} and {maximum.TotalMinutes:0} minutes.",
        "tokenLifetime");

    /// <summary>A session token was supplied in a form other than its stored hash.</summary>
    public static Error TokenNotHashed(string field) => Error.Validation(
        "identity.token-not-hashed",
        "A session or device token is stored only as its hash.",
        field);

    /// <summary>
    /// Another request changed the same row between this one reading it and writing it back. The
    /// caller decides what that means: for an answered challenge it is a second answer arriving with
    /// the first, and the loser is refused exactly as a wrong answer would be.
    /// </summary>
    public static Error ConcurrentChange { get; } = Error.Conflict(
        "identity.concurrent-change",
        "Someone else changed this at the same moment. Read it again and retry.");

    /// <summary>
    /// An administrator aimed an administrative action at their own account. Suspending or deactivating
    /// yourself ends your own sessions in the same request and leaves nobody able to undo it from that
    /// account, so the door locks from the outside only.
    /// </summary>
    public static Error CannotAdministerOwnAccount { get; } = Error.Conflict(
        "identity.cannot-administer-own-account",
        "You cannot apply this to your own account. Ask another administrator.");

    /// <summary>A role was named that this organisation does not have.</summary>
    public static Error RoleNotFound(string? key) => Error.Validation(
        "identity.role-not-found",
        $"There is no role called '{key}' in this organisation.",
        "roleKeys");

    /// <summary>A branch was named that is not open, or does not belong to this organisation.</summary>
    public static Error BranchNotAssignable(Guid branchId) => Error.Validation(
        "identity.branch-not-assignable",
        $"Branch {branchId} is closed or belongs to another organisation, so nobody can be assigned to it.",
        "branches");

    /// <summary>More than one branch was marked as the account's usual place of work.</summary>
    public static Error OnePrimaryBranchOnly { get; } = Error.Validation(
        "identity.one-primary-branch-only",
        "Choose exactly one branch as the usual place of work.",
        "branches");

    /// <summary>
    /// The account's default branch was left out of its assignments, which would send every screen to
    /// a branch its holder cannot act in.
    /// </summary>
    public static Error HomeBranchNotAssigned { get; } = Error.Validation(
        "identity.home-branch-not-assigned",
        "The account's default branch has to be one of the branches it is assigned to.",
        "branches");

    /// <summary>
    /// The change would leave the organisation with nobody who can administer accounts.
    /// </summary>
    /// <remarks>
    /// The only way back from that state is a database edit, which is exactly what this whole surface
    /// exists to make unnecessary.
    /// </remarks>
    public static Error LastAdministrator { get; } = Error.Conflict(
        "identity.last-administrator",
        "This would leave nobody able to administer accounts. Give somebody else an administrative "
        + "role first, then take this one away.");

    /// <summary>The session is revoked, idle-expired or past its absolute expiry.</summary>
    public static Error SessionNotActive { get; } = Error.Forbidden(
        "identity.session-not-active",
        "This session is no longer active. Sign in again.");

    /// <summary>The configured session lifetimes are not usable.</summary>
    public static Error SessionLifetimeInvalid(string reason) => Error.Validation(
        "identity.session-lifetime-invalid",
        reason,
        "sessionLifetime");

    /// <summary>A trusted device may not outlive the configured maximum.</summary>
    public static Error TrustedDeviceLifetimeTooLong(int maximumDays) => Error.Validation(
        "identity.trusted-device-lifetime-too-long",
        $"A trusted device may be remembered for at most {maximumDays} days.",
        "expiresAt");

    /// <summary>The account already remembers as many devices as it may.</summary>
    public static Error TooManyTrustedDevices(int maximum) => Error.Conflict(
        "identity.too-many-trusted-devices",
        $"At most {maximum} devices may be remembered. Revoke one before adding another.");

    /// <summary>The named trusted device is not on this account.</summary>
    public static Error TrustedDeviceNotFound { get; } = Error.NotFound(
        "identity.trusted-device-not-found",
        "That device is not on this account.");

    /// <summary>The named passkey is not on this account.</summary>
    public static Error PasskeyNotFound { get; } = Error.NotFound(
        "identity.passkey-not-found",
        "That passkey is not on this account.");

    /// <summary>A passkey with the same credential identifier is already registered here.</summary>
    public static Error PasskeyAlreadyRegistered { get; } = Error.Conflict(
        "identity.passkey-already-registered",
        "That passkey is already registered on this account.");

    /// <summary>
    /// The authenticator's signature counter did not advance. WebAuthn defines a counter that only ever
    /// increases; a repeated or lower value is the documented signal of a cloned authenticator.
    /// </summary>
    public static Error PasskeyCounterWentBackwards { get; } = Error.Conflict(
        "identity.passkey-counter-went-backwards",
        "This passkey's use counter did not advance, which can mean the credential has been copied.");

    /// <summary>An administrative action that requires a recorded reason was attempted without one.</summary>
    public static Error ReasonRequired { get; } = Error.Validation(
        "identity.reason-required",
        "This action is recorded in the audit trail and needs a stated reason.",
        "reason");

    /// <summary>The supplied locale is not one this deployment serves.</summary>
    public static Error LocaleNotSupported { get; } = Error.Validation(
        "identity.locale-not-supported",
        "That language is not available in this deployment.",
        "locale");

    /// <summary>A role key was not lower-case letters, digits and underscores starting with a letter.</summary>
    public static Error RoleKeyNotWellFormed { get; } = Error.Validation(
        "identity.role-key-not-well-formed",
        "A role key is lower-case letters, digits and underscores, and starts with a letter.",
        "key");

    /// <summary>A branch code was not upper-case letters and digits.</summary>
    public static Error BranchCodeNotWellFormed { get; } = Error.Validation(
        "identity.branch-code-not-well-formed",
        "A branch code is upper-case letters and digits.",
        "code");

    /// <summary>A role names a permission the catalogue does not declare.</summary>
    public static Error PermissionNotInCatalogue(string permissionKey) => Error.Validation(
        "identity.permission-not-in-catalogue",
        $"'{permissionKey}' is not a permission this application declares.",
        "permissionKey");

    /// <summary>The supplied timezone identifier is not an IANA identifier.</summary>
    public static Error TimeZoneNotRecognised { get; } = Error.Validation(
        "identity.time-zone-not-recognised",
        "That timezone identifier is not recognised.",
        "timeZoneId");
}
