using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Pricing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Pricing;

/// <summary>
/// The price-list routes (#41, #146): lists, their versions, the items and discount rules of a
/// version, and publication. The same shape as the tax configuration routes.
/// </summary>
internal static class PriceListEndpoints
{
    public static RouteGroupBuilder MapPriceListEndpoints(this RouteGroupBuilder billing)
    {
        MapLists(billing);
        MapVersions(billing);
        MapItems(billing);
        MapDiscountRules(billing);
        MapPublish(billing);

        return billing;
    }

    private static void MapLists(RouteGroupBuilder billing)
    {
        billing.MapGet("/price-lists", async Task<IResult> (
                IPriceListStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var lists = await store.ListListsAsync(caller.Context.OrganisationId, cancellationToken);

                return Results.Ok(lists.Select(PriceListPayload.From).ToArray());
            })
            .Produces<PriceListPayload[]>(StatusCodes.Status200OK)
            .WithName("ListPriceLists")
            .WithSummary("List the organisation's price lists, by code.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPost("/price-lists", async Task<IResult> (
                CreatePriceListRequest request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.CreateListAsync(
                    new CreatePriceListCommand(
                        caller.Context.OrganisationId, request.Code ?? string.Empty, request.Name ?? string.Empty, request.Reason, caller.UserId),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Created($"/api/v1/billing/price-lists/{result.Value.List.Id}", PriceListPayload.From(result.Value.List));
            })
            .Produces<PriceListPayload>(StatusCodes.Status201Created)
            .WithName("CreatePriceList")
            .WithSummary("Create a price list. Most shops have one; a shop pricing branches differently has one per group of branches.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.ListCreatedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapGet("/price-lists/{priceListId:guid}", async Task<IResult> (
                Guid priceListId,
                HttpContext context,
                IPriceListStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var list = await store.FindListAsync(priceListId, caller.Context.OrganisationId, cancellationToken);
                if (list is null)
                {
                    return Problems.From(Domain.BillingErrors.PriceListNotFound, context);
                }

                context.Response.SetEntityTag(store.EntityTagOf(list));

                return Results.Ok(PriceListPayload.From(list));
            })
            .Produces<PriceListPayload>(StatusCodes.Status200OK)
            .WithName("GetPriceList")
            .WithSummary("Read one price list. The ETag is the token a rename is made against.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPut("/price-lists/{priceListId:guid}", async Task<IResult> (
                Guid priceListId,
                RenamePriceListRequest request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.RenameListAsync(
                    new RenamePriceListCommand(
                        priceListId, caller.Context.OrganisationId, Precondition(context), request.Name ?? string.Empty, request.Reason, caller.UserId),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(PriceListPayload.From(result.Value.List));
            })
            .Produces<PriceListPayload>(StatusCodes.Status200OK)
            .WithName("RenamePriceList")
            .WithSummary("Rename a price list. Its code never changes: seeds and exports refer to it.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.ListRenamedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapVersions(RouteGroupBuilder billing)
    {
        billing.MapGet("/price-lists/{priceListId:guid}/versions", async Task<IResult> (
                Guid priceListId,
                HttpContext context,
                IPriceListStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (await store.FindListAsync(priceListId, caller.Context.OrganisationId, cancellationToken) is null)
                {
                    return Problems.From(Domain.BillingErrors.PriceListNotFound, context);
                }

                var versions = await store.ListVersionsAsync(priceListId, caller.Context.OrganisationId, cancellationToken);

                return Results.Ok(versions.Select(PriceListVersionSummaryPayload.From).ToArray());
            })
            .Produces<PriceListVersionSummaryPayload[]>(StatusCodes.Status200OK)
            .WithName("ListPriceListVersions")
            .WithSummary("List a price list's versions, newest first. Summaries only.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPost("/price-lists/{priceListId:guid}/versions", async Task<IResult> (
                Guid priceListId,
                PriceListVersionRequest request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (!request.SaysTaxInclusive)
                {
                    // A money-bearing flag is never defaulted: an omitted one is refused, as every other field is.
                    return Problems.From(Domain.BillingErrors.Required("taxInclusive"), context);
                }

                var result = await handler.CreateDraftAsync(
                    new CreatePriceListDraftCommand(
                        priceListId, caller.Context.OrganisationId, request.ToDetails(), request.CloneFromVersionId, request.Reason, caller.UserId),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Created(
                    $"/api/v1/billing/price-lists/versions/{result.Value.Version.Id}",
                    PriceListVersionPayload.From(result.Value.Version));
            })
            .Produces<PriceListVersionPayload>(StatusCodes.Status201Created)
            .WithName("CreatePriceListDraft")
            .WithSummary("Start a draft price-list version, empty or cloned from an existing version of the same list.")
            .WithDescription(
                "Cloning the published version is the ordinary way to change a rate: a published version is "
                + "immutable, so a change is a clone, an edit and a second publication. The clone carries the same "
                + "items and rules as concepts with new rows of its own.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.DraftedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapGet("/price-lists/versions/{versionId:guid}", async Task<IResult> (
                Guid versionId,
                HttpContext context,
                IPriceListStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var version = await store.FindVersionAsync(versionId, caller.Context.OrganisationId, cancellationToken);
                if (version is null)
                {
                    return Problems.From(Domain.BillingErrors.VersionNotFound, context);
                }

                context.Response.SetEntityTag(store.EntityTagOf(version));

                return Results.Ok(PriceListVersionPayload.From(version));
            })
            .Produces<PriceListVersionPayload>(StatusCodes.Status200OK)
            .WithName("GetPriceListVersion")
            .WithSummary("Read one price-list version and everything in it, whatever its status.")
            .WithDescription("The ETag is the token every change to the version is made against.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPut("/price-lists/versions/{versionId:guid}", async Task<IResult> (
                Guid versionId,
                PriceListVersionRequest request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (!request.SaysTaxInclusive)
                {
                    // A money-bearing flag is never defaulted: an omitted one is refused, as every other field is.
                    return Problems.From(Domain.BillingErrors.Required("taxInclusive"), context);
                }

                var result = await handler.DescribeAsync(
                    new DescribePriceListVersionCommand(
                        versionId, caller.Context.OrganisationId, Precondition(context), request.ToDetails(), request.Reason, caller.UserId),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(PriceListVersionPayload.From(result.Value.Version));
            })
            .Produces<PriceListVersionPayload>(StatusCodes.Status200OK)
            .WithName("DescribePriceListVersion")
            .WithSummary("Change a draft's name, notes, effective date, conventions and branches. Whole-value.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.ChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapGet("/price-lists/versions/{versionId:guid}/validation", async Task<IResult> (
                Guid versionId,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ValidateAsync(versionId, caller.Context.OrganisationId, cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(BillingValidationReportPayload.From(result.Value));
            })
            .Produces<BillingValidationReportPayload>(StatusCodes.Status200OK)
            .WithName("ValidatePriceListVersion")
            .WithSummary("Run the publication checks against a version and report what they found.")
            .WithDescription(
                "An item naming a tax code nobody published, a branch another list already prices, a code "
                + "re-spelled after the catalogue referred to it. They encode no rate.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);
    }

    private static void MapItems(RouteGroupBuilder billing)
    {
        billing.MapPost("/price-lists/versions/{versionId:guid}/items", async Task<IResult> (
                Guid versionId,
                PriceListItemRequest request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (!request.SaysActive)
                {
                    // A money-bearing flag is never defaulted: an omitted one is refused, as every other field is.
                    return Problems.From(Domain.BillingErrors.Required("active"), context);
                }

                var result = await handler.AddItemAsync(
                    new AddPriceListItemCommand(
                        versionId, caller.Context.OrganisationId, Precondition(context), request.ToDetails(), request.Reason, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                // The version's row moved with its child: the tag the caller sent is stale, and the one on
                // this response is what the next write must carry.
                context.Response.SetEntityTag(result.Value.VersionTag);

                return Results.Created(
                    $"/api/v1/billing/price-lists/versions/{versionId}/items/{result.Value.Item.Id}",
                    PriceListItemPayload.From(result.Value.Item));
            })
            .Produces<PriceListItemPayload>(StatusCodes.Status201Created)
            .WithName("AddPriceListItem")
            .WithSummary("Add an item — a service's base charge, a surcharge or a material — to a draft.")
            .WithDescription(
                "The item's code is what the catalogue's service types and design options name; its tax code is "
                + "one of the published tax configuration's, checked at publication so a half-entered list can be saved.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.ItemAddedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPut("/price-lists/versions/{versionId:guid}/items/{itemId:guid}", async Task<IResult> (
                Guid versionId,
                Guid itemId,
                PriceListItemRequest request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (!request.SaysActive)
                {
                    // A money-bearing flag is never defaulted: an omitted one is refused, as every other field is.
                    return Problems.From(Domain.BillingErrors.Required("active"), context);
                }

                var result = await handler.EditItemAsync(
                    new EditPriceListItemCommand(
                        versionId, caller.Context.OrganisationId, Precondition(context), itemId, request.ToDetails(), request.Reason, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.VersionTag);

                return Results.Ok(PriceListItemPayload.From(result.Value.Item));
            })
            .Produces<PriceListItemPayload>(StatusCodes.Status200OK)
            .WithName("EditPriceListItem")
            .WithSummary("Replace what a draft says about an item, whole-value.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.ItemChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPost("/price-lists/versions/{versionId:guid}/items/{itemId:guid}/delete", async Task<IResult> (
                Guid versionId,
                Guid itemId,
                BillingReasonRequest? request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.RemoveItemAsync(
                    new RemovePriceListItemCommand(
                        versionId, caller.Context.OrganisationId, Precondition(context), itemId, request?.Reason, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithName("RemovePriceListItem")
            .WithSummary("Remove an item from a draft.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.ItemRemovedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapDiscountRules(RouteGroupBuilder billing)
    {
        billing.MapPost("/price-lists/versions/{versionId:guid}/discount-rules", async Task<IResult> (
                Guid versionId,
                DiscountRuleRequest request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (!request.SaysActive)
                {
                    // A money-bearing flag is never defaulted: an omitted one is refused, as every other field is.
                    return Problems.From(Domain.BillingErrors.Required("active"), context);
                }

                var result = await handler.AddDiscountRuleAsync(
                    new AddDiscountRuleCommand(
                        versionId, caller.Context.OrganisationId, Precondition(context), request.ToDetails(), request.Reason, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.VersionTag);

                return Results.Created(
                    $"/api/v1/billing/price-lists/versions/{versionId}/discount-rules/{result.Value.Rule.Id}",
                    DiscountRulePayload.From(result.Value.Rule));
            })
            .Produces<DiscountRulePayload>(StatusCodes.Status201Created)
            .WithName("AddDiscountRule")
            .WithSummary("Add a discount rule — what kind, how much on the counter's own authority, how much with approval — to a draft.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.RuleAddedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPut("/price-lists/versions/{versionId:guid}/discount-rules/{ruleId:guid}", async Task<IResult> (
                Guid versionId,
                Guid ruleId,
                DiscountRuleRequest request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (!request.SaysActive)
                {
                    // A money-bearing flag is never defaulted: an omitted one is refused, as every other field is.
                    return Problems.From(Domain.BillingErrors.Required("active"), context);
                }

                var result = await handler.EditDiscountRuleAsync(
                    new EditDiscountRuleCommand(
                        versionId, caller.Context.OrganisationId, Precondition(context), ruleId, request.ToDetails(), request.Reason, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.VersionTag);

                return Results.Ok(DiscountRulePayload.From(result.Value.Rule));
            })
            .Produces<DiscountRulePayload>(StatusCodes.Status200OK)
            .WithName("EditDiscountRule")
            .WithSummary("Replace what a draft says about a discount rule, whole-value.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.RuleChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPost("/price-lists/versions/{versionId:guid}/discount-rules/{ruleId:guid}/delete", async Task<IResult> (
                Guid versionId,
                Guid ruleId,
                BillingReasonRequest? request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.RemoveDiscountRuleAsync(
                    new RemoveDiscountRuleCommand(
                        versionId, caller.Context.OrganisationId, Precondition(context), ruleId, request?.Reason, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithName("RemoveDiscountRule")
            .WithSummary("Remove a discount rule from a draft.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.RuleRemovedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapPublish(RouteGroupBuilder billing)
        => billing.MapPost("/price-lists/versions/{versionId:guid}/publish", async Task<IResult> (
                Guid versionId,
                BillingReasonRequest? request,
                HttpContext context,
                PriceListHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.PublishAsync(
                    new PublishPriceListVersionCommand(
                        versionId, caller.Context.OrganisationId, Precondition(context), request?.Reason ?? string.Empty, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    if (result.Error.Code == Domain.BillingErrors.PublishValidationFailed.Code)
                    {
                        var report = await handler.ValidateAsync(versionId, caller.Context.OrganisationId, cancellationToken);
                        if (report.IsSuccess)
                        {
                            return Problems.FromFindings(
                                result.Error, [.. report.Value.Findings.Select(BillingFindingPayload.From)], context);
                        }
                    }

                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Published.Tag);

                return Results.Ok(new PriceListPublicationPayload(
                    PriceListVersionPayload.From(result.Value.Published.Version),
                    result.Value.SupersededVersionId,
                    [.. result.Value.Findings.Select(BillingFindingPayload.From)]));
            })
            .Produces<PriceListPublicationPayload>(StatusCodes.Status200OK)
            .WithName("PublishPriceListVersion")
            .WithSummary("Publish a draft, retiring the list's published version in the same transaction.")
            .WithDescription(
                "Refused while a publication check fails, with every finding in the problem detail. Publication "
                + "changes what every future order is quoted at, so it demands a fresh second factor and a reason.")
            .RequirePermission(BillingPermissions.PublishPriceList, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PriceListHandler.PublishedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .RequireStepUp()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);
}
