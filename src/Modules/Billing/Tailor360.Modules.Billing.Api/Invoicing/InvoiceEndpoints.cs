using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Invoicing;

/// <summary>The invoice-draft routes (#42, #153): draft from an order, read, re-price, discard, list.</summary>
/// <remarks>
/// Every route is in branch scope: an invoice is issued by the branch that took the order, and the
/// routes that name one resolve its branch through the module's resource-scope resolver (ARCH-023). No
/// read permission exists in the catalogue for an invoice, so reading sits on the permission that
/// drafts one — a cashier who may draft may read — as data classification 5.10 describes.
/// </remarks>
public static class InvoiceEndpoints
{
    /// <summary>Maps the routes onto the Billing group.</summary>
    public static RouteGroupBuilder MapInvoiceEndpoints(this RouteGroupBuilder billing)
    {
        ArgumentNullException.ThrowIfNull(billing);

        billing.MapGet("/invoices", async Task<IResult> (
                HttpContext context,
                IInvoiceStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken,
                string? status = null,
                string? cursor = null,
                int limit = InvoiceListQuery.DefaultLimit) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(Domain.BillingErrors.Required("branch"), context);
                }

                InvoiceStatus? wanted = null;
                if (status is not null)
                {
                    if (!Enum.TryParse<InvoiceStatus>(status, ignoreCase: false, out var parsed))
                    {
                        return Problems.From(Domain.BillingErrors.Required("filter[status]"), context);
                    }

                    wanted = parsed;
                }

                var page = await store.ListAsync(
                    new InvoiceListQuery(caller.Context.OrganisationId, branchId, wanted, cursor, Math.Clamp(limit, 1, InvoiceListQuery.MaximumLimit)),
                    cancellationToken);

                return Results.Ok(InvoicePagePayload.From(page));
            })
            .Produces<InvoicePagePayload>(StatusCodes.Status200OK)
            .WithName("ListInvoices")
            .WithSummary("The branch's invoices, newest first, by cursor.")
            .WithDescription("Filtered by `status` (Draft, Posted, Discarded) when given; the caller's current branch only.")
            .RequirePermission(BillingPermissions.CreateInvoice, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPost("/invoices", async Task<IResult> (
                CreateInvoiceRequest request,
                HttpContext context,
                InvoiceHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(Domain.BillingErrors.Required("branch"), context);
                }

                if (request.OrderId is not { } orderId)
                {
                    return Problems.From(Domain.BillingErrors.Required("orderId"), context);
                }

                if (string.IsNullOrWhiteSpace(request.CalculationReference))
                {
                    return Problems.From(Domain.BillingErrors.Required("calculationReference"), context);
                }

                var result = await handler.CreateDraftAsync(
                    new CreateInvoiceDraftCommand(
                        caller.Context.OrganisationId, branchId, orderId, request.CalculationReference, request.GarmentJobIds ?? [],
                        caller.Permissions, request.Reason, caller.UserId),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Created($"/api/v1/billing/invoices/{result.Value.Invoice.Id}", InvoicePayload.From(result.Value.Invoice));
            })
            .Produces<InvoicePayload>(StatusCodes.Status201Created)
            .WithName("CreateInvoiceDraft")
            .WithSummary("Draft an invoice from an order's stored calculation.")
            .WithDescription(
                "The order must be confirmed, not cancelled and taken at the caller's branch; the calculation named must "
                + "reproduce on the versions it was made on; no live invoice may already charge for any of its garment jobs. "
                + "Nothing is re-priced: the lines are the calculation's.")
            .RequirePermission(BillingPermissions.CreateInvoice, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(InvoiceHandler.DraftedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapGet("/invoices/{invoiceId:guid}", async Task<IResult> (
                Guid invoiceId,
                HttpContext context,
                IInvoiceStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var invoice = await store.FindAsync(invoiceId, caller.Context.OrganisationId, cancellationToken);
                if (invoice is null)
                {
                    return Problems.From(Domain.BillingErrors.InvoiceNotFound, context);
                }

                context.Response.SetEntityTag(store.EntityTagOf(invoice));

                return Results.Ok(InvoicePayload.From(invoice));
            })
            .Produces<InvoicePayload>(StatusCodes.Status200OK)
            .WithName("GetInvoice")
            .WithSummary("Read an invoice with its lines, and the tag a change sends back as If-Match.")
            .RequirePermission(BillingPermissions.CreateInvoice, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPut("/invoices/{invoiceId:guid}", async Task<IResult> (
                Guid invoiceId,
                RepriceInvoiceRequest request,
                HttpContext context,
                InvoiceHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (request.On is not { } on)
                {
                    return Problems.From(Domain.BillingErrors.Required("on"), context);
                }

                foreach (var line in request.Lines ?? [])
                {
                    if (line?.Discount is { Value: null })
                    {
                        return Problems.From(Domain.BillingErrors.Required($"lines[{line.LineKey}].discount.value"), context);
                    }

                    if (line?.Override is { Rate: null })
                    {
                        return Problems.From(Domain.BillingErrors.Required($"lines[{line.LineKey}].override.rate"), context);
                    }
                }

                var result = await handler.RepriceAsync(
                    new RepriceInvoiceCommand(
                        invoiceId, caller.Context.OrganisationId, Precondition(context), on, request.PlaceOfSupplyStateCode ?? string.Empty,
                        request.ToLines(), request.Reason, caller.UserId),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(InvoicePayload.From(result.Value.Invoice));
            })
            .Produces<InvoicePayload>(StatusCodes.Status200OK)
            .WithName("RepriceInvoiceDraft")
            .WithSummary("Replace a draft's lines by pricing them afresh.")
            .WithDescription(
                "Whole-value: the lines sent are the lines kept, priced by the engine under a reference of the draft's own. "
                + "A discount beyond its rule's counter maximum or an override beyond the version's threshold needs "
                + "billing.override_price on a recently re-authenticated session, as everywhere.")
            .RequirePermission(BillingPermissions.UpdateInvoice, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(InvoiceHandler.UpdatedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPost("/invoices/{invoiceId:guid}/discard", async Task<IResult> (
                Guid invoiceId,
                BillingReasonRequest? request,
                HttpContext context,
                InvoiceHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.DiscardAsync(
                    new DiscardInvoiceCommand(invoiceId, caller.Context.OrganisationId, Precondition(context), request?.Reason, caller.UserId),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(InvoicePayload.From(result.Value.Invoice));
            })
            .Produces<InvoicePayload>(StatusCodes.Status200OK)
            .WithName("DiscardInvoiceDraft")
            .WithSummary("Abandon a draft, freeing its garment jobs for another invoice.")
            .WithDescription("A reason is recorded. A posted invoice is never discarded; it is cancelled by its compensating record.")
            .RequirePermission(BillingPermissions.UpdateInvoice, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(InvoiceHandler.DiscardedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return billing;
    }

    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);
}
