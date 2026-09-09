using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.FieldVisibility;
using Tailor360.Platform.Security.Permissions;
using Tailor360.Web.Timeline;

namespace Tailor360.Web.Endpoints;

/// <summary>
/// The customer timeline: every module's history of one person, merged into one page.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the first endpoint the host owns rather than a module.</strong> It is here because
/// no module could answer it: the timeline is the union of what several modules know, and a module that
/// read another module's tables to build it would be the thing the whole architecture is arranged to
/// prevent. So each module implements <see cref="ITimelineSource"/> over the facts it owns, this route
/// asks all of them, and <see cref="CustomerTimelineComposer"/> merges the answers.
/// <c>docs/architecture/architecture-rules.md</c> records the arrangement as ROD-02 and
/// <c>docs/architecture/components.md</c> says the composition endpoint lives in the host "not in
/// Customers".
/// </para>
/// <para>
/// Today Customers is the only source. The route is written for several because it is cheaper to write
/// the merge once than to discover, when Orders lands, that the shape assumed one — and because the
/// partial-failure behaviour is not something to bolt on afterwards: a source that fails is named in the
/// response and the rest of the history is still served.
/// </para>
/// <para>
/// <strong>What it does not do is decide what a caller may see.</strong> Whether an entry appears at all
/// is the contributing module's decision, taken from the caller's permissions — Customers withholds a
/// consent entry from somebody without <c>customers.read_consent</c>, because the entry's own title says
/// the thing that permission exists to gate. Which <em>fields</em> of an entry come back is the approved
/// response view <c>customers.timeline</c>, exactly as for the record. The host merges and pages; it
/// does not mask.
/// </para>
/// </remarks>
public static class CustomerTimelineEndpoints
{
    /// <summary>The route the customer detail screen reads.</summary>
    public const string Path = "/api/v1/customers/{customerId:guid}/timeline";

    /// <summary>Where the reach and permission choices on this route were reviewed.</summary>
    private const string Review = "#26, docs/prd/workflows/branch-scenarios.md";

    /// <summary>
    /// Why a route naming a customer declares no resource scope (ARCH-023).
    /// </summary>
    /// <remarks>
    /// The same reason every customer route gives, restated rather than shared because the module's
    /// copy is private to its own endpoint class and a constant reached across a project boundary for
    /// the sake of not retyping it would be a worse dependency than the retyping.
    /// </remarks>
    private const string NoBranchResource =
        "A customer record is organisation-wide and belongs to no one branch, so there is no branch for "
        + "a resource scope to resolve it to: what is branch-scoped is the set of branches that can see "
        + "it, which grows whenever a second branch serves the person (branch-scenarios.md section 3.2). "
        + "Reach is enforced instead where it means something — this route refuses a customer outside "
        + "the caller's organisation before it asks a single source for an entry, and each source "
        + "withholds the entries the caller's permissions do not reach.";

    private const string NoStore = "no-store";

    private const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>Maps the timeline endpoint.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The route builder.</returns>
    public static IEndpointRouteBuilder MapCustomerTimelineEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(Path, async Task<IResult> (
                Guid customerId,
                HttpContext context,
                ICustomerSnapshotQuery customers,
                CustomerTimelineComposer composer,
                IFieldVisibilityPolicy views,
                ICurrentUser caller,
                CancellationToken cancellationToken,
                string? cursor = null,
                int limit = TimelineQuery.DefaultLimit) =>
            {
                // Existence first, and through the module's published contract rather than by asking a
                // source and inferring it from an empty answer. An unknown customer and a customer
                // nothing has happened to are different things, and answering both with an empty
                // timeline would make the second look like a bug and the first look like a record.
                var customer = await customers.GetAsync(
                    customerId, [.. caller.Permissions], cancellationToken);

                if (customer is null)
                {
                    return NotFound(context);
                }

                var page = await composer.ComposeAsync(
                    new CustomerTimelineRequest(
                        customerId,
                        caller.Context,
                        caller.AssignedBranches,
                        caller.Permissions,
                        cursor,
                        limit),
                    cancellationToken);

                context.Response.Headers.CacheControl = NoStore;

                return Results.Ok(CustomerTimelinePayload.From(
                    page, views.MaskFor(CustomersResponseViews.Timeline)));
            })
            .Produces<CustomerTimelinePayload>(StatusCodes.Status200OK)
            .WithName("GetCustomerTimeline")
            .WithSummary("Read one customer's history, merged from every module that holds part of it.")
            .WithDescription(
                "Newest first, cursor-paged. An entry appears only when the caller's permissions reach "
                + "it: consent and communication-preference entries need `customers.read_consent`, "
                + "subject-access export entries need `customers.export`, and the reason an actor gave "
                + "needs `customers.read_notes`. `unavailableSources` names any module that could not "
                + "answer, so a gap in the history is visible rather than silent.")
            .WithTags("Customers")
            .RequirePermission(CustomersPermissions.Read, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        return endpoints;
    }

    /// <summary>
    /// The same answer the Customers module gives for an identifier that names no record.
    /// </summary>
    /// <remarks>
    /// Built here rather than borrowed, because the code lives on a <c>Domain</c> type and a host may
    /// not reference one (ARCH-006). The string is repeated deliberately and a contract test holds the
    /// two equal, so a client cannot be handed two different codes for one condition.
    /// </remarks>
    private static IResult NotFound(HttpContext context)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = CustomerTimelinePayload.CustomerNotFound,
            ["retryable"] = false,
        };

        var correlationId = context.Response.Headers[CorrelationHeader].ToString();
        if (correlationId.Length > 0)
        {
            extensions["correlationId"] = correlationId;
        }

        return Results.Problem(
            detail: "No customer matches that identifier.",
            instance: context.Request.Path,
            statusCode: StatusCodes.Status404NotFound,
            title: "Not found",
            type: $"urn:tailor360:problem:{CustomerTimelinePayload.CustomerNotFound}",
            extensions: extensions);
    }
}
