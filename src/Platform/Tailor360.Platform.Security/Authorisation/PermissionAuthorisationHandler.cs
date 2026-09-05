using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Evaluates <see cref="PermissionRequirement"/>. The handler fails closed: an unknown permission key,
/// an unauthenticated caller, a missing multi-factor session or a stale step-up all deny the request.
/// </summary>
/// <param name="currentUser">The caller.</param>
/// <param name="catalogue">The permission catalogue, used to read the permission's session demands.</param>
/// <param name="clock">The clock, used to evaluate step-up freshness.</param>
/// <param name="stepUpOptions">Step-up freshness configuration.</param>
public sealed class PermissionAuthorisationHandler(
    ICurrentUser currentUser,
    PermissionCatalogue catalogue,
    IClock clock,
    IOptions<StepUpOptions> stepUpOptions)
    : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var permission = catalogue.Find(requirement.PermissionKey);
        if (permission is null)
        {
            // An endpoint referring to a permission that no module declares is a defect, not a
            // permitted request. Failing closed keeps a typo from opening an endpoint to everyone.
            context.Fail(new AuthorizationFailureReason(this, "Unknown permission."));
            return Task.CompletedTask;
        }

        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(permission.Key))
        {
            context.Fail(new AuthorizationFailureReason(this, "Permission not held."));
            return Task.CompletedTask;
        }

        // A session that has answered only the first factor holds no permission, whatever its roles
        // grant. It exists to finish the sign-in and to end itself, and this is the second place that
        // says so — the first being the assurance requirement on the self-service endpoints.
        if (!currentUser.IsSignInComplete)
        {
            context.Fail(new AuthorizationFailureReason(this, "The sign-in has not been completed."));
            return Task.CompletedTask;
        }

        if (permission.RequiresMfa && !currentUser.MfaSatisfied)
        {
            context.Fail(new AuthorizationFailureReason(this, "Multi-factor authentication required."));
            return Task.CompletedTask;
        }

        if (permission.RequiresStepUp && !IsStepUpFresh())
        {
            context.Fail(new AuthorizationFailureReason(this, "Recent re-authentication required."));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private bool IsStepUpFresh()
    {
        var last = currentUser.LastReauthenticatedAt;
        return last is not null && clock.UtcNow - last.Value <= stepUpOptions.Value.Freshness;
    }
}
