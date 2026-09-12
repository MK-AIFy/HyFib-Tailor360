using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Catalog.Api.Payloads;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Catalog.Api.Catalogue;

/// <summary>
/// The design catalogue's administration routes: groups, options and rules on a draft version, and
/// their words on a published one (#30, issue #137).
/// </summary>
/// <remarks>
/// The same shape as the category and service-type routes, on purpose: every change is made against
/// the version's <c>ETag</c>, carries a retry key, is audited, and is refused on a published version
/// but for the presentation corrections, which demand a reason and the publish permission. A read of
/// the groups and rules is the version read itself — they travel in <c>CatalogVersionPayload</c>,
/// because an administrator never looks at a group without the category it belongs to.
/// </remarks>
public static class CatalogDesignEndpoints
{
    /// <summary>Maps the design routes under the catalogue group.</summary>
    /// <param name="catalog">The catalogue group.</param>
    /// <returns>The group.</returns>
    public static RouteGroupBuilder MapCatalogDesignEndpoints(this RouteGroupBuilder catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        MapGroups(catalog);
        MapOptions(catalog);
        MapRules(catalog);
        MapPresentation(catalog);

        return catalog;
    }

    private static void MapGroups(RouteGroupBuilder catalog)
    {
        catalog.MapPost(
                "/versions/{versionId:guid}/categories/{categoryId:guid}/design-groups",
                async Task<IResult> (
                    Guid versionId,
                    Guid categoryId,
                    DesignGroupRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.AddDesignGroupAsync(
                        new AddDesignGroupCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            categoryId,
                            request.ToDetails(),
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Created(
                            $"/api/v1/catalog/versions/{versionId}/design-groups/{result.Value.Id}",
                            DesignGroupPayload.From(result.Value));
                })
            .Produces<DesignGroupPayload>(StatusCodes.Status201Created)
            .WithName("AddCatalogDesignGroup")
            .WithSummary("Add a design option group to a category in a draft.")
            .WithDescription(
                "A group belongs to one category of one version; a service type of that category "
                + "offers it by naming it. The code is fixed once the version is published, because "
                + "rules, snapshots and exports refer to it.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DesignGroupAddedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPut(
                "/versions/{versionId:guid}/design-groups/{designOptionGroupId:guid}",
                async Task<IResult> (
                    Guid versionId,
                    Guid designOptionGroupId,
                    DesignGroupRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.EditDesignGroupAsync(
                        new EditDesignGroupCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            designOptionGroupId,
                            request.ToDetails(),
                            request.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Ok(DesignGroupPayload.From(result.Value));
                })
            .Produces<DesignGroupPayload>(StatusCodes.Status200OK)
            .WithName("EditCatalogDesignGroup")
            .WithSummary("Replace what a draft says about a design option group.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DesignGroupChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPost(
                "/versions/{versionId:guid}/design-groups/{designOptionGroupId:guid}/delete",
                async Task<IResult> (
                    Guid versionId,
                    Guid designOptionGroupId,
                    CatalogReasonRequest? request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.RemoveDesignGroupAsync(
                        new RemoveDesignGroupCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            designOptionGroupId,
                            request?.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure ? Problems.From(result.Error, context) : Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .WithName("RemoveCatalogDesignGroup")
            .WithSummary("Remove a design option group, its options and the rules that read it from a draft.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DesignGroupRemovedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapOptions(RouteGroupBuilder catalog)
    {
        catalog.MapPost(
                "/versions/{versionId:guid}/design-groups/{designOptionGroupId:guid}/options",
                async Task<IResult> (
                    Guid versionId,
                    Guid designOptionGroupId,
                    DesignOptionRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.AddDesignOptionAsync(
                        new AddDesignOptionCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            designOptionGroupId,
                            request.ToDetails(),
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Created(
                            $"/api/v1/catalog/versions/{versionId}/design-options/{result.Value.Id}",
                            DesignOptionPayload.From(result.Value));
                })
            .Produces<DesignOptionPayload>(StatusCodes.Status201Created)
            .WithName("AddCatalogDesignOption")
            .WithSummary("Add an option to a design option group in a draft.")
            .WithDescription(
                "Every option carries help text and alternative text, because the picker is a picture "
                + "first and the job card is read in monochrome. NONE is the reserved code for 'the "
                + "customer chose not to have this' and is selectable in every group.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DesignOptionAddedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPut(
                "/versions/{versionId:guid}/design-options/{designOptionId:guid}",
                async Task<IResult> (
                    Guid versionId,
                    Guid designOptionId,
                    DesignOptionRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.EditDesignOptionAsync(
                        new EditDesignOptionCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            designOptionId,
                            request.ToDetails(),
                            request.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Ok(DesignOptionPayload.From(result.Value));
                })
            .Produces<DesignOptionPayload>(StatusCodes.Status200OK)
            .WithName("EditCatalogDesignOption")
            .WithSummary("Replace what a draft says about a design option.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DesignOptionChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPost(
                "/versions/{versionId:guid}/design-options/{designOptionId:guid}/delete",
                async Task<IResult> (
                    Guid versionId,
                    Guid designOptionId,
                    CatalogReasonRequest? request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.RemoveDesignOptionAsync(
                        new RemoveDesignOptionCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            designOptionId,
                            request?.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure ? Problems.From(result.Error, context) : Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .WithName("RemoveCatalogDesignOption")
            .WithSummary("Remove a design option from a draft.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DesignOptionRemovedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapRules(RouteGroupBuilder catalog)
    {
        catalog.MapPost(
                "/versions/{versionId:guid}/categories/{categoryId:guid}/design-rules",
                async Task<IResult> (
                    Guid versionId,
                    Guid categoryId,
                    DesignRuleRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.AddDesignRuleAsync(
                        new AddDesignRuleCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            categoryId,
                            request.ToDetails(),
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Created(
                            $"/api/v1/catalog/versions/{versionId}/design-rules/{result.Value.Id}",
                            DesignRulePayload.From(result.Value));
                })
            .Produces<DesignRulePayload>(StatusCodes.Status201Created)
            .WithName("AddCatalogDesignRule")
            .WithSummary("Add a requires, excludes, requires-attachment or note rule to a category in a draft.")
            .WithDescription(
                "The rule's DR-nn number is allocated by the catalogue and never re-used. Its operands "
                + "read groups of its own category; whether the options it names exist, and whether it "
                + "agrees with the other rules, is checked at publication, where every finding arrives "
                + "at once.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DesignRuleAddedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPut(
                "/versions/{versionId:guid}/design-rules/{designRuleId:guid}",
                async Task<IResult> (
                    Guid versionId,
                    Guid designRuleId,
                    DesignRuleRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.EditDesignRuleAsync(
                        new EditDesignRuleCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            designRuleId,
                            request.ToDetails(),
                            request.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Ok(DesignRulePayload.From(result.Value));
                })
            .Produces<DesignRulePayload>(StatusCodes.Status200OK)
            .WithName("EditCatalogDesignRule")
            .WithSummary("Replace what a draft says about a design rule. Its number and category never change.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DesignRuleChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPost(
                "/versions/{versionId:guid}/design-rules/{designRuleId:guid}/delete",
                async Task<IResult> (
                    Guid versionId,
                    Guid designRuleId,
                    CatalogReasonRequest? request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.RemoveDesignRuleAsync(
                        new RemoveDesignRuleCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            designRuleId,
                            request?.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure ? Problems.From(result.Error, context) : Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .WithName("RemoveCatalogDesignRule")
            .WithSummary("Remove a design rule from a draft. Its number is retired with it.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DesignRuleRemovedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapPresentation(RouteGroupBuilder catalog)
    {
        catalog.MapPost(
                "/versions/{versionId:guid}/design-groups/{designOptionGroupId:guid}/presentation",
                async Task<IResult> (
                    Guid versionId,
                    Guid designOptionGroupId,
                    DesignGroupPresentationRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.CorrectDesignPresentationAsync(
                        new CorrectDesignPresentationCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            designOptionGroupId,
                            null,
                            request.ToPresentation(),
                            null,
                            request.Reason ?? string.Empty,
                            caller.UserId),
                        cancellationToken);

                    return Corrected(result, context);
                })
            .Produces<CatalogVersionPayload>(StatusCodes.Status200OK)
            .WithName("CorrectCatalogDesignGroupPresentation")
            .WithSummary("Correct the label, Tamil label or display order of a published design group.")
            .WithDescription(
                "The one edit a published group admits. Nothing downstream reads a label, so the "
                + "correction changes what is shown and nothing a confirmed garment is pinned to. A "
                + "reason is required and the change is audited.")
            .RequirePermission(CatalogPermissions.Publish, BranchScope.Organisation)
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.LabelCorrectedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPost(
                "/versions/{versionId:guid}/design-options/{designOptionId:guid}/presentation",
                async Task<IResult> (
                    Guid versionId,
                    Guid designOptionId,
                    DesignOptionPresentationRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.CorrectDesignPresentationAsync(
                        new CorrectDesignPresentationCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            null,
                            designOptionId,
                            null,
                            request.ToPresentation(),
                            request.Reason ?? string.Empty,
                            caller.UserId),
                        cancellationToken);

                    return Corrected(result, context);
                })
            .Produces<CatalogVersionPayload>(StatusCodes.Status200OK)
            .WithName("CorrectCatalogDesignOptionPresentation")
            .WithSummary("Correct the label, Tamil label, help text, alternative text or display order of a published design option.")
            .WithDescription(
                "The help text and the alternative text are words for people, like the label; the "
                + "illustration is not correctable here, because the drawing the customer was shown "
                + "is part of what they agreed to.")
            .RequirePermission(CatalogPermissions.Publish, BranchScope.Organisation)
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.LabelCorrectedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static IResult Corrected(
        Platform.Abstractions.Results.Result<AdministeredCatalogVersion> result,
        HttpContext context)
    {
        if (result.IsFailure)
        {
            return Problems.From(result.Error, context);
        }

        context.Response.SetEntityTag(result.Value.Tag);
        return Results.Ok(CatalogVersionPayload.From(result.Value.Version));
    }

    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);
}
