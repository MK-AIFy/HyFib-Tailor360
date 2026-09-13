using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Catalog.Api.Payloads;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Catalog.Api.Catalogue;

/// <summary>
/// The design picker's own routes: what a service type offers today, the drafts Reception builds against
/// it, and the migration a republish may leave standing (#30, issue #140).
/// </summary>
/// <remarks>
/// <para>
/// A sibling of <see cref="CatalogDesignEndpoints"/> rather than an addition to it: that file is
/// administration, gated on <c>catalog.edit</c> and <c>catalog.publish</c>, and reads and writes a
/// version's tree directly; this one is the counter's own surface, gated on <c>catalog.design.select</c>
/// — the grant <c>orders.intake</c>'s holders carry (<c>docs/prd/state-transitions.md</c> line 124) —
/// and never touches a version at all. It reads the currently published one and writes only drafts.
/// </para>
/// <para>
/// <strong>Every draft route is branch-scoped and resource-scoped to the draft.</strong> A draft is
/// shared within the branch that started it, the same way a measurement draft is; another branch's
/// draft reads and writes as not-found, through <c>DesignSelectionDraftScopeResolver</c>.
/// </para>
/// </remarks>
public static class CatalogDesignSelectionEndpoints
{
    /// <summary>A session working in no branch cannot be told what its branch may choose from.</summary>
    private static readonly Error NoBranch = Error.Validation(
        "catalog.branch-required",
        "This session is not working in a branch, and what may be chosen is a different list at each "
        + "branch. Choose a branch and ask again.",
        "branch");

