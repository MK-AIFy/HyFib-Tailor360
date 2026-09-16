using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Orders.Api.Payloads;
using Tailor360.Modules.Orders.Application.Workflows;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Orders.Api.Workflows;

/// <summary>
/// The drafting surface over E06-F02-1's workflow model (#243): list and create a definition, read one
/// version, replace a draft version's whole graph, and start a new draft by cloning a published one.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every route here declares <see cref="BranchScope.Organisation"/>.</strong> A workflow
/// definition and its versions belong to the organisation and to no branch — the same reasoning
/// <c>CatalogVersionEndpoints</c> gives for the catalogue, whose closest analogue this class follows.
/// Each route naming a definition or a version records
/// <see cref="ResourceScopeEndpointExtensions.TouchesNoBranchOwnedResource{TBuilder}"/> rather than a
/// resource kind: ARCH-023 does not engage on an organisation-scoped route at all, but recording the
/// reason out loud is what keeps the matrix's blank <c>Resource</c> cell a decision rather than an
/// oversight.
/// </para>
/// <para>
/// <strong>Only the drafting key is used here.</strong> Every route demands
/// <see cref="CatalogPermissions.EditWorkflows"/> (<c>catalog.workflows.edit</c>); publishing and
/// retiring, gated on <see cref="CatalogPermissions.PublishWorkflows"/> with step-up, are E06-F02-3's.
/// A draft nobody can publish is harmless, which is why this slice can create and edit one and cannot
/// make it live.
/// </para>
/// <para>
/// <strong>The graph is checked whole on every <c>PUT</c>, and a refusal is not a save.</strong> See
/// <see cref="WorkflowDefinitionHandler.ReplaceGraphAsync"/>'s own remarks. When the submitted graph
/// carries an error-severity finding this returns a single document naming every one of them, keyed by
/// phase code, and nothing is written.
/// </para>
/// </remarks>
public static class WorkflowDefinitionEndpoints
{
    /// <summary>Why these routes name no branch-owned resource, and where that was reviewed.</summary>
    private const string OrganisationOwned =
        "A workflow definition and its versions belong to the organisation, not a branch.";

    private const string Review = "#243";

    /// <summary>Maps the workflow definition administration routes under the Orders group.</summary>
    /// <param name="orders">The Orders group.</param>
    /// <returns>The group.</returns>
    public static RouteGroupBuilder MapWorkflowDefinitionEndpoints(this RouteGroupBuilder orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        MapList(orders);
        MapCreateDefinition(orders);
        MapReadVersion(orders);
        MapReplaceGraph(orders);
        MapCreateVersion(orders);

        return orders;
    }

