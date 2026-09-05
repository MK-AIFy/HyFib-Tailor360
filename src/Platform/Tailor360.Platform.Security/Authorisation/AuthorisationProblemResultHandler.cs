using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
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
/// exactly the moment not to describe the lock.
/// </para>
/// </remarks>
public sealed class AuthorisationProblemResultHandler : IAuthorizationMiddlewareResultHandler
{
    /// <summary>The code returned when a request carried no usable session.</summary>
    public const string AuthenticationRequiredCode = "security.authentication-required";

    /// <summary>The code returned when the caller has not finished signing in.</summary>
    public const string SignInIncompleteCode = "security.sign-in-incomplete";

    /// <summary>The code returned when the caller's session has not satisfied a second factor.</summary>
    public const string SecondFactorRequiredCode = "security.second-factor-required";

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
            var (code, detail) = Describe(authorizeResult.AuthorizationFailure);

            await SecurityProblemDetails.WriteAsync(
                context, StatusCodes.Status403Forbidden, code, "Not allowed", detail);

            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>
    /// Names the class of refusal from the requirement that was not met. The assurance requirement is
    /// singled out because its two failures are the ones a client can act on: one means "finish signing
    /// in", the other "answer your second factor", and both are recoverable without anybody's help.
    /// </summary>
    private static (string Code, string Detail) Describe(AuthorizationFailure? failure)
    {
        var assurance = failure?.FailedRequirements
            .OfType<SessionAssuranceRequirement>()
            .Select(requirement => (SessionAssurance?)requirement.Level)
            .FirstOrDefault();

        return assurance switch
        {
            SessionAssurance.SignInComplete => (
                SignInIncompleteCode,
                "Finish signing in before using this. Answer the step your sign-in asked for."),
            SessionAssurance.SecondFactorSatisfied => (
                SecondFactorRequiredCode,
                "Answer your second factor before changing this account's security settings."),
            _ => (ForbiddenCode, "This account is not allowed to do that."),
        };
    }
}
