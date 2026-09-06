using Microsoft.AspNetCore.Authorization;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Permissions;

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
    /// <summary>
    /// The permission that grants reach across every branch. It is the catalogue's own key rather than
    /// a copy of the string, so a rename cannot leave this handler asking for a permission nobody has.
    /// </summary>
    public const string OrganisationWidePermission = PlatformPermissions.ReadAllBranches;

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BranchScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!currentUser.IsAuthenticated)
        {
            context.Fail(new RefusalReason(
                this, AuthorisationRefusal.NotAuthenticated, "Not authenticated."));

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
            // Typed, not a bare message. The denial trail classifies a refusal by reading these, and a
            // branch-scope refusal recorded as "permission not held" would mislabel the one pattern the
            // trail exists to make visible: somebody with the permission reaching outside their branch.
            context.Fail(new RefusalReason(
                this, AuthorisationRefusal.OutsideBranchScope, "Outside the caller's branch scope."));
        }

        return Task.CompletedTask;
    }
}
