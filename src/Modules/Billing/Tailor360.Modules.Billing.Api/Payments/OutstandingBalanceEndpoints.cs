using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Payments;

/// <summary>
/// The branch's outstanding balances, in one read (#421): every posted invoice at the caller's branch
/// with money still owed, the server-side aggregate #216 asked a decision on rather than the client
/// walking every page of <c>ListInvoices</c> and asking <c>GetOrderBalance</c> once per invoice on it.
/// </summary>
internal static class OutstandingBalanceEndpoints
{
    public static RouteGroupBuilder MapOutstandingBalanceEndpoints(this RouteGroupBuilder billing)
    {
        billing.MapGet("/outstanding-balances", async Task<IResult> (
                HttpContext context,
                OutstandingBalanceQuery query,
                ICurrentUser caller,
                CancellationToken cancellationToken,
                string? cursor = null,
                int limit = InvoiceListQuery.DefaultLimit) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(BillingErrors.Required("branch"), context);
                }

                var page = await query.GetAsync(
                    caller.Context.OrganisationId, branchId, cursor, Math.Clamp(limit, 1, InvoiceListQuery.MaximumLimit), cancellationToken);

                return Results.Ok(OutstandingBalancePagePayload.From(page));
            })
            .Produces<OutstandingBalancePagePayload>(StatusCodes.Status200OK)
            .WithName("ListOutstandingBalances")
            .WithSummary("The branch's posted invoices with money still owed, by cursor.")
            .WithDescription(
                "Filled a page at a time from the rows ListInvoices and GetOrderBalance already read (INV-PAY-06): a "
                + "source page of posted invoices that are all settled yields no rows on its own, so the read loops "
                + "whole source pages until it has enough rows or the branch's posted invoices are exhausted. "
                + "`nextCursor` is non-null whenever the scan stopped without exhausting the branch's posted invoices, "
                + "so the screen keeps its Show more control rather than showing the empty state on a page that simply "
                + "found nothing yet; it is null only once nothing posted and unpaid is left to find.")
            .RequirePermission(BillingPermissions.CreateInvoice, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        return billing;
    }
}
