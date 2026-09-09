using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Catalog.Api.Payloads;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Catalog.Api.Catalogue;

/// <summary>
/// Administering the catalogue: drafting a version, editing its tree, previewing it, publishing it and
/// retiring it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every route here declares <see cref="BranchScope.Organisation"/>.</strong> A catalogue
/// version is organisation-wide configuration: one version is published for the whole business at a
/// time, and what is branch-specific is availability — a set of branches on a category, which is data
/// inside the version rather than a fact about who may edit it. Declaring
/// <see cref="BranchScope.CurrentBranch"/> would be a claim that a draft belongs to a branch, which
/// would be untrue and would let a Branch Manager's assignment decide what the organisation offers.
/// </para>
/// <para>
/// <strong>Editing and publishing are different permissions on purpose.</strong> Drafting is
/// <c>catalog.edit</c>; publishing and retiring are <c>catalog.publish</c>, which the catalogue flags
/// for a second factor, a fresh re-authentication and a stated reason. The line is where
/// <c>docs/prd/category-hierarchy.md</c> section 7 puts it: a draft is invisible to intake and costs
/// nothing to get wrong, and a published version is quoted, worked to and invoiced against.
/// </para>
/// <para>
/// <strong>A correction to a published version is a publish-level act.</strong> Section 7's table
/// gives every action on a published version to <c>catalog.publish</c>, and that includes the label
/// correction — even though a label is read by people and by nothing else, the row it changes is one
/// confirmed orders are pinned to.
/// </para>
/// </remarks>
public static class CatalogVersionEndpoints
{
    /// <summary>Where the reach and permission choices on these routes were reviewed.</summary>
    private const string Review = "#29, docs/prd/category-hierarchy.md sections 6 and 7";

    /// <summary>Maps the catalogue administration routes.</summary>
    /// <param name="catalog">The <c>/api/v1/catalog</c> group.</param>
    /// <returns>The same group, for chaining.</returns>
    public static RouteGroupBuilder MapCatalogVersionEndpoints(this RouteGroupBuilder catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        MapList(catalog);
        MapCreate(catalog);
        MapRead(catalog);
        MapValidate(catalog);
        MapCategories(catalog);
        MapServiceTypes(catalog);
        MapPresentation(catalog);
        MapPublish(catalog);
        MapRetire(catalog);

        return catalog;
    }

