using Microsoft.AspNetCore.Authorization;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// The classes of refusal the authorisation handlers distinguish, so that the answer written to the
/// caller can be chosen from a typed value rather than by matching on a message.
/// </summary>
/// <remarks>
/// The distinction matters to the client, not only to a log reader. "Answer your second factor" and
/// "re-authenticate, you did that eleven minutes ago" are both recoverable without anybody's help and
/// the client offers a dialog for each; "this account may not do that" is not recoverable at all and
/// offering the same dialog for it would be a lie. A single status code cannot carry that, which is why
/// each class has a stable code in the problem document.
/// </remarks>
public enum AuthorisationRefusal
{
    /// <summary>The endpoint names a permission the caller does not hold, or that no module declares.</summary>
    PermissionNotHeld = 0,

    /// <summary>The caller answered the first factor and has not finished signing in.</summary>
    SignInIncomplete = 1,

    /// <summary>The permission demands a multi-factor session and this session is not one.</summary>
    SecondFactorRequired = 2,

    /// <summary>The permission demands a fresh re-authentication and the last one is too old.</summary>
    StepUpRequired = 3,

    /// <summary>The caller is acting outside the branches they are assigned to.</summary>
    OutsideBranchScope = 4,

    /// <summary>The resource does not exist, or belongs to a branch the caller cannot reach.</summary>
    ResourceUnreachable = 5,

    /// <summary>The resource exists and is reachable, and is assigned to somebody else.</summary>
    NotAssigned = 6,

    /// <summary>
    /// The pipeline could not decide, because it was not assembled to. A missing resolution step is a
    /// defect in the host, and this is the value that refuses the request while saying so in the log.
    /// </summary>
    PipelineIncomplete = 7,

    /// <summary>
    /// The request carried no usable session.
    /// </summary>
    /// <remarks>
    /// Ordinarily the authentication middleware answers this before a requirement handler is reached, so
    /// a handler producing it means the endpoint's policy was assembled without
    /// <c>RequireAuthenticatedUser</c>. It exists as its own value rather than being folded into
    /// <see cref="PermissionNotHeld"/> because "nobody was signed in" and "this person may not do that"
    /// are different facts, and the denial trail is read by somebody trying to tell them apart.
    /// </remarks>
    NotAuthenticated = 8,
}

/// <summary>
/// An authorisation failure that names its class. The framework's own reason carries a message only,
/// which would leave the code that answers the caller matching on English text.
/// </summary>
/// <param name="handler">The handler that refused.</param>
/// <param name="refusal">Which class of refusal this is.</param>
/// <param name="message">A short sentence for the log. Never contains anything about the caller or the resource.</param>
public sealed class RefusalReason(IAuthorizationHandler handler, AuthorisationRefusal refusal, string message)
    : AuthorizationFailureReason(handler, message)
{
    /// <summary>Which class of refusal this is.</summary>
    public AuthorisationRefusal Refusal { get; } = refusal;
}
