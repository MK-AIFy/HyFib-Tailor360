using Microsoft.AspNetCore.Authorization;
using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Evaluates <see cref="ResourceBranchRequirement"/> against the branch that owns the resource the
/// request named.
/// </summary>
/// <param name="currentUser">The caller.</param>
/// <param name="resourceScope">The resource loaded for this request.</param>
public sealed class ResourceBranchAuthorisationHandler(
    ICurrentUser currentUser,
    ResourceScopeContext resourceScope)
    : AuthorizationHandler<ResourceBranchRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceBranchRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!currentUser.IsAuthenticated)
        {
            Refuse(context, AuthorisationRefusal.NotAuthenticated, "Not authenticated.");
            return Task.CompletedTask;
        }

        switch (resourceScope.Status)
        {
            case ResourceScopeStatus.Resolved when CanReach(resourceScope.Scope!, requirement.Scope):
                context.Succeed(requirement);
                break;

            case ResourceScopeStatus.Resolved:
            case ResourceScopeStatus.NotFound:
                // The two answer alike on purpose. A caller who edits an identifier in the address bar
                // learns the same thing whether the record is somebody else's or nobody's: nothing.
                Refuse(context, AuthorisationRefusal.ResourceUnreachable, "Resource outside the caller's reach.");
                break;

            default:
                // Unresolved means the resolution step never ran; NotRequired means it ran and found no
                // resource declared, which cannot happen on an endpoint carrying this requirement.
                // Either way the pipeline did not answer the question, so the request does not pass.
                Refuse(
                    context,
                    AuthorisationRefusal.PipelineIncomplete,
                    "The endpoint requires a resource scope and none was resolved.");
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The declared reach decides, and all three values mean different things.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BranchScope.CurrentBranch"/> is the narrowest and the most common: the row has to
    /// belong to the branch the caller's session is <em>working in</em>, not merely to one of the
    /// branches they could switch to. A person assigned to Chennai and Coimbatore, working in Chennai,
    /// is refused a Coimbatore row here until they switch — which is what makes the branch switcher a
    /// deliberate act with an audit trail rather than a label on a screen.
    /// </para>
    /// <para>
    /// <see cref="BranchScope.AssignedBranches"/> is the widening: any branch the caller is assigned
    /// to, whichever one the session happens to be working in. An endpoint declares it when the action
    /// genuinely spans the person's branches.
    /// </para>
    /// <para>
    /// <see cref="BranchScope.Organisation"/> adds the reach permission, and adds it only for reading:
    /// a caller who holds it may read a row in a branch they do not work in, while the same caller
    /// writing to that row is declared <see cref="BranchScope.CurrentBranch"/> by the endpoint and is
    /// refused above — which is what keeps "reach only, never write" true rather than merely written
    /// down.
    /// </para>
    /// </remarks>
    private bool CanReach(ResourceScope scope, BranchScope declared) => declared switch
    {
        BranchScope.CurrentBranch =>
            currentUser.Context.BranchId == scope.BranchId && currentUser.CanActInBranch(scope.BranchId),

        BranchScope.AssignedBranches => currentUser.CanActInBranch(scope.BranchId),

        BranchScope.Organisation =>
            currentUser.CanActInBranch(scope.BranchId)
            || currentUser.HasPermission(BranchScopeAuthorisationHandler.OrganisationWidePermission),

        _ => false,
    };

    private void Refuse(AuthorizationHandlerContext context, AuthorisationRefusal refusal, string message)
        => context.Fail(new RefusalReason(this, refusal, message));
}
