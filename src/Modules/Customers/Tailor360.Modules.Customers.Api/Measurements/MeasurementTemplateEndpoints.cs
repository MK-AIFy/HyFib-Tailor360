using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Customers.Api.Payloads;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Customers.Api.Measurements;

/// <summary>
/// Administering the field sets a garment is measured against.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every route is organisation-scoped, because a template is.</strong> A field set is the shop's
/// definition of how a blouse is measured; it is not owned by a branch and does not differ between them. Scoping
/// these to a branch would invite two branches to drift into measuring the same garment differently, which is the
/// failure the versioning exists to prevent.
/// </para>
/// <para>
/// <strong>Drafting is <c>catalog.templates.edit</c>; reviewing and publishing are
/// <c>catalog.templates.publish</c>.</strong> The split is the separation of duties the plan asks for: the person
/// who writes a template is not, by that permission alone, the person who decides it is right. Approval, return
/// and retirement sit on the publishing side with publication itself, because each is a reviewer's act.
/// </para>
/// <para>
/// <strong>Every route demanding <c>catalog.templates.publish</c> carries a second factor.</strong> The permission
/// is flagged for step-up in the catalogue, so the flag belongs to the permission rather than to the individual
/// act — approving and returning included, not only publishing and retiring. That is deliberate: approval is the
/// decision publication then executes, and a reviewer whose session was taken over is as costly as a publisher
/// whose session was. Publication and retirement additionally demand a stated reason, because they change what
/// every counter in the shop is asked to measure and the trail has to say who decided and why, months later, when
/// a garment does not fit.
/// </para>
/// </remarks>
public static class MeasurementTemplateEndpoints
{
    /// <summary>Maps the measurement-template routes.</summary>
    /// <param name="customers">The <c>/api/v1/customers</c> group.</param>
    /// <returns>The same group, for chaining.</returns>
    public static RouteGroupBuilder MapMeasurementTemplateEndpoints(this RouteGroupBuilder customers)
    {
        ArgumentNullException.ThrowIfNull(customers);

        MapReads(customers);
        MapDrafting(customers);
        MapLifecycle(customers);

        return customers;
    }

