using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Tailor360.Platform.Security.Antiforgery;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Answers a refused request with an RFC 9457 problem document instead of an empty status line.
/// </summary>
/// <remarks>
/// <para>
/// The framework's default writes a bare 401 or 403 with no body. Every other refusal in this system
/// carries a stable <c>code</c>, a correlation identifier and a sentence written for the person reading
/// the screen, and a client that has to guess from a status code alone cannot tell "sign in again" from
/// "you have not answered your second factor" from "you do not have permission" — three refusals with
/// three different things to do about them.
/// </para>
/// <para>
/// What it does not do is explain how to pass. The reason names the class of failure and nothing about
/// the caller, the endpoint's requirements or which of them was missed beyond that class: a refusal is
/// exactly the moment not to describe the lock. The one place the class is deliberately coarse is the
/// resource: a record in another branch and a record that does not exist answer identically, so that
/// editing an identifier in the address bar tells the person doing it nothing at all.
/// </para>
/// </remarks>
/// <param name="logger">Records the one refusal that means the host is misconfigured.</param>
public sealed class AuthorisationProblemResultHandler(ILogger<AuthorisationProblemResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    /// <summary>The code returned when a request carried no usable session.</summary>
    public const string AuthenticationRequiredCode = "security.authentication-required";

    /// <summary>The code returned when the caller has not finished signing in.</summary>
    public const string SignInIncompleteCode = "security.sign-in-incomplete";

    /// <summary>The code returned when the caller's session has not satisfied a second factor.</summary>
    public const string SecondFactorRequiredCode = "security.second-factor-required";

    /// <summary>
    /// The code returned when the action needs a re-authentication fresher than this session's.
    /// It is distinct from <see cref="SecondFactorRequiredCode"/> because the client's answer differs:
    /// this one is recoverable in place, by asking for the factor again and retrying the pending request
    /// with the same idempotency key.
    /// </summary>
    public const string StepUpRequiredCode = "security.step-up-required";

    /// <summary>The code returned when the resource is not the caller's to see, or is not there.</summary>
    public const string ResourceNotFoundCode = "security.resource-not-found";

    /// <summary>The code returned when the resource is somebody else's work.</summary>
    public const string NotAssignedCode = "security.not-assigned";

    /// <summary>The code returned when the caller is signed in and simply may not do this.</summary>
    public const string ForbiddenCode = "security.forbidden";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <inheritdoc />
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Challenged)
        {
            await SecurityProblemDetails.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                AuthenticationRequiredCode,
                "Sign in to continue",
                "This request needs a signed-in session. Sign in and try again.");

            return;
        }

        if (authorizeResult.Forbidden)
        {
            var refusal = Classify(authorizeResult.AuthorizationFailure);
            if (refusal == AuthorisationRefusal.PipelineIncomplete)
            {
                // Every caller is being refused, and not because of anything they did. This is the one
                // authorisation outcome that is a defect in the host rather than an answer to a request.
                logger.LogError(
                    "Endpoint {Endpoint} demands a resource scope that the request pipeline never "
                    + "resolved. Call UseTailor360ResourceScope() between UseRouting() and "
                    + "UseAuthorization().",
                    context.GetEndpoint()?.DisplayName ?? "(unknown)");
            }

            var (status, code, title, detail) = Describe(refusal);
            await SecurityProblemDetails.WriteAsync(context, status, code, title, detail);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>
    /// Reduces the failed requirements to the one class of refusal worth telling the caller about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Several requirements can fail at once — a caller may lack the permission <em>and</em> be asking
    /// for another branch's row — and the order below is the order of coarseness. Answering with the
    /// coarsest failure is both the least informative to somebody probing and the most useful to
    /// somebody debugging, because it names the first gate that would have to be passed.
    /// </para>
    /// <para>
    /// It is public because the denial recorder classifies the same failure the same way. A trail that
    /// said one thing while the caller was answered another would be worse than no trail, so both read
    /// this method rather than each deciding for itself.
    /// </para>
    /// </remarks>
    /// <param name="failure">The authorisation failure, or null when there was none.</param>
    public static AuthorisationRefusal Classify(AuthorizationFailure? failure)
    {
        if (failure is null)
        {
            return AuthorisationRefusal.PermissionNotHeld;
        }

        var assurance = failure.FailedRequirements
            .OfType<SessionAssuranceRequirement>()
            .Select(requirement => (SessionAssurance?)requirement.Level)
            .FirstOrDefault();

        if (assurance == SessionAssurance.SignInComplete)
        {
            return AuthorisationRefusal.SignInIncomplete;
        }

        if (assurance == SessionAssurance.SecondFactorSatisfied)
        {
            return AuthorisationRefusal.SecondFactorRequired;
        }

        var refusals = failure.FailureReasons.OfType<RefusalReason>().Select(reason => reason.Refusal).ToArray();
        if (refusals.Length == 0)
        {
            return AuthorisationRefusal.PermissionNotHeld;
        }

        // OutsideBranchScope is last on purpose, and it is the one entry here that is not simply "the
        // earliest gate". A caller can fail the branch check and the resource check at once — asking
        // for another branch's row from a session with no reach does both — and when that happens the
        // resource's own refusal is the answer, because a row in another branch and a row that is not
        // there have to be answered identically and 404 is what that answer is. Telling them instead
        // that their account may not do this would let a caller sort the rows they name into "in my
        // branch" and "not in my branch". The branch refusal is not lost: the denial trail records
        // every reason, and that is where the distinction is wanted.
        AuthorisationRefusal[] byCoarseness =
        [
            AuthorisationRefusal.PipelineIncomplete,
            AuthorisationRefusal.NotAuthenticated,
            AuthorisationRefusal.SignInIncomplete,
            AuthorisationRefusal.PermissionNotHeld,
            AuthorisationRefusal.SecondFactorRequired,
            AuthorisationRefusal.StepUpRequired,
            AuthorisationRefusal.ResourceUnreachable,
            AuthorisationRefusal.NotAssigned,
            AuthorisationRefusal.OutsideBranchScope,
        ];

        return byCoarseness.FirstOrDefault(refusals.Contains, AuthorisationRefusal.PermissionNotHeld);
    }

    /// <summary>
    /// The status a caller refused for this reason is answered with.
    /// </summary>
    /// <remarks>
    /// The denial recorder writes it into the audit entry, so that the trail says what the caller was
    /// actually given. Deriving it from "forbidden" would record 403 for every cross-branch attempt,
    /// which is served as 404 on purpose — and those are the entries an investigation reads first.
    /// </remarks>
    /// <param name="refusal">The class of refusal.</param>
    public static int StatusFor(AuthorisationRefusal refusal) => Describe(refusal).Status;

    private static (int Status, string Code, string Title, string Detail) Describe(AuthorisationRefusal refusal)
        => refusal switch
        {
            AuthorisationRefusal.NotAuthenticated => (
                StatusCodes.Status401Unauthorized,
                AuthenticationRequiredCode,
                "Sign in to continue",
                "This request needs a signed-in session. Sign in and try again."),

            AuthorisationRefusal.SignInIncomplete => (
                StatusCodes.Status403Forbidden,
                SignInIncompleteCode,
                "Not allowed",
                "Finish signing in before using this. Answer the step your sign-in asked for."),

            AuthorisationRefusal.SecondFactorRequired => (
                StatusCodes.Status403Forbidden,
                SecondFactorRequiredCode,
                "Not allowed",
                "Answer your second factor before doing this."),

            AuthorisationRefusal.StepUpRequired => (
                StatusCodes.Status403Forbidden,
                StepUpRequiredCode,
                "Confirm it is you",
                "This action needs you to confirm your identity again. Re-authenticate and it will carry on."),

            AuthorisationRefusal.ResourceUnreachable => (
                StatusCodes.Status404NotFound,
                ResourceNotFoundCode,
                "Not found",
                "No record matching that reference is available to this account."),

            AuthorisationRefusal.NotAssigned => (
                StatusCodes.Status403Forbidden,
                NotAssignedCode,
                "Not allowed",
                "This work is assigned to somebody else. Your workshop lead can reassign it."),

            _ => (
                StatusCodes.Status403Forbidden,
                ForbiddenCode,
                "Not allowed",
                "This account is not allowed to do that."),
        };
}
