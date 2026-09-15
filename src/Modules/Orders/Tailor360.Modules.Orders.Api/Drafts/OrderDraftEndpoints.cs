using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Orders.Api.Payloads;
using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Orders.Api.Drafts;

/// <summary>
/// The order draft lifecycle (#199): starting a draft, reading it, re-pointing its customer or its
/// order-level schedule, and adding, saving, removing and declaring dependencies between its garment
/// sections.
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
                        OrderDraftPayload.From(result.Value.Draft, result.Value.GarmentTags));
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

                    return Results.Ok(OrderDraftPayload.From(result.Value.Draft, result.Value.GarmentTags));
                })
            .Produces<OrderDraftPayload>(StatusCodes.Status200OK)
            .WithName("GetOrderDraft")
            .WithSummary("Read an order draft.")
            .WithDescription(
                "The entity tag is what an edit to the order-level fields sends back as If-Match. "
                + "Each garment section carries its own version in the body, sent back in double quotes as "
                + "If-Match on every edit to that section — so a draft reopened from this read can be edited "
                + "section by section without a further round trip.")
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

                    return Results.Ok(OrderDraftPayload.From(result.Value.Draft, result.Value.GarmentTags));
                })
            .Produces<OrderDraftPayload>(StatusCodes.Status200OK)
            .WithName("SetOrderDraftCustomer")
            .WithSummary("Point a draft at a different customer.")
            .WithDescription(
                "Corrects a mis-selection at the counter: the garment sections are kept, because they "
                + "describe the garments and not the person. Refused while a section still reuses a "
                + "measurement taken for the customer the draft is leaving — change that section first.")
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

                    return Results.Ok(OrderDraftPayload.From(result.Value.Draft, result.Value.GarmentTags));
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

        orders.MapPost(
                "/drafts/{draftId:guid}/garments",
                async Task<IResult> (
                    Guid draftId,
                    AddOrderDraftGarmentRequest request,
                    HttpContext context,
                    OrderDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.AddGarmentAsync(
                        new AddOrderDraftGarmentCommand(
                            draftId,
                            request.CategoryKey,
                            request.ServiceTypeKey,
                            request.DesignSelectionDraftId,
                            DraftRequestParsing.ParseOrUndefined<MeasurementIntent>(request.MeasurementIntent),
                            request.MeasurementVersionId,
                            request.DueDate,
                            request.Instructions,
                            request.ReferenceMediaIds,
                            caller.Context.OrganisationId,
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Created(
                        $"/api/v1/orders/drafts/{draftId}/garments/{result.Value.Garment.Id}",
                        OrderDraftGarmentPayload.From(result.Value.Garment, result.Value.Tag));
                })
            .Produces<OrderDraftGarmentPayload>(StatusCodes.Status201Created)
            .WithName("AddOrderDraftGarment")
            .WithSummary("Add a garment section to a draft.")
            .WithDescription(
                "The category, service type and measurement template are pinned server-side from what "
                + "the branch may order today — the same pin a confirmed order is frozen against — never "
                + "trusted from the request. A reused measurement must be this customer's own and answer "
                + "the template the service is measured by. No If-Match: this creates the section, so "
                + "there is no earlier version to be stale against.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrdersResourceKinds.OrderDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(OrderDraftHandler.GarmentAddedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        orders.MapPut(
                "/drafts/{draftId:guid}/garments/{garmentId:guid}",
                async Task<IResult> (
                    Guid draftId,
                    Guid garmentId,
                    SaveOrderDraftGarmentRequest request,
                    HttpContext context,
                    OrderDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.SaveGarmentAsync(
                        new SaveOrderDraftGarmentCommand(
                            draftId,
                            garmentId,
                            request.CategoryKey,
                            request.ServiceTypeKey,
                            request.DesignSelectionDraftId,
                            DraftRequestParsing.ParseOrUndefined<MeasurementIntent>(request.MeasurementIntent),
                            request.MeasurementVersionId,
                            request.DueDate,
                            request.Instructions,
                            request.ReferenceMediaIds,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return await ConflictOrProblemForGarmentAsync(
                            result.Error, draftId, garmentId, caller.Context.OrganisationId, context, handler,
                            cancellationToken);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(OrderDraftGarmentPayload.From(result.Value.Garment, result.Value.Tag));
                })
            .Produces<OrderDraftGarmentPayload>(StatusCodes.Status200OK)
            .WithName("SaveOrderDraftGarment")
            .WithSummary("Replace the whole content of a garment section.")
            .WithDescription(
                "Every field is replaced together; the section's identity, position and dependencies are "
                + "left alone. The precondition is the section's own tag, not the draft's, and only the "
                + "section's row moves — two counters editing different sections of one draft never "
                + "collide, on either tag. A reused measurement is bound as on adding a section.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrdersResourceKinds.OrderDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(OrderDraftHandler.GarmentSavedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        orders.MapPost(
                "/drafts/{draftId:guid}/garments/{garmentId:guid}/delete",
                async Task<IResult> (
                    Guid draftId,
                    Guid garmentId,
                    HttpContext context,
                    OrderDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.RemoveGarmentAsync(
                        new RemoveOrderDraftGarmentCommand(
                            draftId,
                            garmentId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return await ConflictOrProblemForGarmentAsync(
                            result.Error, draftId, garmentId, caller.Context.OrganisationId, context, handler,
                            cancellationToken);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(OrderDraftPayload.From(result.Value.Draft, result.Value.GarmentTags));
                })
            .Produces<OrderDraftPayload>(StatusCodes.Status200OK)
            .WithName("RemoveOrderDraftGarment")
            .WithSummary("Remove a garment section, and every dependency naming it.")
            .WithDescription(
                "A POST sub-resource rather than DELETE, matching the withdraw-dependency route: no "
                + "permissioned route in this module uses the DELETE verb. Answers with the whole draft, "
                + "not the removed section — the removal can withdraw dependencies on other sections too. "
                + "Precondition is the removed section's own tag.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrdersResourceKinds.OrderDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(OrderDraftHandler.GarmentRemovedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        orders.MapPost(
                "/drafts/{draftId:guid}/garments/{garmentId:guid}/dependencies",
                async Task<IResult> (
                    Guid draftId,
                    Guid garmentId,
                    DeclareOrderDraftDependencyRequest request,
                    HttpContext context,
                    OrderDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.DeclareDependencyAsync(
                        new DeclareOrderDraftDependencyCommand(
                            draftId,
                            garmentId,
                            request.PrerequisiteGarmentId,
                            DraftRequestParsing.ParseOrUndefined<JobDependencyKind>(request.Kind),
                            request.Reason,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return await ConflictOrProblemForGarmentAsync(
                            result.Error, draftId, garmentId, caller.Context.OrganisationId, context, handler,
                            cancellationToken);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(OrderDraftGarmentPayload.From(result.Value.Garment, result.Value.Tag));
                })
            .Produces<OrderDraftGarmentPayload>(StatusCodes.Status200OK)
            .WithName("DeclareOrderDraftDependency")
            .WithSummary("Declare that one section waits for, or is delivered with, another.")
            .WithDescription(
                "Both sections must be on this draft. Declaring touches the dependent section, so its tag "
                + "moves and is a genuine precondition even though the dependency table itself carries none.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrdersResourceKinds.OrderDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(OrderDraftHandler.DependencyDeclaredAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        orders.MapPost(
                "/drafts/{draftId:guid}/garments/{garmentId:guid}/dependencies/delete",
                async Task<IResult> (
                    Guid draftId,
                    Guid garmentId,
                    WithdrawOrderDraftDependencyRequest request,
                    HttpContext context,
                    OrderDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.WithdrawDependencyAsync(
                        new WithdrawOrderDraftDependencyCommand(
                            draftId,
                            garmentId,
                            request.PrerequisiteGarmentId,
                            DraftRequestParsing.ParseOrUndefined<JobDependencyKind>(request.Kind),
                            caller.Context.OrganisationId,
                            Precondition(context),
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return await ConflictOrProblemForGarmentAsync(
                            result.Error, draftId, garmentId, caller.Context.OrganisationId, context, handler,
                            cancellationToken);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(OrderDraftGarmentPayload.From(result.Value.Garment, result.Value.Tag));
                })
            .Produces<OrderDraftGarmentPayload>(StatusCodes.Status200OK)
            .WithName("WithdrawOrderDraftDependency")
            .WithSummary("Withdraw a dependency one section declared on another.")
            .WithDescription(
                "A body rather than a route segment: the composite primary key makes the prerequisite-and-"
                + "kind pair the row's identity, and a route can carry only one identifier past the section.")
            .RequirePermission(OrdersPermissions.Intake, BranchScope.CurrentBranch)
            .ScopedToResource(OrdersResourceKinds.OrderDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(OrderDraftHandler.DependencyWithdrawnAction)
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

    /// <summary>
    /// <see cref="ConflictOrProblemAsync"/>, for a failure that names a garment section — re-reads the
    /// section's own tag rather than the draft's.
    /// </summary>
    private static async Task<IResult> ConflictOrProblemForGarmentAsync(
        Error error,
        Guid draftId,
        Guid garmentId,
        Guid organisationId,
        HttpContext context,
        OrderDraftHandler handler,
        CancellationToken cancellationToken)
    {
        if (error != OrdersErrors.ConcurrentChange)
        {
            return Problems.From(error, context);
        }

        var current = await handler.ReadGarmentAsync(draftId, garmentId, organisationId, cancellationToken);

        return current.IsFailure
            ? Problems.From(current.Error, context)
            : ConcurrencyResults.VersionConflict(context, error.Code, error.Message, current.Value.Tag);
    }
}