    private static void MapReads(RouteGroupBuilder customers)
    {
        customers.MapGet("/measurement-templates", async Task<IResult> (
                MeasurementTemplateHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var templates = await handler.ListAsync(caller.Context.OrganisationId, cancellationToken);

                return Results.Ok(templates
                    .Select(administered => MeasurementTemplatePayload.From(administered.Template, withFields: false))
                    .ToArray());
            })
            .Produces<MeasurementTemplatePayload[]>(StatusCodes.Status200OK)
            .WithName("ListMeasurementTemplates")
            .WithSummary("List the measurement templates and the state of each version.")
            .WithDescription(
                "Without the fields: a list screen shows which templates exist, which version of each is "
                + "published and what is in draft. Read one template to see its field set.")
            .RequirePermission(CustomersPermissions.EditTemplates, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        customers.MapGet("/measurement-templates/{templateId:guid}", async Task<IResult> (
                HttpContext context,
                MeasurementTemplateHandler handler,
                ICurrentUser caller,
                Guid templateId,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(
                    templateId, caller.Context.OrganisationId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(MeasurementTemplatePayload.From(result.Value.Template, withFields: true));
            })
            .Produces<MeasurementTemplatePayload>(StatusCodes.Status200OK)
            .WithName("GetMeasurementTemplate")
            .WithSummary("Read one template and every version of it, with their fields.")
            .WithDescription(
                "The entity tag is the template's, so an If-Match on any command below is a precondition on the "
                + "whole template rather than on one version of it.")
            .RequirePermission(CustomersPermissions.EditTemplates, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        customers.MapGet(
                "/measurement-templates/{templateId:guid}/versions/{versionId:guid}/validation",
                async Task<IResult> (
                    HttpContext context,
                    MeasurementTemplateHandler handler,
                    ICurrentUser caller,
                    Guid templateId,
                    Guid versionId,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.ValidateAsync(
                        templateId, versionId, caller.Context.OrganisationId, cancellationToken);

                    return result.IsFailure
                        ? Problems.From(result.Error, context)
                        : Results.Ok(TemplateValidationPayload.From(result.Value));
                })
            .Produces<TemplateValidationPayload>(StatusCodes.Status200OK)
            .WithName("ValidateMeasurementTemplateVersion")
            .WithSummary("Check a version without changing anything.")
            .WithDescription(
                "The preview publication runs. Every finding is reported rather than the first, because a "
                + "published version cannot be corrected in place and an administrator fixing one wants the whole "
                + "list rather than six round trips. Warnings do not refuse publication; errors do.")
            .RequirePermission(CustomersPermissions.EditTemplates, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);
    }

    private static void MapDrafting(RouteGroupBuilder customers)
    {
        customers.MapPost("/measurement-templates", async Task<IResult> (
                HttpContext context,
                MeasurementTemplateHandler handler,
                ICurrentUser caller,
                CreateMeasurementTemplateRequest request,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                var result = await handler.CreateAsync(
                    new CreateMeasurementTemplateCommand(
                        caller.Context.OrganisationId, request.Code, request.Name, request.Description,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Created(
                    $"/api/v1/customers/measurement-templates/{result.Value.Template.Id}",
                    MeasurementTemplatePayload.From(result.Value.Template, withFields: true));
            })
            .Produces<MeasurementTemplatePayload>(StatusCodes.Status201Created)
            .WithName("CreateMeasurementTemplate")
            .WithSummary("Create a measurement template with no versions.")
            .WithDescription(
                "The template is the thing a catalogue service type points at; it captures nothing until a "
                + "version has been drafted, reviewed and published.")
            .RequirePermission(CustomersPermissions.EditTemplates, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(MeasurementTemplateHandler.TemplateCreatedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        customers.MapPost("/measurement-templates/{templateId:guid}/versions", async Task<IResult> (
                HttpContext context,
                MeasurementTemplateHandler handler,
                ICurrentUser caller,
                Guid templateId,
                StartTemplateDraftRequest request,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                if (!Enum.TryParse<DisplayUnit>(request.DefaultDisplayUnit, ignoreCase: true, out var unit))
                {
                    return Problems.From(
                        MeasurementApiErrors.NotAValidValue("defaultDisplayUnit", request.DefaultDisplayUnit),
                        context);
                }

                var result = await handler.StartDraftAsync(
                    new StartTemplateDraftCommand(
                        templateId, caller.Context.OrganisationId, request.Name, request.Notes, unit,
                        request.CloneFromVersionId, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Created(
                    $"/api/v1/customers/measurement-templates/{templateId}",
                    MeasurementTemplatePayload.From(result.Value.Template, withFields: true));
            })
            .Produces<MeasurementTemplatePayload>(StatusCodes.Status201Created)
            .WithName("StartMeasurementTemplateDraft")
            .WithSummary("Start a draft version, empty or copied from an existing one.")
            .WithDescription(
                "Cloning is how a published version is changed: the fields are copied with fresh identities and "
                + "the same keys, so the draft describes the same measurements and can be edited freely without "
                + "touching what the published version says.")
            .RequirePermission(CustomersPermissions.EditTemplates, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(MeasurementTemplateHandler.DraftedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        customers.MapPost(
                "/measurement-templates/{templateId:guid}/versions/{versionId:guid}/fields",
                async Task<IResult> (
                    HttpContext context,
                    MeasurementTemplateHandler handler,
                    ICurrentUser caller,
                    Guid templateId,
                    Guid versionId,
                    TemplateFieldRequest request,
                    CancellationToken cancellationToken) =>
                    await SaveFieldAsync(
                        context, handler, caller, templateId, versionId, null, request, cancellationToken))
            .Produces<MeasurementTemplatePayload>(StatusCodes.Status201Created)
            .WithName("AddMeasurementTemplateField")
            .WithSummary("Add a field to a draft.")
            .WithDescription(
                "The key is what captured values are filed under, so it is unique within the version and cannot "
                + "be changed once the version is published.")
            .RequirePermission(CustomersPermissions.EditTemplates, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(MeasurementTemplateHandler.FieldAddedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        customers.MapPut(
                "/measurement-templates/{templateId:guid}/versions/{versionId:guid}/fields/{fieldId:guid}",
                async Task<IResult> (
                    HttpContext context,
                    MeasurementTemplateHandler handler,
                    ICurrentUser caller,
                    Guid templateId,
                    Guid versionId,
                    Guid fieldId,
                    TemplateFieldRequest request,
                    CancellationToken cancellationToken) =>
                    await SaveFieldAsync(
                        context, handler, caller, templateId, versionId, fieldId, request, cancellationToken))
            .Produces<MeasurementTemplatePayload>(StatusCodes.Status200OK)
            .WithName("ChangeMeasurementTemplateField")
            .WithSummary("Replace a field of a draft, keeping its key.")
            .WithDescription(
                "The key in the body is ignored. Renaming through an edit would orphan every value already filed "
                + "under the old one, so a rename is a removal and an addition, which the trail shows as two acts.")
            .RequirePermission(CustomersPermissions.EditTemplates, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(MeasurementTemplateHandler.FieldChangedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        customers.MapPost(
                "/measurement-templates/{templateId:guid}/versions/{versionId:guid}/fields/{fieldId:guid}/delete",
                async Task<IResult> (
                    HttpContext context,
                    MeasurementTemplateHandler handler,
                    ICurrentUser caller,
                    Guid templateId,
                    Guid versionId,
                    Guid fieldId,
                    TemplateReasonRequest? request,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.RemoveFieldAsync(
                        new RemoveTemplateFieldCommand(
                            templateId, versionId, caller.Context.OrganisationId, fieldId, request?.Reason,
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(MeasurementTemplatePayload.From(result.Value.Template, withFields: true));
                })
            .Produces<MeasurementTemplatePayload>(StatusCodes.Status200OK)
            .WithName("RemoveMeasurementTemplateField")
            .WithSummary("Remove a field from a draft.")
            .WithDescription(
                "A sub-resource rather than DELETE, so that the reason travels in a body like every other "
                + "command here. Only a draft admits it.")
            .RequirePermission(CustomersPermissions.EditTemplates, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(MeasurementTemplateHandler.FieldRemovedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
    }

    private static void MapLifecycle(RouteGroupBuilder customers)
    {
        Lifecycle(
            customers,
            "submit",
            CustomersPermissions.EditTemplates,
            MeasurementTemplateHandler.SubmittedAction,
            "SubmitMeasurementTemplateVersion",
            "Submit a draft for a second administrator to review.",
            "The draft stops being editable the moment it is submitted: a reviewer reads a version that cannot "
            + "change under them.",
            reasonRequired: false,
            stepUp: false,
            (handler, command, token) => handler.SubmitAsync(command, token));

        Lifecycle(
            customers,
            "return",
            CustomersPermissions.PublishTemplates,
            MeasurementTemplateHandler.ReturnedAction,
            "ReturnMeasurementTemplateVersion",
            "Send a version in review back to its author.",
            "The approval goes back with it. A version that comes back for changes and is then resubmitted has "
            + "not been reviewed in the state it is now in.",
            reasonRequired: true,
            stepUp: true,
            (handler, command, token) => handler.ReturnToDraftAsync(command, token));

        Lifecycle(
            customers,
            "approve",
            CustomersPermissions.PublishTemplates,
            MeasurementTemplateHandler.ApprovedAction,
            "ApproveMeasurementTemplateVersion",
            "Approve a reviewed version, so that it may be published.",
            "The administrator who submitted a version does not also approve it — unless they are the only "
            + "administrator who could, because a rule that locks a one-owner shop out of its own templates is "
            + "not a rule that shop can follow.",
            reasonRequired: false,
            stepUp: true,
            (handler, command, token) => handler.ApproveAsync(command, token));

        Lifecycle(
            customers,
            "publish",
            CustomersPermissions.PublishTemplates,
            MeasurementTemplateHandler.PublishedAction,
            "PublishMeasurementTemplateVersion",
            "Make a version the one measurements are captured against.",
            "Publication retires the version it supersedes in the same act, so there is never a moment with two "
            + "published versions or none. Measurements already taken still render through the version they were "
            + "captured under. Refused while publish validation reports an error — read the validation route "
            + "first to see them all.",
            reasonRequired: true,
            stepUp: true,
            (handler, command, token) => handler.PublishAsync(command, token));

        Lifecycle(
            customers,
            "retire",
            CustomersPermissions.PublishTemplates,
            MeasurementTemplateHandler.RetiredAction,
            "RetireMeasurementTemplateVersion",
            "Stop new captures against the published version.",
            "Refused while a published catalogue version still points at this template: retiring it would leave "
            + "a service a counter can order with nothing to measure it by. Everything already captured still "
            + "renders through it.",
            reasonRequired: true,
            stepUp: true,
            (handler, command, token) => handler.RetireAsync(command, token));
    }

    private static void Lifecycle(
        RouteGroupBuilder customers,
        string verb,
        string permission,
        string action,
        string name,
        string summary,
        string description,
        bool reasonRequired,
        bool stepUp,
        Func<MeasurementTemplateHandler, TemplateLifecycleCommand, CancellationToken,
            Task<Tailor360.Platform.Abstractions.Results.Result<AdministeredTemplate>>> move)
    {
        var route = customers.MapPost(
                $"/measurement-templates/{{templateId:guid}}/versions/{{versionId:guid}}/{verb}",
                async Task<IResult> (
                    HttpContext context,
                    MeasurementTemplateHandler handler,
                    ICurrentUser caller,
                    Guid templateId,
                    Guid versionId,
                    TemplateReasonRequest? request,
                    CancellationToken cancellationToken) =>
                {
                    if (reasonRequired && string.IsNullOrWhiteSpace(request?.Reason))
                    {
                        return Problems.From(MeasurementApiErrors.ReasonRequired, context);
                    }

                    var result = await move(
                        handler,
                        new TemplateLifecycleCommand(
                            templateId,
                            versionId,
                            caller.Context.OrganisationId,
                            request?.Reason ?? string.Empty,
                            context.Request.TryGetIfMatch(out var expected) ? expected : null,
                            caller.UserId),
                        cancellationToken);

                    if (result.IsFailure)
                    {
                        return Problems.From(result.Error, context);
                    }

                    context.Response.SetEntityTag(result.Value.Tag);

                    return Results.Ok(MeasurementTemplatePayload.From(result.Value.Template, withFields: true));
                })
            .Produces<MeasurementTemplatePayload>(StatusCodes.Status200OK)
            .WithName(name)
            .WithSummary(summary)
            .WithDescription(description)
            .RequirePermission(permission, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(action, reasonRequired)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        if (stepUp)
        {
            route.RequireStepUp();
        }
    }

    private static async Task<IResult> SaveFieldAsync(
        HttpContext context,
        MeasurementTemplateHandler handler,
        ICurrentUser caller,
        Guid templateId,
        Guid versionId,
        Guid? fieldId,
        TemplateFieldRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = request.ToDefinition();

        if (definition.IsFailure)
        {
            return Problems.From(definition.Error, context);
        }

        var result = await handler.SaveFieldAsync(
            new SaveTemplateFieldCommand(
                templateId, versionId, caller.Context.OrganisationId, fieldId, definition.Value,
                context.Request.TryGetIfMatch(out var expected) ? expected : null, caller.UserId),
            cancellationToken);

        if (result.IsFailure)
        {
            return Problems.From(result.Error, context);
        }

        context.Response.SetEntityTag(result.Value.Tag);

        var payload = MeasurementTemplatePayload.From(result.Value.Template, withFields: true);

        return fieldId is null
            ? Results.Created($"/api/v1/customers/measurement-templates/{templateId}", payload)
            : Results.Ok(payload);
    }
}
