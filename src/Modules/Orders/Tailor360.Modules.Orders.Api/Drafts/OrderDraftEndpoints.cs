using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Orders.Api.Payloads;
using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Orders.Api.Drafts;

/// <summary>
/// The order draft lifecycle: starting a draft, reading it, and re-pointing its customer or its
/// order-level schedule (#199). Adding, saving and removing garment sections, and their dependencies,
/// is the module's next slice.
/// </summary>
/// <remarks>
/// <para>
/// The module's first HTTP surface. Every route here declares <see cref="OrdersPermissions.Intake"/> at
/// <see cref="BranchScope.CurrentBranch"/> — the grant <c>docs/prd/state-transitions.md</c> line 125
/// names for creating a draft, and the same one every edit demands, because a draft is shared within the
/// branch that started it.
/// </para>
/// <para>
/// <strong>Every route naming a draft is resource-scoped to it</strong> through
/// <see cref="OrderDraftScopeResolver"/> — reads and writes as not-found for another branch's draft,
/// through the identifier-editing oracle the authorisation design closes.
/// </para>
/// </remarks>
public static class OrderDraftEndpoints
{
    /// <summary>Maps the order draft lifecycle routes under the Orders group.</summary>
    /// <param name="orders">The Orders group.</param>
    /// <returns>The group.</returns>
    public static RouteGroupBuilder MapOrderDraftEndpoints(this RouteGroupBuilder orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        orders.MapPost(
                "/drafts",
                async Task<IResult> (
                    StartOrderDraftRequest request,
                    HttpContext context,
                    OrderDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    if (caller.Context.BranchId is not { } branchId)
                    {
                        return Problems.From(OrdersErrors.NoBranchInContext, context);
                    }

                    var result = await handler.StartAsync(
                        new StartOrderDraftCommand(
                            request.CustomerId,
                            request.DueDate,
                            request.Notes,
                            caller.Context.OrganisationId,
                            branchId,
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Created(
                        $"/api/v1/orders/drafts/{result.Value.Draft.Id}",
                        OrderDraftPayload.From(result.Value.Draft));
                })
            .Produces<OrderDraftPayload>(StatusCodes.Status201Created)
            .WithName("StartOrderDraft")
            .WithSummary("Start an order draft.")
            .WithDescription(
                "Shared within the branch: every user holding orders.intake sees it and may carry it on. "
                + "Expires after the branch's configured window, default 72 hours, at which point every "
                + "further edit is refused. The retention sweep that removes an expired row is separate "
                + "worker infrastructure, not part of this route.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(OrderDraftHandler.DraftCreatedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        orders.MapGet(
                "/drafts/{draftId:guid}",
                async Task<IResult> (
                    Guid draftId,
                    HttpContext context,
                    OrderDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.ReadAsync(draftId, caller.Context.OrganisationId, cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(OrderDraftPayload.From(result.Value.Draft));
                })
            .Produces<OrderDraftPayload>(StatusCodes.Status200OK)
            .WithName("GetOrderDraft")
            .WithSummary("Read an order draft.")
            .WithDescription(
                "The entity tag is what an edit to the order-level fields sends back as If-Match. "
                + "Garment sections carry their own tag, once #199's second slice adds them.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrdersResourceKinds.OrderDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        orders.MapPut(
                "/drafts/{draftId:guid}/customer",
                async Task<IResult> (
                    Guid draftId,
                    SetOrderDraftCustomerRequest request,
                    HttpContext context,
                    OrderDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.SetCustomerAsync(
                        new SetOrderDraftCustomerCommand(
                            draftId,
                            request.CustomerId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return await ConflictOrProblemAsync(
                            result.Error, draftId, caller.Context.OrganisationId, context, handler, cancellationToken);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(OrderDraftPayload.From(result.Value.Draft));
                })
            .Produces<OrderDraftPayload>(StatusCodes.Status200OK)
            .WithName("SetOrderDraftCustomer")
            .WithSummary("Point a draft at a different customer.")
            .WithDescription(
                "Corrects a mis-selection at the counter: the garment sections are kept, because they "
                + "describe the garments and not the person.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrdersResourceKinds.OrderDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(OrderDraftHandler.CustomerSetAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        orders.MapPut(
                "/drafts/{draftId:guid}/schedule",
                async Task<IResult> (
                    Guid draftId,
                    SetOrderDraftScheduleRequest request,
                    HttpContext context,
                    OrderDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.SetScheduleAsync(
                        new SetOrderDraftScheduleCommand(
                            draftId,
                            request.DueDate,
                            request.Notes,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return await ConflictOrProblemAsync(
                            result.Error, draftId, caller.Context.OrganisationId, context, handler, cancellationToken);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(OrderDraftPayload.From(result.Value.Draft));
                })
            .Produces<OrderDraftPayload>(StatusCodes.Status200OK)
            .WithName("SetOrderDraftSchedule")
            .WithSummary("Set the order-level promised date and notes.")
            .WithDescription(
                "Replaces both fields together, including clearing one by sending null: the aggregate "
                + "writes them as one whole value, never a partial update.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrdersResourceKinds.OrderDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(OrderDraftHandler.ScheduleSetAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return orders;
    }

    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);

    /// <summary>
    /// Answers a failed edit, carrying the row's current tag when the failure was a stale
    /// <c>If-Match</c> — the caller re-reads only to render a generic problem, otherwise.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>CustomerEndpoints</c>' merge route: on <c>orders.concurrent-change</c>, read the
    /// draft again for its current tag and answer through <see cref="ConcurrencyResults.VersionConflict"/>,
    /// which sets the <c>ETag</c> header and a <c>currentVersion</c> body member so the losing caller
    /// can offer a merge without a second manual round trip. The code and message are the domain
    /// error's own — this changes the response's shape, not its vocabulary.
    /// </remarks>
    private static async Task<IResult> ConflictOrProblemAsync(
        Error error,
        Guid draftId,
        Guid organisationId,
        HttpContext context,
        OrderDraftHandler handler,
        CancellationToken cancellationToken)
    {
        if (error != OrdersErrors.ConcurrentChange)
        {
            return Problems.From(error, context);
        }

        var current = await handler.ReadAsync(draftId, organisationId, cancellationToken);

        return current.IsFailure
            ? Problems.From(current.Error, context)
            : ConcurrencyResults.VersionConflict(context, error.Code, error.Message, current.Value.Tag);
    }
}
