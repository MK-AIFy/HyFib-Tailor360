using Microsoft.AspNetCore.Authorization;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Evaluates <see cref="SessionAssuranceRequirement"/>. It fails closed: an unauthenticated caller, a
/// sign-in that is still outstanding and an unsatisfied second factor all deny.
/// </summary>
/// <remarks>
/// This is the control that keeps a password-only session inside the sign-in flow. Every level is
/// evaluated from the ticket, which was rebuilt from the session row on this request, so nothing here
/// depends on a value the client supplied or on a step the client claims to have reached.
/// </remarks>
/// <param name="currentUser">The caller.</param>
public sealed class SessionAssuranceAuthorisationHandler(ICurrentUser currentUser)
    : AuthorizationHandler<SessionAssuranceRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SessionAssuranceRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (Satisfies(requirement.Level))
        {
            context.Succeed(requirement);
        }

        // Deliberately not context.Fail(). An explicit fail short-circuits the whole evaluation and
        // reports itself with no requirements attached, which leaves the middleware result handler
        // unable to say which assurance was missing — and "answer your second factor" and "you do not
        // have permission" are two different things for the person reading the screen. Simply not
        // succeeding leaves this requirement in the failure's unmet set, which is what it is.
        return Task.CompletedTask;
    }

    private bool Satisfies(SessionAssurance level) => level switch
    {
        SessionAssurance.LiveSession => currentUser.IsAuthenticated,
        SessionAssurance.SignInComplete => currentUser.IsAuthenticated && currentUser.IsSignInComplete,
        SessionAssurance.SecondFactorSatisfied =>
            currentUser.IsAuthenticated && currentUser.IsSignInComplete && currentUser.MfaSatisfied,

        // An unrecognised level is a defect, not a permitted request. Denying keeps a future value
        // added without a case here from opening an endpoint to everyone.
        _ => false,
    };
}
