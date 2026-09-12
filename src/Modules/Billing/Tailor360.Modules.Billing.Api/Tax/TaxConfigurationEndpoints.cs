using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Tax;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Tax;

/// <summary>
/// The tax configuration routes (#41, #145): versions, their codes, and publication.
/// </summary>
/// <remarks>
/// The same shape as the catalogue version routes, because it is the same kind of thing: versioned
/// configuration an administrator changes without a deployment, immutable once published. Every
/// write takes <c>Idempotency-Key</c>; every change to a version takes <c>If-Match</c> against the
/// version's tag; publication demands a fresh second factor and a reason.
/// </remarks>
internal static class TaxConfigurationEndpoints
{
    public static RouteGroupBuilder MapTaxConfigurationEndpoints(this RouteGroupBuilder billing)
    {
        MapList(billing);
        MapCreate(billing);
        MapRead(billing);
        MapDescribe(billing);
        MapValidate(billing);
        MapTaxCodes(billing);
        MapPublish(billing);

        return billing;
    }

    private static void MapList(RouteGroupBuilder billing)
        => billing.MapGet("/tax-configuration/versions", async Task<IResult> (
                Application.Abstractions.ITaxConfigurationStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var versions = await store.ListAsync(caller.Context.OrganisationId, cancellationToken);

                return Results.Ok(versions.Select(TaxConfigurationSummaryPayload.From).ToArray());
            })
            .Produces<TaxConfigurationSummaryPayload[]>(StatusCodes.Status200OK)
            .WithName("ListTaxConfigurationVersions")
            .WithSummary("List the organisation's tax configuration versions, newest first.")
            .WithDescription("Summaries only; a version's codes are read one version at a time.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapCreate(RouteGroupBuilder billing)
        => billing.MapPost("/tax-configuration/versions", async Task<IResult> (
                CreateTaxConfigurationDraftRequest? request,
                HttpContext context,
                TaxConfigurationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.CreateDraftAsync(
                    new CreateTaxConfigurationDraftCommand(
                        caller.Context.OrganisationId,
                        request?.Name ?? string.Empty,
                        request?.Notes,
                        request?.EffectiveFrom ?? default,
                        request?.CloneFromVersionId,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Created(
                    $"/api/v1/billing/tax-configuration/versions/{result.Value.Version.Id}",
                    TaxConfigurationPayload.From(result.Value.Version));
            })
            .Produces<TaxConfigurationPayload>(StatusCodes.Status201Created)
            .WithName("CreateTaxConfigurationDraft")
            .WithSummary("Start a draft tax configuration version, empty or cloned from an existing one.")
            .WithDescription(
                "Cloning the published version is the ordinary way to change what is in force: a published "
                + "version is immutable, so a rate change is a clone, an edit and a second publication. The "
                + "clone carries the same codes as concepts with new rows of its own.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(TaxConfigurationHandler.DraftedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static void MapRead(RouteGroupBuilder billing)
        => billing.MapGet("/tax-configuration/versions/{versionId:guid}", async Task<IResult> (
                Guid versionId,
                HttpContext context,
                Application.Abstractions.ITaxConfigurationStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var version = await store.FindAsync(versionId, caller.Context.OrganisationId, cancellationToken);
                if (version is null)
                {
                    return Problems.From(Domain.BillingErrors.VersionNotFound, context);
                }

                context.Response.SetEntityTag(store.EntityTagOf(version));

                return Results.Ok(TaxConfigurationPayload.From(version));
            })
            .Produces<TaxConfigurationPayload>(StatusCodes.Status200OK)
            .WithName("GetTaxConfigurationVersion")
            .WithSummary("Read one tax configuration version and its codes, whatever its status.")
            .WithDescription("The ETag is the token every change to the version is made against.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapDescribe(RouteGroupBuilder billing)
        => billing.MapPut("/tax-configuration/versions/{versionId:guid}", async Task<IResult> (
                Guid versionId,
                DescribeTaxConfigurationRequest request,
                HttpContext context,
                TaxConfigurationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.DescribeAsync(
                    new DescribeTaxConfigurationCommand(
                        versionId,
                        caller.Context.OrganisationId,
                        Precondition(context),
                        request.Name ?? string.Empty,
                        request.Notes,
                        request.EffectiveFrom ?? default,
                        request.Reason,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(TaxConfigurationPayload.From(result.Value.Version));
            })
            .Produces<TaxConfigurationPayload>(StatusCodes.Status200OK)
            .WithName("DescribeTaxConfigurationVersion")
            .WithSummary("Change a draft's name, notes and effective date.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(TaxConfigurationHandler.ChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static void MapValidate(RouteGroupBuilder billing)
        => billing.MapGet("/tax-configuration/versions/{versionId:guid}/validation", async Task<IResult> (
                Guid versionId,
                HttpContext context,
                TaxConfigurationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ValidateAsync(versionId, caller.Context.OrganisationId, cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(BillingValidationReportPayload.From(result.Value));
            })
            .Produces<BillingValidationReportPayload>(StatusCodes.Status200OK)
            .WithName("ValidateTaxConfigurationVersion")
            .WithSummary("Run the publication checks against a version and report what they found.")
            .WithDescription(
                "The checks say what would make a calculation contradict itself — a CGST without its SGST, an "
                + "IGST that is not their sum, a code re-spelled after invoices carried it. They encode no rate.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapTaxCodes(RouteGroupBuilder billing)
    {
        billing.MapPost("/tax-configuration/versions/{versionId:guid}/tax-codes", async Task<IResult> (
                Guid versionId,
                TaxCodeRequest request,
                HttpContext context,
                TaxConfigurationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.AddTaxCodeAsync(
                    new AddTaxCodeCommand(
                        versionId,
                        caller.Context.OrganisationId,
                        Precondition(context),
                        request.ToDetails(),
                        request.Reason,
                        caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Created(
                        $"/api/v1/billing/tax-configuration/versions/{versionId}/tax-codes/{result.Value.Id}",
                        TaxCodePayload.From(result.Value));
            })
            .Produces<TaxCodePayload>(StatusCodes.Status201Created)
            .WithName("AddTaxCode")
            .WithSummary("Add a tax code to a draft tax configuration version.")
            .WithDescription(
                "A code is a classification (HSN or SAC) and the components it carries. Only a draft accepts it; "
                + "the components' arithmetic is checked at publication so a half-entered code can be saved.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(TaxConfigurationHandler.TaxCodeAddedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPut("/tax-configuration/versions/{versionId:guid}/tax-codes/{taxCodeId:guid}", async Task<IResult> (
                Guid versionId,
                Guid taxCodeId,
                TaxCodeRequest request,
                HttpContext context,
                TaxConfigurationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.EditTaxCodeAsync(
                    new EditTaxCodeCommand(
                        versionId,
                        caller.Context.OrganisationId,
                        Precondition(context),
                        taxCodeId,
                        request.ToDetails(),
                        request.Reason,
                        caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(TaxCodePayload.From(result.Value));
            })
            .Produces<TaxCodePayload>(StatusCodes.Status200OK)
            .WithName("EditTaxCode")
            .WithSummary("Replace what a draft says about a tax code.")
            .WithDescription("Whole-value, not partial: an omitted rate list means 'nil-rated' rather than 'unchanged'.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(TaxConfigurationHandler.TaxCodeChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPost(
                "/tax-configuration/versions/{versionId:guid}/tax-codes/{taxCodeId:guid}/delete",
                async Task<IResult> (
                    Guid versionId,
                    Guid taxCodeId,
                    BillingReasonRequest? request,
                    HttpContext context,
                    TaxConfigurationHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.RemoveTaxCodeAsync(
                        new RemoveTaxCodeCommand(
                            versionId,
                            caller.Context.OrganisationId,
                            Precondition(context),
                            taxCodeId,
                            request?.Reason,
                            caller.UserId),
                        cancellationToken);

                    return result.IsFailure ? Problems.From(result.Error, context) : Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .WithName("RemoveTaxCode")
            .WithSummary("Remove a tax code from a draft tax configuration version.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(TaxConfigurationHandler.TaxCodeRemovedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapPublish(RouteGroupBuilder billing)
        => billing.MapPost("/tax-configuration/versions/{versionId:guid}/publish", async Task<IResult> (
                Guid versionId,
                BillingReasonRequest? request,
                HttpContext context,
                TaxConfigurationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.PublishAsync(
                    new PublishTaxConfigurationCommand(
                        versionId,
                        caller.Context.OrganisationId,
                        Precondition(context),
                        request?.Reason ?? string.Empty,
                        caller.UserId),
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

                return Results.Ok(new TaxConfigurationPublicationPayload(
                    TaxConfigurationPayload.From(result.Value.Published.Version),
                    result.Value.SupersededVersionId,
                    [.. result.Value.Findings.Select(BillingFindingPayload.From)]));
            })
            .Produces<TaxConfigurationPublicationPayload>(StatusCodes.Status200OK)
            .WithName("PublishTaxConfigurationVersion")
            .WithSummary("Publish a draft, retiring the version it supersedes in the same transaction.")
            .WithDescription(
                "Refused while a publication check fails, with every finding in the problem detail. Two "
                + "administrators publishing different drafts at once is settled by a partial unique index: one "
                + "commits and the other is answered 409.")
            .RequirePermission(BillingPermissions.PublishPriceList, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(TaxConfigurationHandler.PublishedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .RequireStepUp()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);
}
