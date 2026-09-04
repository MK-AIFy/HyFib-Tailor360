using Microsoft.AspNetCore.Authorization;
using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Evaluates <see cref="BranchScopeRequirement"/> against the caller's branch assignments.
/// Organisation-wide reach is not implied by holding many branches; it is a separate demand that only
/// an organisation-scoped role satisfies.
/// </summary>
/// <param name="currentUser">The caller.</param>
public sealed class BranchScopeAuthorisationHandler(ICurrentUser currentUser)
    : AuthorizationHandler<BranchScopeRequirement>
{
    /// <summary>The permission that grants reach across every branch.</summary>
    public const string OrganisationWidePermission = "admin.organisation.read_all_branches";

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BranchScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!currentUser.IsAuthenticated)
        {
            context.Fail(new AuthorizationFailureReason(this, "Not authenticated."));
            return Task.CompletedTask;
        }

        var satisfied = requirement.Scope switch
        {
            BranchScope.CurrentBranch =>
                currentUser.Context.BranchId is { } branchId && currentUser.CanActInBranch(branchId),
            BranchScope.AssignedBranches => currentUser.AssignedBranches.Count > 0,
            BranchScope.Organisation => currentUser.HasPermission(OrganisationWidePermission),
            _ => false,
        };

        if (satisfied)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, "Outside the caller's branch scope."));
        }

        return Task.CompletedTask;
    }
}
