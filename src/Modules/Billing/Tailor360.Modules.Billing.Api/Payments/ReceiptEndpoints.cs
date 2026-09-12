using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Payments;

/// <summary>
/// The receipt's document (#169): streamed and printed under <c>billing.print_receipt</c>, the Cashier's
/// permission (raci row 5), each access audited against the receipt. The receipt itself is issued with the
/// payment and read on it; there is no route that changes one.
/// </summary>
internal static class ReceiptEndpoints
{
    public static RouteGroupBuilder MapReceiptEndpoints(this RouteGroupBuilder billing)
    {
        billing.MapGet("/receipts/{receiptId:guid}/document", async Task<IResult> (
                Guid receiptId,
                HttpContext context,
                DocumentArtifactHandler documents,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var opened = await documents.OpenReceiptAsync(receiptId, caller.Context.OrganisationId, caller.UserId, cancellationToken);
                if (opened.IsFailure)
                {
                    return Problems.From(opened.Error, context);
                }

                context.Response.Headers.CacheControl = "no-store";
                return Results.File(opened.Value.Content, opened.Value.ContentType, $"{opened.Value.DocumentNumber}.pdf");
            })
            .Produces<Stream>(StatusCodes.Status200OK, DocumentArtifact.PdfContentType)
            .WithName("DownloadReceiptDocument")
            .WithSummary("Stream the rendered receipt.")
            .WithDescription(
                "The bytes the worker stored, as they were stored: no URL to the object is ever given out, the caller is "
                + "re-authorised in branch scope on every call, and the access is written to the audit trail against the "
                + "receipt. Not available until the worker has rendered the document.")
            .RequirePermission(BillingPermissions.PrintReceipt, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Receipt, "receiptId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            // Declared although ARCH-008 does not demand it of a GET, and the entry is written by the handler
            // rather than by this metadata: the sensitive read is visible where every other audit is.
            .Audited(DocumentArtifactHandler.ReceiptDownloadedAction)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPost("/receipts/{receiptId:guid}/print", async Task<IResult> (
                Guid receiptId,
                PrintReceiptRequest? request,
                HttpContext context,
                DocumentArtifactHandler documents,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var queued = await documents.PrintReceiptAsync(receiptId, caller.Context.OrganisationId, request?.Copies ?? 1, caller.UserId, cancellationToken);
                return queued.IsFailure ? Problems.From(queued.Error, context) : Results.Accepted(value: new PrintJobPayload(queued.Value));
            })
            .Produces<PrintJobPayload>(StatusCodes.Status202Accepted)
            .WithName("PrintReceipt")
            .WithSummary("Send the rendered receipt to the branch's print queue, on the receipt roll.")
            .WithDescription(
                "Through the print-queue port, one to five copies; acknowledged with the job's identifier and audited. Not "
                + "available until the document is rendered. Until the print bridge of #55 replaces the adapter, the queue "
                + "acknowledges and logs the job and nothing is printed (ADR-0014).")
            .RequirePermission(BillingPermissions.PrintReceipt, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Receipt, "receiptId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(DocumentArtifactHandler.ReceiptPrintedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapGet("/receipts/barcode/{payload}", async Task<IResult> (
                string payload,
                HttpContext context,
                DocumentArtifactHandler documents,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(Domain.BillingErrors.Required("branch"), context);
                }

                var receipt = await documents.ResolveReceiptBarcodeAsync(payload, caller.Context.OrganisationId, branchId, cancellationToken);
                return receipt is null
                    ? Problems.From(Domain.BillingErrors.DocumentNotFound, context)
                    : Results.Ok(ReceiptPayload.From(receipt));
            })
            .Produces<ReceiptPayload>(StatusCodes.Status200OK)
            .WithName("ResolveReceiptBarcode")
            .WithSummary("Resolve an R- barcode payload to the receipt it was printed on.")
            .WithDescription(
                "For a caller working in the branch that issued the receipt. Another branch's receipt, another organisation's, "
                + "a payload whose check character does not hold, an I- payload and a payload of nothing all read alike as not "
                + "found: the lookup confirms the existence of nothing it does not show. Its own route rather than a second "
                + "answer shape on the invoice lookup, so that route's contract stays as published.")
            .RequirePermission(BillingPermissions.PrintReceipt, BranchScope.CurrentBranch)
            .TouchesNoBranchOwnedResource(
                "The route parameter is an opaque barcode payload, not a resource identifier: nothing can be resolved before the "
                + "payload is parsed, and the handler answers only a receipt of the caller's own branch.",
                "#169")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        return billing;
    }
}
