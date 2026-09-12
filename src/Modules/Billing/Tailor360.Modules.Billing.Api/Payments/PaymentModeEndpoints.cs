using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Payments;

/// <summary>
/// The payment modes, administered under the Billing configuration permission the tax configuration
/// and the price lists share: a mode is configuration of the same kind (plan D8), read and changed by
/// the same people.
/// </summary>
internal static class PaymentModeEndpoints
{
    public static RouteGroupBuilder MapPaymentModeEndpoints(this RouteGroupBuilder billing)
    {
        MapList(billing);
        MapRead(billing);
        MapDescribe(billing);

        return billing;
    }

    private static void MapList(RouteGroupBuilder billing)
        => billing.MapGet("/payment-modes", async Task<IResult> (
                IPaymentModeStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var modes = await store.ListAsync(caller.Context.OrganisationId, cancellationToken);

                return Results.Ok(modes.Select(PaymentModePayload.From).ToArray());
            })
            .Produces<PaymentModePayload[]>(StatusCodes.Status200OK)
            .WithName("ListPaymentModes")
            .WithSummary("List the organisation's payment modes, active or not, by code.")
            .WithDescription("Configuration, not money: the flags a payment in each mode is taken under, and where it is offered.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapRead(RouteGroupBuilder billing)
        => billing.MapGet("/payment-modes/{paymentModeId:guid}", async Task<IResult> (
                Guid paymentModeId,
                HttpContext context,
                IPaymentModeStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var mode = await store.FindAsync(paymentModeId, caller.Context.OrganisationId, cancellationToken);
                if (mode is null)
                {
                    return Problems.From(BillingErrors.PaymentModeNotFound, context);
                }

                context.Response.SetEntityTag(store.EntityTagOf(mode));

                return Results.Ok(PaymentModePayload.From(mode));
            })
            .Produces<PaymentModePayload>(StatusCodes.Status200OK)
            .WithName("GetPaymentMode")
            .WithSummary("Read one payment mode with the ETag every change to it is made against.")
            .WithDescription("The list carries no token; a change is read here first, then sent with If-Match.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapDescribe(RouteGroupBuilder billing)
        => billing.MapPut("/payment-modes/{paymentModeId:guid}", async Task<IResult> (
                Guid paymentModeId,
                DescribePaymentModeRequest? request,
                HttpContext context,
                PaymentModeHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.DescribeAsync(
                    new DescribePaymentModeCommand(
                        paymentModeId,
                        caller.Context.OrganisationId,
                        Precondition(context),
                        new PaymentModeDetails(request?.Name ?? string.Empty, request?.RequiresReference ?? false, request?.RequiresProvider ?? false, request?.AllowedForRefund ?? false),
                        request?.BranchIds ?? [],
                        request?.IsActive ?? true,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(PaymentModePayload.From(result.Value.Mode));
            })
            .Produces<PaymentModePayload>(StatusCodes.Status200OK)
            .WithName("DescribePaymentMode")
            .WithSummary("Change a payment mode's name, flags, branch restriction or active state.")
            .WithDescription(
                "Every field is sent, against the ETag of the mode as read. The code never changes, and a payment "
                + "already recorded keeps the mode it was taken in; deactivating a mode stops new use only.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PaymentModeHandler.ChangedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);
}
