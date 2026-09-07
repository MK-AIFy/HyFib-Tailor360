namespace Tailor360.Modules.Identity.Domain.Sessions;

/// <summary>
/// What a session still owes before its holder has finished signing in.
/// </summary>
/// <remarks>
/// <para>
/// This is stored on the session rather than derived from the account, because neither the account nor
/// the <c>MfaSatisfied</c> flag can answer the question on its own. A remembered device skips the
/// challenge without satisfying it, so its session is complete while <c>MfaSatisfied</c> stays false;
/// an account that has just enrolled an authenticator has a confirmed factor while the session that
/// enrolled it was complete all along. Deriving the answer from either would lock out the first case
/// and let the second through.
/// </para>
/// <para>
/// The distinction matters because a session that still owes a step is a <em>half</em> session: it
/// proves a password and nothing more. It may finish signing in and it may sign out, and it may reach
/// nothing else — least of all anything that mints or discloses credential material.
/// </para>
/// </remarks>
public enum SessionPendingStep
{
    /// <summary>Nothing is owed: the holder has finished signing in.</summary>
    None = 0,

    /// <summary>A second-factor challenge must be answered.</summary>
    MultiFactorChallenge = 1,

    /// <summary>
    /// The account must enrol a second factor before it may do anything else, because its permissions
    /// require one and it has none.
    /// </summary>
    MultiFactorEnrolment = 2,
}
