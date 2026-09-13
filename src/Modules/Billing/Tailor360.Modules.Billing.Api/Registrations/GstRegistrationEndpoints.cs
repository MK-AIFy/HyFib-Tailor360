using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Registrations;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Registrations;

/// <summary>The GST registration routes (#41, #145).</summary>
internal static class GstRegistrationEndpoints
{
    public static RouteGroupBuilder MapGstRegistrationEndpoints(this RouteGroupBuilder billing)
    {
        billing.MapGet("/gst-registrations", async Task<IResult> (
                IGstRegistrationStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var registrations = await store.ListAsync(caller.Context.OrganisationId, cancellationToken);

                return Results.Ok(registrations.Select(GstRegistrationPayload.From).ToArray());
            })
            .Produces<GstRegistrationPayload[]>(StatusCodes.Status200OK)
            .WithName("ListGstRegistrations")
            .WithSummary("List the organisation's GST registrations, by branch and first day.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPost("/gst-registrations", async Task<IResult> (
                GstRegistrationRequest request,
                HttpContext context,
                GstRegistrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.AddAsync(
                    new AddGstRegistrationCommand(
                        caller.Context.OrganisationId, request.ToDetails(), request.Reason, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Created(
                    $"/api/v1/billing/gst-registrations/{result.Value.Registration.Id}",
                    GstRegistrationPayload.From(result.Value.Registration));
            })
            .Produces<GstRegistrationPayload>(StatusCodes.Status201Created)
            .WithName("AddGstRegistration")
            .WithSummary("Record a branch's GST registration.")
            .WithDescription(
                "The GSTIN is checked for shape and check character, never against the tax portal. At most one "
                + "registration of a branch is in force on any day; an overlap is refused.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(GstRegistrationHandler.AddedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapGet("/gst-registrations/{registrationId:guid}", async Task<IResult> (
                Guid registrationId,
                HttpContext context,
                IGstRegistrationStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var registration = await store.FindAsync(registrationId, caller.Context.OrganisationId, cancellationToken);
                if (registration is null)
                {
                    return Problems.From(Domain.BillingErrors.RegistrationNotFound, context);
                }

                context.Response.SetEntityTag(store.EntityTagOf(registration));

                return Results.Ok(GstRegistrationPayload.From(registration));
            })
            .Produces<GstRegistrationPayload>(StatusCodes.Status200OK)
            .WithName("GetGstRegistration")
            .WithSummary("Read one GST registration. The ETag is the token an amendment is made against.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPut("/gst-registrations/{registrationId:guid}", async Task<IResult> (
                Guid registrationId,
                GstRegistrationRequest request,
                HttpContext context,
                GstRegistrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.AmendAsync(
                    new AmendGstRegistrationCommand(
                        registrationId,
                        caller.Context.OrganisationId,
                        context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty),
                        request.ToDetails(),
                        request.Reason,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(GstRegistrationPayload.From(result.Value.Registration));
            })
            .Produces<GstRegistrationPayload>(StatusCodes.Status200OK)
            .WithName("AmendGstRegistration")
            .WithSummary("Amend a GST registration: its dates, names and number. The branch never changes.")
            .WithDescription(
                "A registration is never deleted — invoices were issued under it — so a branch re-registered "
                + "under a new number ends this one the day before and records the new one.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(GstRegistrationHandler.ChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return billing;
    }
}
