using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Orders.Api;

/// <summary>
/// The Orders module's HTTP surface, covering estimates, orders, garment jobs, snapshots, the production workflow, QC and alterations.
/// Every endpoint added here must declare an authorisation policy through
/// <c>RequirePermission</c> or an explicit anonymous justification (architecture rule ARCH-007).
/// </summary>
public static class OrdersEndpoints
{
    /// <summary>The route prefix for this module. The major API version is part of the path (D18).</summary>
    public const string GroupPrefix = "/api/v1/orders";

    /// <summary>The OpenAPI tag applied to this module's operations.</summary>
    public const string OpenApiTag = "Orders";

    /// <summary>Maps the module's endpoint group.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var orders = endpoints.MapGroup(GroupPrefix).WithTags(OpenApiTag);

        orders.MapGet("/drafts", async Task<IResult> (
                HttpContext context,
                ICurrentUser caller,
                OrderDraftHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return ProblemResults.From(
                        context, StatusCodes.Status400BadRequest, "orders.branch-required",
                        "Choose a branch", "Select a branch before viewing order drafts.");
                }

                var drafts = await handler.ListRecentAsync(
                    caller.Context.OrganisationId, branchId, cancellationToken);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new RecentOrderDraftsPayload(
                    [.. drafts.Select(RecentOrderDraftPayload.From)]));
            })
            .Produces<RecentOrderDraftsPayload>(StatusCodes.Status200OK)
            .WithName("ListRecentOrderDrafts")
            .WithSummary("List the 25 most recently edited active drafts in the current branch.")
            .WithDescription("Returns branch and organisation scoped intake drafts that have not expired. Open a draft to read its full garment list and current ETag.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        orders.MapPost("/drafts", async Task<IResult> (
                HttpContext context,
                ICurrentUser caller,
                OrderDraftHandler handler,
                StartOrderDraftRequest request,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return ProblemResults.From(
                        context, StatusCodes.Status400BadRequest, "orders.branch-required",
                        "Choose a branch", "Select a branch before starting an order draft.");
                }

                var result = await handler.StartAsync(
                    request.CustomerId, caller.Context.OrganisationId, branchId,
                    caller.UserId, caller.Permissions, cancellationToken);
                if (result.IsFailure)
                {
                    return Failure(context, result.Error);
                }

                context.Response.SetEntityTag(result.Value.Tag);
                return Results.Created(
                    $"{GroupPrefix}/drafts/{result.Value.Draft.Id}",
                    OrderDraftPayload.From(result.Value.Draft));
            })
            .Produces<OrderDraftPayload>(StatusCodes.Status201Created)
            .WithName("StartOrderDraft")
            .WithSummary("Start an unpriced order draft for a customer in the current organisation.")
            .WithDescription("Copies the current customer identity into a branch-owned intake draft. The draft expires after 72 hours and is not a confirmed order.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited("orders.draft_create")
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        orders.MapGet("/drafts/{draftId:guid}", async Task<IResult> (
                HttpContext context,
                ICurrentUser caller,
                OrderDraftHandler handler,
                Guid draftId,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return ProblemResults.From(
                        context, StatusCodes.Status400BadRequest, "orders.branch-required",
                        "Choose a branch", "Select a branch before opening an order draft.");
                }

                var result = await handler.ReadAsync(
                    draftId, caller.Context.OrganisationId, branchId, cancellationToken);
                if (result.IsFailure)
                {
                    return Failure(context, result.Error);
                }

                context.Response.SetEntityTag(result.Value.Tag);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(OrderDraftPayload.From(result.Value.Draft));
            })
            .Produces<OrderDraftPayload>(StatusCodes.Status200OK)
            .WithName("GetOrderDraft")
            .WithSummary("Read a branch-owned order draft and its current ETag.")
            .WithDescription("Returns the customer snapshot and garments selected so far. The response ETag is required when adding a garment.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrderDraftResourceKinds.Draft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        orders.MapPost("/drafts/{draftId:guid}/garments", async Task<IResult> (
                HttpContext context,
                ICurrentUser caller,
                OrderDraftHandler handler,
                Guid draftId,
                AddDraftGarmentRequest request,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return ProblemResults.From(
                        context, StatusCodes.Status400BadRequest, "orders.branch-required",
                        "Choose a branch", "Select a branch before editing an order draft.");
                }

                var current = await handler.ReadAsync(
                    draftId, caller.Context.OrganisationId, branchId, cancellationToken);
                if (current.IsFailure)
                {
                    return Failure(context, current.Error);
                }

                var refused = ConcurrencyResults.CheckIfMatch(
                    context, current.Value.Tag, "orders.draft.changed",
                    "This draft changed. Review the new version before adding this garment.");
                if (refused is not null)
                {
                    return refused;
                }

                _ = context.Request.TryGetIfMatch(out var expected);
                var result = await handler.AddGarmentAsync(
                    draftId, caller.Context.OrganisationId, branchId, caller.UserId,
                    request.ServiceTypeId, request.Quantity, request.Notes,
                    expected, cancellationToken);
                if (result.IsFailure)
                {
                    return Failure(context, result.Error);
                }

                context.Response.SetEntityTag(result.Value.Tag);
                return Results.Ok(OrderDraftPayload.From(result.Value.Draft));
            })
            .Produces<OrderDraftPayload>(StatusCodes.Status200OK)
            .WithName("AddOrderDraftGarment")
            .WithSummary("Add a published catalogue service to an unpriced order draft.")
            .WithDescription("Requires If-Match and Idempotency-Key. The selected service and catalogue version are pinned to the garment; this does not reserve stock or quote a price.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrderDraftResourceKinds.Draft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited("orders.draft_garment_added")
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return endpoints;
    }

    private static IResult Failure(HttpContext context, Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
        return ProblemResults.From(context, status, error.Code, "Order draft could not be saved", error.Message);
    }
}
