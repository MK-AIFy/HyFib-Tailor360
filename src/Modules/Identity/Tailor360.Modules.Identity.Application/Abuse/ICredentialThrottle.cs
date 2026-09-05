namespace Tailor360.Modules.Identity.Application.Abuse;

/// <summary>
/// Counts credential attempts per account and per client address, and refuses the ones past the limit.
/// </summary>
/// <remarks>
/// <para>
/// This is the second of the three abuse controls and it sits between the other two. The rate-limit
/// policy in the host bounds requests per address whatever they are about; the progressive lockout on
/// the account bounds failures against one account and survives a restart because it is a column. This
/// one bounds the pair — <em>this address against this account</em> — and it is the only one of the
/// three that runs before the password is hashed.
/// </para>
/// <para>
/// That ordering is the point. Verifying an Argon2id hash costs tens of milliseconds of CPU and a
/// megabyte or two of memory by design; without a check in front of it, an unauthenticated caller can
/// spend the server's whole CPU budget on passwords that were never going to be right. The lockout
/// column cannot do that job, because reaching it means the hash has already been computed.
/// </para>
/// <para>
/// The counters are per instance and are not durable, which is a deliberate limit rather than an
/// oversight: they are a cheap first filter, and the controls that must survive a restart or span
/// replicas — the lockout, the audit trail — are in the database. A deployment that runs more than one
/// web replica should treat the effective limits as multiplied by the replica count and rely on the
/// lockout for the per-account bound. See <c>docs/security/threat-models/authentication.md</c>.
/// </para>
/// </remarks>
public interface ICredentialThrottle
{
    /// <summary>
    /// Decides whether an attempt may be evaluated at all. Call this before doing any work the caller
    /// could make expensive, and in particular before verifying a password.
    /// </summary>
    /// <param name="action">Which endpoint is being called.</param>
    /// <param name="accountKey">
    /// A stable, case-folded key for the account being attempted — the sign-in name as typed, or the
    /// account identifier once one is known. Null when the request names no account.
    /// </param>
    /// <param name="clientKey">The client address, or null when it cannot be resolved.</param>
    ThrottleDecision Check(CredentialAction action, string? accountKey, string? clientKey);

    /// <summary>Counts a failed attempt against both keys.</summary>
    void RecordFailure(CredentialAction action, string? accountKey, string? clientKey);

    /// <summary>
    /// Clears the account's counter after a successful attempt, leaving the address counter alone. An
    /// attacker who guesses one password on a shared address must not be able to reset the evidence of
    /// the hundred guesses that preceded it.
    /// </summary>
    void RecordSuccess(CredentialAction action, string? accountKey, string? clientKey);
}
