using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Catalog.Api.Payloads;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Catalog.Api.Catalogue;

/// <summary>
/// What the counter may order, right now, at the branch the caller is working in.
/// </summary>
/// <remarks>
/// <para>
/// The one catalogue route that is not administration, and the only one every operational role holds a
/// permission for. It answers from the published version and applies the whole orderability rule at
/// once: a grouping node's services, a category outside its active period, a branch that does not
/// offer it, a category behind a flag that is off and a service published with a link missing are all
/// absent. Intake neither lists nor accepts anything else, and that is the point — a screen that
/// offered something the confirmation then refused would be worse than one that offered nothing.
/// </para>
/// <para>
/// <strong>It declares <see cref="BranchScope.CurrentBranch"/>, and the permission is branch-scoped
/// too.</strong> The answer genuinely differs per branch, because availability is a set of branches on
/// each category and a subset of the parent's on each sub-category
/// (<c>docs/prd/category-hierarchy.md</c> section 8). Asking "what may I order" without saying where
/// is not a question with an answer.
/// </para>
/// </remarks>
public static class CurrentCatalogEndpoints
{
    /// <summary>A session working in no branch cannot be told what its branch may order.</summary>
    private static readonly Error NoBranch = Error.Validation(
        "catalog.branch-required",
        "This session is not working in a branch, and what may be ordered is a different list at each "
        + "branch. Choose a branch and ask again.",
        "branch");

    /// <summary>Maps the read route.</summary>
    /// <param name="catalog">The <c>/api/v1/catalog</c> group.</param>
    /// <returns>The same group, for chaining.</returns>
    public static RouteGroupBuilder MapCurrentCatalogEndpoints(this RouteGroupBuilder catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        catalog.MapGet("/current", async Task<IResult> (
                HttpContext context,
                ICatalogAvailabilityQuery availability,
                ICurrentUser caller,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    // Not a permission failure: the caller may well hold catalog.read. It is a
                    // question that cannot be answered, because what a branch offers is the answer.
                    // Declared here rather than taken from the module's error list, because the
                    // condition is about the session this route was reached with and not about the
                    // catalogue — and because the Api layer's own list of what it may reference does
                    // not include Domain.
                    return Problems.From(NoBranch, context);
                }

                // One call, because the version and the services have to be the same answer. Asking
                // for them separately would let a publication land between the two reads and label
                // one version's services with another version's identifier.
                var catalogue = await availability.GetOrderableCatalogAsync(
                    caller.Context.OrganisationId, branchId, clock.UtcNow, cancellationToken);

                return Results.Ok(new OrderableCatalogPayload(
                    catalogue.VersionId,
                    branchId,
                    [.. catalogue.Services.Select(OrderableServicePayload.From)]));
            })
            .Produces<OrderableCatalogPayload>(StatusCodes.Status200OK)
            .WithName("GetCurrentCatalog")
            .WithSummary("List what the caller's branch may order today.")
            .WithDescription(
                "Answers from the published catalogue version, filtered by the branch's availability, "
                + "the active dates read in the branch's timezone, the category's feature flag and "
                + "whether the service was published complete. An empty list is a real answer: it "
                + "means nothing has been published yet, or nothing is offered at this branch.")
            .RequirePermission(CatalogPermissions.Read, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        return catalog;
    }
}