    /// <summary>Maps the design picker and draft routes under the catalogue group.</summary>
    /// <param name="catalog">The catalogue group.</param>
    /// <returns>The group.</returns>
    public static RouteGroupBuilder MapCatalogDesignSelectionEndpoints(this RouteGroupBuilder catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        catalog.MapGet(
                "/current/service-types/{serviceTypeId:guid}/design",
                async Task<IResult> (
                    Guid serviceTypeId,
                    HttpContext context,
                    DesignSelectionDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    if (caller.Context.BranchId is not { } branchId)
                    {
                        return Problems.From(NoBranch, context);
                    }

                    var result = await handler.ReadPickerAsync(
                        serviceTypeId, caller.Context.OrganisationId, branchId, cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Ok(DesignPickerPayload.From(result.Value));
                })
            .Produces<DesignPickerPayload>(StatusCodes.Status200OK)
            .WithName("GetCatalogDesignPicker")
            .WithSummary("What a service type offers for its design, at the caller's branch, today.")
            .WithDescription(
                "The service type's groups in display order, each option with its label, help text, "
                + "illustration reference and alt text, its price-list item code and day impact, and the "
                + "rules in a client-evaluable form — only what is offerable at this branch today. "
                + "Nothing about any customer.")
            .RequirePermission(CatalogPermissions.DesignSelect, BranchScope.CurrentBranch)
            .TouchesNoBranchOwnedResource(
                "The service type named in the route is a row of the currently published catalogue "
                + "version, organisation-wide configuration rather than a branch-owned resource — the "
                + "same reasoning CatalogModuleServiceCollectionExtensions gives for the administration "
                + "routes. Branch availability is enforced inside the handler, through the same "
                + "ICatalogAvailabilityQuery.IsOrderableAsync predicate /current itself answers from, "
                + "and a service type not offered here reads as not found rather than as forbidden.",
                "#140")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        catalog.MapPost(
                "/design-drafts",
                async Task<IResult> (
                    StartDesignSelectionDraftRequest request,
                    HttpContext context,
                    DesignSelectionDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    if (caller.Context.BranchId is not { } branchId)
                    {
                        return Problems.From(NoBranch, context);
                    }

                    var result = await handler.StartAsync(
                        new StartDesignSelectionDraftCommand(
                            request.ServiceTypeId, caller.Context.OrganisationId, branchId, caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Created(
                        $"/api/v1/catalog/design-drafts/{result.Value.Draft.Id}",
                        DesignSelectionDraftPayload.From(result.Value.Draft, null));
                })
            .Produces<DesignSelectionDraftPayload>(StatusCodes.Status201Created)
            .WithName("StartCatalogDesignSelectionDraft")
            .WithSummary("Start choosing a design for a service type of the currently published version.")
            .WithDescription(
                "Pinned to the currently published version for its life; republishing the catalogue "
                + "changes nothing here until the draft is migrated. Expires after 24 hours by default, "
                + "the same figure the measurement draft uses.")
            .RequirePermission(CatalogPermissions.DesignSelect, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(DesignSelectionDraftHandler.DraftStartedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapGet(
                "/design-drafts/{draftId:guid}",
                async Task<IResult> (
                    Guid draftId,
                    HttpContext context,
                    DesignSelectionDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.ReadAsync(draftId, caller.Context.OrganisationId, cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(DesignSelectionDraftPayload.From(result.Value));
                })
            .Produces<DesignSelectionDraftPayload>(StatusCodes.Status200OK)
            .WithName("GetCatalogDesignSelectionDraft")
            .WithSummary("Read a design selection draft.")
            .WithDescription(
                "The entity tag is what a save sends back as If-Match. When the catalogue was republished "
                + "since the draft was pinned, the answer carries a migration prompt naming exactly what "
                + "moved — an option retired, a group newly required, a rule added — and the pin holds "
                + "until the draft is migrated.")
            .RequirePermission(CatalogPermissions.DesignSelect, BranchScope.CurrentBranch)
            .ScopedToResource(DesignSelectionResourceKinds.DesignSelectionDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        catalog.MapPut(
                "/design-drafts/{draftId:guid}",
                async Task<IResult> (
                    Guid draftId,
                    SaveDesignSelectionsRequest request,
                    HttpContext context,
                    DesignSelectionDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    ArgumentNullException.ThrowIfNull(request);

                    var result = await handler.SaveAsync(
                        new SaveDesignSelectionsCommand(
                            draftId,
                            [.. (request.Selections ?? []).Select(selection => selection.ToInput())],
                            request.Instructions,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(DesignSelectionDraftPayload.From(result.Value.Draft, null));
                })
            .Produces<DesignSelectionDraftPayload>(StatusCodes.Status200OK)
            .WithName("SaveCatalogDesignSelectionDraft")
            .WithSummary("Replace the whole selection set of a draft.")
            .WithDescription(
                "Saved whole rather than per group: the picker renders and saves one garment's choices at "
                + "once. A value for a group or an option this version does not have at all is refused; "
                + "everything else — offerability, rules, a required group left unset — is answered by "
                + "…/check rather than by this route.")
            .RequirePermission(CatalogPermissions.DesignSelect, BranchScope.CurrentBranch)
            .ScopedToResource(DesignSelectionResourceKinds.DesignSelectionDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(DesignSelectionDraftHandler.DraftSavedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        catalog.MapGet(
                "/design-drafts/{draftId:guid}/check",
                async Task<IResult> (
                    Guid draftId,
                    bool? hasReferenceImage,
                    HttpContext context,
                    DesignSelectionDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.CheckAsync(
                        draftId, caller.Context.OrganisationId, hasReferenceImage ?? false, cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Ok(DesignCheckPayload.From(draftId, result.Value));
                })
            .Produces<DesignCheckPayload>(StatusCodes.Status200OK)
            .WithName("CheckCatalogDesignSelectionDraft")
            .WithSummary("Ask what stands between a draft and confirmation.")
            .WithDescription(
                "Changes nothing. Reports every violation at once, blocking or not — a note never blocks. "
                + "hasReferenceImage answers a requires-attachment rule: Catalog holds no garment and no "
                + "media of its own, so the caller who does supplies it, the same way order confirmation "
                + "will through IDesignSelectionQuery.")
            .RequirePermission(CatalogPermissions.DesignSelect, BranchScope.CurrentBranch)
            .ScopedToResource(DesignSelectionResourceKinds.DesignSelectionDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        catalog.MapPost(
                "/design-drafts/{draftId:guid}/migrate",
                async Task<IResult> (
                    Guid draftId,
                    bool? hasReferenceImage,
                    HttpContext context,
                    DesignSelectionDraftHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.MigrateAsync(
                        new MigrateDesignSelectionDraftCommand(
                            draftId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            hasReferenceImage ?? false,
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(DesignMigrationOutcomePayload.From(result.Value));
                })
            .Produces<DesignMigrationOutcomePayload>(StatusCodes.Status200OK)
            .WithName("MigrateCatalogDesignSelectionDraft")
            .WithSummary("Re-pin a draft to the currently published version and re-validate it.")
            .WithDescription(
                "Applies exactly the migration the draft's own read named: a selection whose option was "
                + "retired cannot survive it, and a group newly required or a rule newly added applies "
                + "from here on, never retrospectively to the pinned version. Already-current is a no-op "
                + "success rather than a refusal.")
            .RequirePermission(CatalogPermissions.DesignSelect, BranchScope.CurrentBranch)
            .ScopedToResource(DesignSelectionResourceKinds.DesignSelectionDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(DesignSelectionDraftHandler.DraftMigratedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return catalog;
    }

    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);
}
