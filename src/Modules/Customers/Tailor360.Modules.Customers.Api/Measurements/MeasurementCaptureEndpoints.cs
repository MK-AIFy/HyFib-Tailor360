using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Customers.Api.Payloads;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Customers.Api.Measurements;

/// <summary>
/// Measuring a garment at a counter (issue #121).
/// </summary>
/// <remarks>
/// <para>
/// Five routes, and the shape of them follows what a person does rather than what the data is. Start (or pick
/// up) the measuring; save a step; ask whether it would be accepted; confirm; read a confirmed measurement.
/// </para>
/// <para>
/// <strong>Every command carries <c>If-Match</c> and an <c>Idempotency-Key</c>.</strong> Drafts are shared within
/// a branch, so a section save without a precondition would silently overwrite the half somebody else measured;
/// and a confirmation is the one act here that must never happen twice, so a retry after a lost answer has to
/// reach the measurement it already made.
/// </para>
/// <para>
/// <strong>The branch scope is the current branch.</strong> Measurements are taken where the customer is standing,
/// and a draft belongs to the branch measuring — reading another branch's half-finished garment is not something
/// this surface offers.
/// </para>
/// </remarks>
public static class MeasurementCaptureEndpoints
{
    /// <summary>Maps the measurement-capture routes.</summary>
    /// <param name="customers">The <c>/api/v1/customers</c> group.</param>
    /// <returns>The same group, for chaining.</returns>
    public static RouteGroupBuilder MapMeasurementCaptureEndpoints(this RouteGroupBuilder customers)
    {
        ArgumentNullException.ThrowIfNull(customers);

        customers.MapPost("/measurement-drafts", async Task<IResult> (
                HttpContext context,
                MeasurementCaptureHandler handler,
                ICurrentUser caller,
                StartMeasurementDraftRequest request,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                // A draft belongs to the branch measuring, and there is no sensible default: measurements are
                // taken where the customer is standing. A caller with no branch is refused rather than filed
                // under an arbitrary one.
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(MeasurementErrors.BranchRequired, context);
                }

                var result = await handler.StartAsync(
                    new StartMeasurementDraftCommand(
                        request.CustomerId,
                        request.MeasurementTemplateId,
                        request.ReuseFromVersionId,
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
                    $"/api/v1/customers/measurement-drafts/{result.Value.Draft.Id}",
                    MeasurementDraftPayload.From(result.Value.Draft));
            })
            .Produces<MeasurementDraftPayload>(StatusCodes.Status201Created)
            .WithName("StartMeasurementDraft")
            .WithSummary("Start measuring a garment, or pick up the measuring already under way.")
            .WithDescription(
                "A branch has at most one open draft per customer and template, so asking twice reaches the "
                + "first one rather than starting a second set of half-finished measurements. Reusing an earlier "
                + "measurement pre-fills the values whose fields the published template version still has.")
            .RequirePermission(CustomersPermissions.CaptureMeasurements, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(MeasurementCaptureHandler.DraftStartedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        customers.MapGet("/measurement-drafts/{draftId:guid}", async Task<IResult> (
                HttpContext context,
                MeasurementCaptureHandler handler,
                ICurrentUser caller,
                Guid draftId,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(
                    draftId, caller.Context.OrganisationId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(MeasurementDraftPayload.From(result.Value.Draft));
            })
            .Produces<MeasurementDraftPayload>(StatusCodes.Status200OK)
            .WithName("GetMeasurementDraft")
            .WithSummary("Read a garment being measured.")
            .WithDescription(
                "The entity tag is what a section save sends back as If-Match. Drafts are shared within a branch, "
                + "so the tag is how two people measuring one garment between them avoid overwriting each other.")
            .RequirePermission(CustomersPermissions.CaptureMeasurements, BranchScope.CurrentBranch)
            .ScopedToResource(MeasurementResourceKinds.MeasurementDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        customers.MapPost("/measurement-drafts/{draftId:guid}/sections", async Task<IResult> (
                HttpContext context,
                MeasurementCaptureHandler handler,
                ICurrentUser caller,
                Guid draftId,
                SaveMeasurementSectionRequest request,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                var result = await handler.SaveSectionAsync(
                    new SaveMeasurementSectionCommand(
                        draftId,
                        request.GroupName,
                        [.. request.Values.Select(value => value.ToCaptured())],
                        caller.Context.OrganisationId,
                        Precondition(context),
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(MeasurementDraftPayload.From(result.Value.Draft));
            })
            .Produces<MeasurementDraftPayload>(StatusCodes.Status200OK)
            .WithName("SaveMeasurementSection")
            .WithSummary("Save one step of the measuring wizard.")
            .WithDescription(
                "The values replace that step rather than merging into it, which is what lets a value be cleared. "
                + "Nothing here is checked against the template's ranges: a half-measured garment is a normal "
                + "state, and the check happens at confirmation.")
            .RequirePermission(CustomersPermissions.CaptureMeasurements, BranchScope.CurrentBranch)
            .ScopedToResource(MeasurementResourceKinds.MeasurementDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(MeasurementCaptureHandler.SectionSavedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        customers.MapGet("/measurement-drafts/{draftId:guid}/check", async Task<IResult> (
                HttpContext context,
                MeasurementCaptureHandler handler,
                ICurrentUser caller,
                Guid draftId,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.CheckAsync(
                    draftId, caller.Context.OrganisationId, cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(MeasurementCheckPayload.From(draftId, result.Value));
            })
            .Produces<MeasurementCheckPayload>(StatusCodes.Status200OK)
            .WithName("CheckMeasurementDraft")
            .WithSummary("Ask what stands between a draft and a confirmed measurement.")
            .WithDescription(
                "Changes nothing. It reports every field that would be refused rather than the first, so a person "
                + "holding a tape is told about all of them at once. Each finding names the field and never the "
                + "value.")
            .RequirePermission(CustomersPermissions.CaptureMeasurements, BranchScope.CurrentBranch)
            .ScopedToResource(MeasurementResourceKinds.MeasurementDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        customers.MapPost("/measurement-drafts/{draftId:guid}/confirm", async Task<IResult> (
                HttpContext context,
                MeasurementCaptureHandler handler,
                ICurrentUser caller,
                Guid draftId,
                ConfirmMeasurementsRequest request,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                var result = await handler.ConfirmAsync(
                    new ConfirmMeasurementsCommand(
                        draftId,
                        request.Reason,
                        request.CorrectsVersionId,
                        caller.Context.OrganisationId,
                        Precondition(context),
                        caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Created(
                        $"/api/v1/customers/measurements/{result.Value.Id}",
                        MeasurementVersionPayload.From(result.Value));
            })
            .Produces<MeasurementVersionPayload>(StatusCodes.Status201Created)
            .WithName("ConfirmMeasurements")
            .WithSummary("Turn a draft into the immutable record of a measurement.")
            .WithDescription(
                "The draft is consumed exactly once, so a retry after a lost answer is a conflict rather than a "
                + "second measurement. Correcting an earlier measurement creates a new one with a reason and "
                + "leaves the old one readable; it never edits it.")
            .RequirePermission(CustomersPermissions.CaptureMeasurements, BranchScope.CurrentBranch)
            .ScopedToResource(MeasurementResourceKinds.MeasurementDraft, "draftId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(MeasurementCaptureHandler.ConfirmedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        customers.MapGet("/measurements/{measurementVersionId:guid}", async Task<IResult> (
                HttpContext context,
                MeasurementCaptureHandler handler,
                ICurrentUser caller,
                Guid measurementVersionId,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadVersionAsync(
                    measurementVersionId, caller.Context.OrganisationId, cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(MeasurementVersionPayload.From(result.Value));
            })
            .Produces<MeasurementVersionPayload>(StatusCodes.Status200OK)
            .WithName("GetMeasurement")
            .WithSummary("Read one confirmed measurement.")
            .WithDescription(
                "It renders through the template version it was captured under, forever, which is what makes a "
                + "two-year-old job card readable. It carries no entity tag, because there is no edit to make a "
                + "precondition for.")
            .RequirePermission(CustomersPermissions.CaptureMeasurements, BranchScope.AssignedBranches)
            .TouchesNoBranchOwnedResource(
                "A confirmed measurement is a fact about a customer, and a customer is organisation-wide: "
                + "docs/prd/workflows/branch-scenarios.md section 3.2 places the record organisation-wide with "
                + "branch-scoped visibility. A garment measured at one branch and stitched at another is the "
                + "ordinary case, so locking the read to the branch that held the tape would refuse exactly the "
                + "tailor who needs it. The draft it came from IS branch-owned and is scoped to its branch above; "
                + "the sensitive-read audit and the narrower measurements.read_sheet permission arrive with the "
                + "sheet in #122.",
                "#121")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        return customers;
    }

    /// <summary>Reads the <c>If-Match</c> the route already refused the request without.</summary>
    /// <remarks>
    /// Every caller sits behind <c>RequireIfMatch()</c>, so by here the header is present and well formed. It
    /// still never yields null: a null precondition means "nothing to check", and somebody working from a screen
    /// another person has already changed would silently overwrite them. An unreadable header therefore becomes a
    /// tag that matches nothing, which fails closed as a 409.
    /// </remarks>
    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);
}
