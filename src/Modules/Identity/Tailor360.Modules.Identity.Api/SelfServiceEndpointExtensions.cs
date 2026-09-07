using Microsoft.AspNetCore.Builder;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Identity.Api;

/// <summary>
/// Declares an endpoint that acts on the caller's own account, and says how much that caller must have
/// proved first.
/// </summary>
/// <remarks>
/// <para>
/// These are the third way an endpoint may declare itself, alongside <c>RequirePermission</c> and
/// <c>AllowAnonymousWithJustification</c>, and they are deliberately narrow: they are for the handful of
/// endpoints where a permission would be the wrong control rather than a missing one. Everybody who can
/// sign in must be able to sign out, see the devices their account is signed in on, revoke one, and
/// enrol a second factor. A permission gating those would either be granted to every role — in which
/// case it decides nothing — or would be capable of locking someone out of their own account's security
/// settings.
/// </para>
/// <para>
/// <b>There are three of them, not one, and that is the point.</b> "Holds a session" is not a single
/// state: a caller who has answered a password holds a session while having proved strictly less than
/// one who has also answered a second factor. Gating every self-service endpoint on "is there a live
/// session" would let somebody holding a stolen password print a fresh sheet of recovery codes, enrol
/// their own authenticator or register their own passkey, and then answer the challenge with what they
/// had just been handed. Each endpoint therefore names the weakest level that is honestly enough:
/// </para>
/// <list type="table">
/// <item>
/// <term><see cref="AllowPendingSignIn"/></term>
/// <description>A live session, complete or not — only for finishing a sign-in or ending it.</description>
/// </item>
/// <item>
/// <term><see cref="RequireSignedInHolder"/></term>
/// <description>A session whose sign-in is finished.</description>
/// </item>
/// <item>
/// <term><see cref="RequireSatisfiedSecondFactor"/></term>
/// <description>A session that has actually satisfied a second factor.</description>
/// </item>
/// </list>
/// <para>
/// What none of them does is authorise anything <em>about another account</em>: every handler behind
/// them takes the account from the session and never from the request, and the one endpoint that names a
/// resource — revoking a session — checks that it belongs to the caller before acting.
/// </para>
/// </remarks>
public static class SelfServiceEndpointExtensions
{
    /// <summary>
    /// Requires a live session and nothing more, for an endpoint a half-signed-in caller must reach.
    /// </summary>
    /// <remarks>
    /// The set this may be used on is closed and small: answering the second-factor challenge, enrolling
    /// the account's first factor when the sign-in demands one, reading the caller's own profile so the
    /// client can paint the challenge screen, and signing out. Everything else takes a stronger level.
    /// </remarks>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="justification">Why a half-signed-in caller must reach this endpoint.</param>
    /// <param name="reviewedIn">The issue or threat model in which the exposure was reviewed.</param>
    public static TBuilder AllowPendingSignIn<TBuilder>(
        this TBuilder builder,
        string justification,
        string reviewedIn)
        where TBuilder : IEndpointConventionBuilder
        => Declare(builder, justification, reviewedIn, SessionAssurance.LiveSession);

    /// <summary>Requires a session whose holder has finished signing in.</summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="justification">Why this acts on the caller's own account alone.</param>
    /// <param name="reviewedIn">The issue or threat model in which the exposure was reviewed.</param>
    public static TBuilder RequireSignedInHolder<TBuilder>(
        this TBuilder builder,
        string justification,
        string reviewedIn)
        where TBuilder : IEndpointConventionBuilder
        => Declare(builder, justification, reviewedIn, SessionAssurance.SignInComplete);

    /// <summary>
    /// Requires a session on which a second factor has actually been satisfied.
    /// </summary>
    /// <remarks>
    /// This is the level for anything that alters or discloses credential material on an account that
    /// already holds a factor. Demanding that the factor also be <em>recent</em> would be better still
    /// for the endpoints that mint a credential, and is recorded as an open residual risk rather than
    /// declared here: the endpoint that lets somebody prove a factor again without signing out arrives
    /// with #24, and a level nobody can satisfy is a level that gets removed at the first support call.
    /// </remarks>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="justification">Why this acts on the caller's own account alone.</param>
    /// <param name="reviewedIn">The issue or threat model in which the exposure was reviewed.</param>
    public static TBuilder RequireSatisfiedSecondFactor<TBuilder>(
        this TBuilder builder,
        string justification,
        string reviewedIn)
        where TBuilder : IEndpointConventionBuilder
        => Declare(builder, justification, reviewedIn, SessionAssurance.SecondFactorSatisfied);

    private static TBuilder Declare<TBuilder>(
        TBuilder builder,
        string justification,
        string reviewedIn,
        SessionAssurance level)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedIn);

        // RequireAuthenticatedUser as well as the assurance requirement, so a request with no session at
        // all is answered 401 by the framework rather than 403 by the handler: the instruction to the
        // client is "authenticate", not "stop".
        builder.RequireAuthorization(policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new SessionAssuranceRequirement(level));
        });

        builder.WithMetadata(new SelfServiceMetadata(justification, reviewedIn, level));

        return builder;
    }
}

/// <summary>
/// Records that an endpoint is gated on the state of the caller's own session rather than on a
/// permission, why, and how much that session must have proved — so the set can be reviewed the way the
/// anonymous set is.
/// </summary>
/// <param name="Justification">Why a permission would be the wrong control here.</param>
/// <param name="ReviewedIn">Where the decision was reviewed.</param>
/// <param name="Assurance">The weakest session the endpoint will act on.</param>
public sealed record SelfServiceMetadata(
    string Justification,
    string ReviewedIn,
    SessionAssurance Assurance);