    private static void MapList(RouteGroupBuilder orders)
        => orders.MapGet(
                "/workflow-definitions",
                async Task<IResult> (
                    WorkflowDefinitionHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var definitions = await handler.ListAsync(caller.Context.OrganisationId, cancellationToken);

                    return Results.Ok(definitions
                        .Select(definition => WorkflowDefinitionPayload.From(definition, handler.EntityTagOf))
                        .ToArray());
                })
            .Produces<WorkflowDefinitionPayload[]>(StatusCodes.Status200OK)
            .WithName("ListWorkflowDefinitions")
            .WithSummary("List the organisation's workflow definitions, each with every version it has.")
            .WithDescription(
                "Whole, not paged: one definition per production process the shop runs, so the list is "
                + "small by construction. Each version is a summary carrying its own entity tag; reading "
                + "a version's phase graph is a separate call.")
            .RequirePermission(CatalogPermissions.EditWorkflows, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapCreateDefinition(RouteGroupBuilder orders)
        => orders.MapPost(
                "/workflow-definitions",
                async Task<IResult> (
                    CreateWorkflowDefinitionRequest request,
                    HttpContext context,
                    WorkflowDefinitionHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.CreateDefinitionAsync(
                        new CreateWorkflowDefinitionCommand(
                            caller.Context.OrganisationId,
                            request.Code,
                            request.Name,
                            request.Description,
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    var definition = result.Value.Definition;

                    return Results.Created(
                        $"/api/v1/orders/workflow-definitions/{definition.Id}",
                        WorkflowDefinitionPayload.From(definition, handler.EntityTagOf));
                })
            .Produces<WorkflowDefinitionPayload>(StatusCodes.Status201Created)
            .WithName("CreateWorkflowDefinition")
            .WithSummary("Create a new, empty workflow definition.")
            .WithDescription(
                "Starts the named process with no versions yet. A first draft version is a separate "
                + "call, so an administrator may name several processes before drafting any of their "
                + "graphs.")
            .RequirePermission(CatalogPermissions.EditWorkflows, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(WorkflowDefinitionHandler.DefinitionCreatedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static void MapReadVersion(RouteGroupBuilder orders)
        => orders.MapGet(
                "/workflow-definitions/{definitionId:guid}/versions/{versionId:guid}",
                async Task<IResult> (
                    Guid definitionId,
                    Guid versionId,
                    HttpContext context,
                    WorkflowDefinitionHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.ReadVersionAsync(
                        definitionId, versionId, caller.Context.OrganisationId, cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(WorkflowVersionPayload.From(result.Value.Version, result.Value.Tag));
                })
            .Produces<WorkflowVersionPayload>(StatusCodes.Status200OK)
            .WithName("GetWorkflowVersion")
            .WithSummary("Read one workflow version with its whole graph.")
            .WithDescription(
                "The same call whatever the version's status: a retired version reads exactly as it did, "
                + "which is what makes a job started against it render unchanged. The entity tag is what "
                + "an edit to this version must be made against.")
            .RequirePermission(CatalogPermissions.EditWorkflows, BranchScope.Organisation)
            .TouchesNoBranchOwnedResource(OrganisationOwned, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapReplaceGraph(RouteGroupBuilder orders)
        => orders.MapPut(
                "/workflow-definitions/{definitionId:guid}/versions/{versionId:guid}",
                async Task<IResult> (
                    Guid definitionId,
                    Guid versionId,
                    ReplaceWorkflowVersionGraphRequest request,
                    HttpContext context,
                    WorkflowDefinitionHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.ReplaceGraphAsync(
                        new ReplaceWorkflowVersionGraphCommand(
                            definitionId,
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            request.ToPhaseInputs(),
                            request.ToTransitionInputs(),
                            request.CategoryKeys ?? [],
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return await ConflictOrProblemAsync(
                            result.Error, definitionId, versionId, caller.Context.OrganisationId, context,
                            handler, cancellationToken);
                    }

                    var replacement = result.Value;

                    if (replacement.WasRefused)
                    {
                        return Problems.FromFindings(
                            OrdersErrors.WorkflowGraphInvalid,
                            [.. replacement.Findings.Select(WorkflowFindingPayload.From)],
                            context);
                    }

                    var administered = replacement.Version!;

                    context.Response.SetEntityTag(administered.Tag);

                    return Results.Ok(WorkflowVersionPayload.From(administered.Version, administered.Tag));
                })
            .Produces<WorkflowVersionPayload>(StatusCodes.Status200OK)
            .WithName("ReplaceWorkflowVersionGraph")
            .WithSummary("Replace a draft version's whole graph: its phases, its transitions and its category mapping.")
            .WithDescription(
                "One payload, not a dozen sub-resources: a phase list and a transition matrix are only "
                + "valid as a set, so all three are replaced together. Every one of the six graph checks "
                + "runs before the save — an unreachable phase, a phase with no permitted role, more or "
                + "fewer than one start phase, no terminal phase, a phase with no way out that is not "
                + "terminal, and a duplicate phase code — and if any of them fails, every finding comes "
                + "back in one document, keyed by phase code, and nothing is saved. Refused on a version "
                + "that is not a draft, naming its status, rather than silently doing nothing.")
            .RequirePermission(CatalogPermissions.EditWorkflows, BranchScope.Organisation)
            .TouchesNoBranchOwnedResource(OrganisationOwned, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(WorkflowDefinitionHandler.VersionEditedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static void MapCreateVersion(RouteGroupBuilder orders)
        => orders.MapPost(
                "/workflow-definitions/{definitionId:guid}/versions",
                async Task<IResult> (
                    Guid definitionId,
                    CreateWorkflowVersionRequest? request,
                    HttpContext context,
                    WorkflowDefinitionHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.CreateVersionAsync(
                        new CreateWorkflowVersionCommand(
                            definitionId,
                            caller.Context.OrganisationId,
                            request?.CloneFromVersionId,
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    var administered = result.Value;

                    context.Response.SetEntityTag(administered.Tag);

                    return Results.Created(
                        $"/api/v1/orders/workflow-definitions/{definitionId}/versions/{administered.Version.Id}",
                        WorkflowVersionPayload.From(administered.Version, administered.Tag));
                })
            .Produces<WorkflowVersionPayload>(StatusCodes.Status201Created)
            .WithName("CreateWorkflowVersion")
            .WithSummary("Start a new draft version, empty or cloned from an existing version of the same definition.")
            .WithDescription(
                "Cloning is the ordinary way to change a published version: a published version is "
                + "immutable, so a correction is a clone, an edit and a publication of the new draft. "
                + "The clone carries the source's phases, transitions and category mapping as fresh rows "
                + "of its own; the source must belong to this same definition.")
            .RequirePermission(CatalogPermissions.EditWorkflows, BranchScope.Organisation)
            .TouchesNoBranchOwnedResource(OrganisationOwned, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(WorkflowDefinitionHandler.VersionDraftedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    // RequireIfMatch rejects absent and malformed headers before the handler runs. The empty fallback
    // fails closed if the handler is ever invoked without that filter.
    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);

    /// <summary>
    /// Answers a failed edit, carrying the version's current tag when the failure was a stale
    /// <c>If-Match</c> — a status conflict such as "not a draft" is answered as an ordinary problem
    /// instead, because re-reading and offering a merge means nothing for a version nobody may edit at
    /// all.
    /// </summary>
    private static async Task<IResult> ConflictOrProblemAsync(
        Error error,
        Guid definitionId,
        Guid versionId,
        Guid organisationId,
        HttpContext context,
        WorkflowDefinitionHandler handler,
        CancellationToken cancellationToken)
    {
        if (error != OrdersErrors.ConcurrentChange)
        {
            return Problems.From(error, context);
        }

        var current = await handler.ReadVersionAsync(definitionId, versionId, organisationId, cancellationToken);

        return current.IsFailure
            ? Problems.From(current.Error, context)
            : ConcurrencyResults.VersionConflict(context, error.Code, error.Message, current.Value.Tag);
    }
}
