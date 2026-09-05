using Microsoft.AspNetCore.Authorization;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// How much a session must have proved before an endpoint will act on it.
/// </summary>
/// <remarks>
/// <para>
/// "Authenticated" is not one thing. A session that has answered a password is authenticated in the
/// framework's sense — it names an account and the ticket store accepted it — while having proved
/// strictly less than one that has also answered a second factor, and less again than one that answered
/// a factor a minute ago. Collapsing the three into a single <c>RequireAuthenticatedUser</c> is what
/// lets somebody holding only a stolen password reach the endpoints that mint credentials.
/// </para>
/// <para>
/// The levels are ordered and each includes the ones below it, so an endpoint declares the weakest level
/// that is honestly enough and the handler enforces everything under it.
/// </para>
/// </remarks>
public enum SessionAssurance
{
    /// <summary>
    /// A live session, complete or not. This is the level for the handful of endpoints a half session
    /// must reach to finish signing in or to end itself, and for nothing else.
    /// </summary>
    LiveSession = 0,

    /// <summary>
    /// A session whose holder has finished signing in: no challenge and no enrolment still outstanding.
    /// </summary>
    SignInComplete = 1,

    /// <summary>
    /// A session on which a second factor has actually been satisfied. Required by anything that alters
    /// or discloses credential material on an account that already has a factor to lose.
    /// </summary>
    /// <remarks>
    /// There is deliberately no fourth level demanding that the factor be <em>recent</em>. It is the
    /// right control for anything that mints a credential — an unattended signed-in screen should not
    /// hand somebody a sheet of recovery codes — but a caller refused for staleness needs a way to prove
    /// a factor again without signing out, and the endpoint that offers one arrives with #24 along with
    /// the <c>RequiresStepUp</c> permissions that will use it. A level nobody can satisfy is a level
    /// that gets removed under pressure at the first support call, so it is recorded as an open residual
    /// risk in the threat model rather than half-built here. Step-up freshness for <em>permissions</em>
    /// is already enforced, by <see cref="PermissionAuthorisationHandler"/>.
    /// </remarks>
    SecondFactorSatisfied = 2,
}

/// <summary>Demands a level of assurance from the session making the request.</summary>
/// <param name="Level">The weakest level the endpoint will act on.</param>
public sealed record SessionAssuranceRequirement(SessionAssurance Level) : IAuthorizationRequirement;
