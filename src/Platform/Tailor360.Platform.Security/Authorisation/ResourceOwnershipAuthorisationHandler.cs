using Microsoft.AspNetCore.Authorization;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Evaluates <see cref="ResourceOwnershipRequirement"/>: the caller is the assignee, or holds a
/// permission that supervises the assignment.
/// </summary>
/// <param name="currentUser">The caller.</param>
/// <param name="resourceScope">The resource loaded for this request.</param>
public sealed class ResourceOwnershipAuthorisationHandler(
    ICurrentUser currentUser,
    ResourceScopeContext resourceScope)
    : AuthorizationHandler<ResourceOwnershipRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnershipRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!currentUser.IsAuthenticated)
        {
            Refuse(context, AuthorisationRefusal.NotAuthenticated, "Not authenticated.");
            return Task.CompletedTask;
        }

        if (resourceScope.Status != ResourceScopeStatus.Resolved || resourceScope.Scope is not { } scope)
        {
            // Ownership cannot be decided without the resource. The branch requirement on the same
            // endpoint has already refused this request; refusing again keeps the two independent, so
            // that removing one never quietly makes the other permissive.
            Refuse(
                context,
                resourceScope.Status == ResourceScopeStatus.NotFound
                    ? AuthorisationRefusal.ResourceUnreachable
                    : AuthorisationRefusal.PipelineIncomplete,
                "No resolved resource to decide ownership against.");

            return Task.CompletedTask;
        }

        var supervises = requirement.SupervisorPermissions.Any(currentUser.HasPermission);
        if (supervises || scope.IsAssignedTo(currentUser.UserId))
        {
            context.Succeed(requirement);
        }
        else
        {
            Refuse(context, AuthorisationRefusal.NotAssigned, "Resource assigned to somebody else.");
        }

        return Task.CompletedTask;
    }

    private void Refuse(AuthorizationHandlerContext context, AuthorisationRefusal refusal, string message)
        => context.Fail(new RefusalReason(this, refusal, message));
}