    private static void MapList(RouteGroupBuilder catalog)
        => catalog.MapGet("/versions", async Task<IResult> (
                CatalogHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var versions = await handler.ListAsync(
                    caller.Context.OrganisationId, cancellationToken);

                return Results.Ok(versions.Select(CatalogVersionSummaryPayload.From).ToArray());
            })
            .Produces<CatalogVersionSummaryPayload[]>(StatusCodes.Status200OK)
            .WithName("ListCatalogVersions")
            .WithSummary("List the organisation's catalogue versions, newest first.")
            .WithDescription(
                "Summaries only. Reading a version's tree is a separate call, because a list of "
                + "twenty versions carrying twenty trees is a page nobody needed.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapCreate(RouteGroupBuilder catalog)
        => catalog.MapPost("/versions", async Task<IResult> (
                CreateCatalogDraftRequest? request,
                HttpContext context,
                CatalogHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.CreateDraftAsync(
                    new CreateCatalogDraftCommand(
                        caller.Context.OrganisationId,
                        request?.Name ?? string.Empty,
                        request?.Notes,
                        request?.CloneFromVersionId,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Created(
                    $"/api/v1/catalog/versions/{result.Value.Version.Id}",
                    CatalogVersionPayload.From(result.Value.Version));
            })
            .Produces<CatalogVersionPayload>(StatusCodes.Status201Created)
            .WithName("CreateCatalogDraft")
            .WithSummary("Start a draft catalogue version, empty or cloned from an existing one.")
            .WithDescription(
                "Cloning the published version is the ordinary way to change a published catalogue: a "
                + "published version is immutable, so a correction is a clone, an edit and a second "
                + "publication. The clone carries the same categories and service types as concepts "
                + "with new rows of its own.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.DraftedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static void MapRead(RouteGroupBuilder catalog)
        => catalog.MapGet("/versions/{versionId:guid}", async Task<IResult> (
                Guid versionId,
                HttpContext context,
                CatalogHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(
                    versionId, caller.Context.OrganisationId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(CatalogVersionPayload.From(result.Value.Version));
            })
            .Produces<CatalogVersionPayload>(StatusCodes.Status200OK)
            .WithName("GetCatalogVersion")
            .WithSummary("Read one catalogue version and everything in it.")
            .WithDescription(
                "The administrator's preview, and the same call whatever the version's status: a "
                + "retired version reads exactly as it did, which is what makes a two-year-old job "
                + "card render. The ETag is the token a publication must be made against.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapValidate(RouteGroupBuilder catalog)
        => catalog.MapGet("/versions/{versionId:guid}/validation", async Task<IResult> (
                Guid versionId,
                HttpContext context,
                CatalogHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ValidateAsync(
                    versionId, caller.Context.OrganisationId, cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(CatalogValidationReportPayload.From(result.Value));
            })
            .Produces<CatalogValidationReportPayload>(StatusCodes.Status200OK)
            .WithName("ValidateCatalogVersion")
            .WithSummary("Run every registered validator against a version and report what they found.")
            .WithDescription(
                "A read: it changes nothing and is not audited. It runs the same code path publication "
                + "takes, deliberately — a preview that ran different checks from the command it "
                + "previews would be worse than no preview.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapCategories(RouteGroupBuilder catalog)
    {
        catalog.MapPost("/versions/{versionId:guid}/categories", async Task<IResult> (
                Guid versionId,
                CategoryRequest request,
                HttpContext context,
                CatalogHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.AddCategoryAsync(
                    new AddCategoryCommand(
                        versionId,
                        caller.Context.OrganisationId,
                        request.ParentCategoryId,
                        request.ToDetails(),
                        caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Created(
                        $"/api/v1/catalog/versions/{versionId}/categories/{result.Value.Id}",
                        CategoryPayload.From(result.Value, isGroupingNode: false));
            })
            .Produces<CategoryPayload>(StatusCodes.Status201Created)
            .WithName("AddCatalogCategory")
            .WithSummary("Add a category to a draft catalogue version.")
            .WithDescription(
                "This is the route that makes 'an administrator adds a category without a deployment' "
                + "true. Only a draft accepts it.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.CategoryAddedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPut("/versions/{versionId:guid}/categories/{categoryId:guid}", async Task<IResult> (
                Guid versionId,
                Guid categoryId,
                CategoryRequest request,
                HttpContext context,
                CatalogHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.EditCategoryAsync(
                    new EditCategoryCommand(
                        versionId,
                        caller.Context.OrganisationId,
                        categoryId,
                        request.ParentCategoryId,
                        request.ToDetails(),
                        request.Reason,
                        caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(CategoryPayload.From(result.Value, isGroupingNode: false));
            })
            .Produces<CategoryPayload>(StatusCodes.Status200OK)
            .WithName("EditCatalogCategory")
            .WithSummary("Replace what a draft says about a category.")
            .WithDescription(
                "Whole-value, not partial: the request carries everything an administrator says about "
                + "the category, so an omitted branch list means 'offered nowhere' rather than "
                + "'unchanged'. Re-parenting is refused when it would make the hierarchy circular.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.CategoryChangedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPost(
                "/versions/{versionId:guid}/categories/{categoryId:guid}/delete",
                async Task<IResult> (
                    Guid versionId,
                    Guid categoryId,
                    CatalogReasonRequest? request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.RemoveCategoryAsync(
                        new RemoveCategoryCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            categoryId,
                            request?.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure ? Problems.From(result.Error, context) : Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .WithName("RemoveCatalogCategory")
            .WithSummary("Remove a category, its sub-categories and their service types from a draft.")
            .WithDescription(
                "A POST sub-resource rather than a DELETE, following the pattern the administration "
                + "routes already use for a removal that carries a reason. Only from a draft: a "
                + "published version is immutable and a database trigger refuses the delete as well. "
                + "Descendants go with it, because leaving them would produce the orphaned parent "
                + "publication refuses.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.CategoryRemovedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapServiceTypes(RouteGroupBuilder catalog)
    {
        catalog.MapPost(
                "/versions/{versionId:guid}/categories/{categoryId:guid}/service-types",
                async Task<IResult> (
                    Guid versionId,
                    Guid categoryId,
                    ServiceTypeRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.AddServiceTypeAsync(
                        new AddServiceTypeCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            categoryId,
                            request.ToDetails(),
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Created(
                            $"/api/v1/catalog/versions/{versionId}/service-types/{result.Value.Id}",
                            ServiceTypePayload.From(result.Value));
                })
            .Produces<ServiceTypePayload>(StatusCodes.Status201Created)
            .WithName("AddCatalogServiceType")
            .WithSummary("Add a service type to a category in a draft.")
            .WithDescription(
                "The five links may all be null here. Whether that is acceptable is decided at "
                + "publication, where a missing link is an error unless the administrator accepted it "
                + "with allowIncomplete, which flags the service not orderable.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.ServiceTypeAddedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPut(
                "/versions/{versionId:guid}/service-types/{serviceTypeId:guid}",
                async Task<IResult> (
                    Guid versionId,
                    Guid serviceTypeId,
                    ServiceTypeRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.EditServiceTypeAsync(
                        new EditServiceTypeCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            serviceTypeId,
                            request.ToDetails(),
                            request.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Ok(ServiceTypePayload.From(result.Value));
                })
            .Produces<ServiceTypePayload>(StatusCodes.Status200OK)
            .WithName("EditCatalogServiceType")
            .WithSummary("Replace what a draft says about a service type, its five links included.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.ServiceTypeChangedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPost(
                "/versions/{versionId:guid}/service-types/{serviceTypeId:guid}/delete",
                async Task<IResult> (
                    Guid versionId,
                    Guid serviceTypeId,
                    CatalogReasonRequest? request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.RemoveServiceTypeAsync(
                        new RemoveServiceTypeCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            serviceTypeId,
                            request?.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .WithName("RemoveCatalogServiceType")
            .WithSummary("Remove a service type from a draft.")
            .RequirePermission(CatalogPermissions.Edit, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.ServiceTypeRemovedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapPresentation(RouteGroupBuilder catalog)
    {
        catalog.MapPost(
                "/versions/{versionId:guid}/categories/{categoryId:guid}/presentation",
                async Task<IResult> (
                    Guid versionId,
                    Guid categoryId,
                    CatalogPresentationRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                    await CorrectAsync(
                        handler, caller, context, versionId, categoryId, null, request, cancellationToken))
            .Produces<CatalogVersionPayload>(StatusCodes.Status200OK)
            .WithName("CorrectCatalogCategoryPresentation")
            .WithSummary("Correct the label, Tamil label, description or display order of a published category.")
            .WithDescription(
                "The only edit a published version admits. Nothing downstream reads a label — a price "
                + "list, a report, an export and an event payload all refer to the code — so a "
                + "correction changes what is shown and nothing that was priced, worked to or "
                + "reported. A reason is required and the change is audited.")
            .RequirePermission(CatalogPermissions.Publish, BranchScope.Organisation)
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.LabelCorrectedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapPost(
                "/versions/{versionId:guid}/service-types/{serviceTypeId:guid}/presentation",
                async Task<IResult> (
                    Guid versionId,
                    Guid serviceTypeId,
                    CatalogPresentationRequest request,
                    HttpContext context,
                    CatalogHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                    await CorrectAsync(
                        handler, caller, context, versionId, null, serviceTypeId, request, cancellationToken))
            .Produces<CatalogVersionPayload>(StatusCodes.Status200OK)
            .WithName("CorrectCatalogServiceTypePresentation")
            .WithSummary("Correct the presentation fields of a published service type.")
            .RequirePermission(CatalogPermissions.Publish, BranchScope.Organisation)
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.LabelCorrectedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapPublish(RouteGroupBuilder catalog)
        => catalog.MapPost("/versions/{versionId:guid}/publish", async Task<IResult> (
                Guid versionId,
                CatalogReasonRequest? request,
                HttpContext context,
                CatalogHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                context.Request.TryGetIfMatch(out var expected);

                var result = await handler.PublishAsync(
                    new PublishCatalogVersionCommand(
                        versionId,
                        caller.Context.OrganisationId,
                        request?.Reason ?? string.Empty,
                        expected.Version is { Length: > 0 } ? expected : null,
                        caller.UserId),
                    cancellationToken);

                if (result.IsSuccess)
                {
                    return Results.Ok(CatalogPublicationPayload.From(result.Value));
                }

                if (result.Error.Code != "catalog.publish-validation-failed")
                {
                    return Problems.From(result.Error, context);
                }

                // The refusal is worth more than its code: every reason at once, each with the path a
                // screen can focus, so the administrator fixes the whole draft rather than one
                // reference per round trip.
                var report = await handler.ValidateAsync(
                    versionId, caller.Context.OrganisationId, cancellationToken);

                return Problems.FromFindings(
                    result.Error,
                    report.IsSuccess ? CatalogFindingPayload.From(report.Value.Findings) : [],
                    context);
            })
            .Produces<CatalogPublicationPayload>(StatusCodes.Status200OK)
            .WithName("PublishCatalogVersion")
            .WithSummary("Publish a draft, superseding whatever was published before it.")
            .WithDescription(
                "Exactly one version is published at a time, so publishing retires the one it "
                + "replaces in the same transaction — and the retirement validators are asked about "
                + "the outgoing version with this one named as its successor. A draft that dropped a "
                + "category work in progress still needs is therefore refused here, where somebody "
                + "can still do something about it. Two administrators publishing different drafts at "
                + "once is settled by the database: one commits and the other is told plainly.")
            .RequirePermission(CatalogPermissions.Publish, BranchScope.Organisation)
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.PublishedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static void MapRetire(RouteGroupBuilder catalog)
        => catalog.MapPost("/versions/{versionId:guid}/retire", async Task<IResult> (
                Guid versionId,
                CatalogReasonRequest? request,
                HttpContext context,
                CatalogHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.RetireAsync(
                    new RetireCatalogVersionCommand(
                        versionId,
                        caller.Context.OrganisationId,
                        request?.Reason ?? string.Empty,
                        caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(CatalogVersionSummaryPayload.From(result.Value.Version));
            })
            .Produces<CatalogVersionSummaryPayload>(StatusCodes.Status200OK)
            .WithName("RetireCatalogVersion")
            .WithSummary("Retire the published version, so nothing new is taken against it.")
            .WithDescription(
                "Retirement stops new orders and nothing else: jobs already in production run to "
                + "dispatch on the configuration they were pinned to, and the retired version stays "
                + "fully readable so their job cards, invoices and reports render exactly as they "
                + "did. Refused while work in progress needs a service no successor replaces.")
            .RequirePermission(CatalogPermissions.Publish, BranchScope.Organisation)
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CatalogHandler.RetiredAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static async Task<IResult> CorrectAsync(
        CatalogHandler handler,
        ICurrentUser caller,
        HttpContext context,
        Guid versionId,
        Guid? categoryId,
        Guid? serviceTypeId,
        CatalogPresentationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.CorrectPresentationAsync(
            new CorrectCatalogPresentationCommand(
                versionId,
                caller.Context.OrganisationId,
                categoryId,
                serviceTypeId,
                request.ToPresentation(),
                request.Reason ?? string.Empty,
                caller.UserId),
            cancellationToken);

        if (result.IsFailure)
        {
            return Problems.From(result.Error, context);
        }

        context.Response.SetEntityTag(result.Value.Tag);

        return Results.Ok(CatalogVersionPayload.From(result.Value.Version));
    }
}
