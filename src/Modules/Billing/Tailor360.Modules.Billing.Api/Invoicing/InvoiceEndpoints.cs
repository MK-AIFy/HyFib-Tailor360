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
                    // By name only: Enum.TryParse would take "99" as a status the enumeration does not define.
                    if (status.Length == 0 || char.IsAsciiDigit(status[0]) || !Enum.TryParse<InvoiceStatus>(status, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
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

        billing.MapPost("/invoices/{invoiceId:guid}/post", async Task<IResult> (
                Guid invoiceId,
                BillingReasonRequest? request,
                HttpContext context,
                InvoiceHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.PostAsync(
                    new PostInvoiceCommand(invoiceId, caller.Context.OrganisationId, Precondition(context), request?.Reason, caller.UserId),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(InvoicePayload.From(result.Value.Invoice));
            })
            .Produces<InvoicePayload>(StatusCodes.Status200OK)
            .WithName("PostInvoice")
            .WithSummary("Post a draft: number it, mint its barcode and freeze it.")
            .WithDescription(
                "The draft's figures are recomputed from the calculation it was drafted from and must match; the number is "
                + "drawn under the sequence lock in the transaction that freezes the row, so two posts at one branch are numbered "
                + "one after the other and a post that fails returns its number. A replay of the same Idempotency-Key answers "
                + "the original. After posting, nothing about the invoice changes: it is cancelled by its compensating record.")
            .RequirePermission(BillingPermissions.PostInvoice, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(InvoiceHandler.PostedAction)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPost("/invoices/{invoiceId:guid}/cancel", async Task<IResult> (
                Guid invoiceId,
                BillingReasonRequest? request,
                HttpContext context,
                InvoiceHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.CancelAsync(
                    new CancelInvoiceCommand(invoiceId, caller.Context.OrganisationId, request?.Reason, caller.UserId),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(InvoicePayload.From(result.Value.Invoice));
            })
            .Produces<InvoicePayload>(StatusCodes.Status200OK)
            .WithName("CancelInvoice")
            .WithSummary("Cancel a posted invoice by its compensating record.")
            .WithDescription(
                "Appends the cancellation and posts a credit note relieving the whole amount, in one transaction; the invoice keeps "
                + "its number, its lines and its totals, and its displayed status becomes cancelled. Within the configured cancellation "
                + "window where one is set. A reason is recorded, and the session must have re-authenticated recently. No If-Match: "
                + "nothing on the invoice's row moves, so the row is locked and re-read in the transaction instead. The invoice's "
                + "garment jobs are free to be invoiced again.")
            .RequirePermission(BillingPermissions.CancelInvoice, BranchScope.CurrentBranch)
            .RequireStepUp()
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(InvoiceHandler.CancelledAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPost("/invoices/{invoiceId:guid}/credit-notes", async Task<IResult> (
                Guid invoiceId,
                PostAdjustmentNoteRequest request,
                HttpContext context,
                InvoiceHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
                await PostNoteAsync(invoiceId, AdjustmentNoteKind.Credit, request, context, handler, caller, cancellationToken))
            .Produces<AdjustmentNotePayload>(StatusCodes.Status201Created)
            .WithName("PostCreditNote")
            .WithSummary("Post a credit note against a posted invoice.")
            .WithDescription(
                "Per line: each line names a garment job of the invoice and the taxable value relieved, taxed at that line's own "
                + "rates and rounded once per component; a line is relieved at most to what it still carries. Numbered from the "
                + "credit-note sequence, posted once, immutable.")
            .RequirePermission(BillingPermissions.PostCreditNote, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(InvoiceHandler.CreditNotePostedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPost("/invoices/{invoiceId:guid}/debit-notes", async Task<IResult> (
                Guid invoiceId,
                PostAdjustmentNoteRequest request,
                HttpContext context,
                InvoiceHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
                await PostNoteAsync(invoiceId, AdjustmentNoteKind.Debit, request, context, handler, caller, cancellationToken))
            .Produces<AdjustmentNotePayload>(StatusCodes.Status201Created)
            .WithName("PostDebitNote")
            .WithSummary("Post a debit note against a posted invoice.")
            .WithDescription(
                "Per line, as a credit note, adding to what the customer owes. The same permission as a credit note: both are the "
                + "compensating documents a posted invoice is corrected by.")
            .RequirePermission(BillingPermissions.PostCreditNote, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(InvoiceHandler.DebitNotePostedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapGet("/invoices/{invoiceId:guid}/document", async Task<IResult> (
                Guid invoiceId,
                HttpContext context,
                DocumentArtifactHandler documents,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var opened = await documents.OpenInvoiceAsync(invoiceId, caller.Context.OrganisationId, caller.UserId, cancellationToken);
                return opened.IsFailure ? Problems.From(opened.Error, context) : StreamDocument(context, opened.Value);
            })
            .Produces<Stream>(StatusCodes.Status200OK, DocumentArtifact.PdfContentType)
            .WithName("DownloadInvoiceDocument")
            .WithSummary("Stream the rendered invoice.")
            .WithDescription(
                "The bytes the worker stored, as they were stored: no URL to the object is ever given out, the caller is "
                + "re-authorised in branch scope on every call, and the access is written to the audit trail against the "
                + "invoice. Not available until the worker has rendered the document.")
            .RequirePermission(BillingPermissions.CreateInvoice, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            // Declared although ARCH-008 does not demand it of a GET, and the entry is written by the handler
            // rather than by this metadata: the sensitive read is visible where every other audit is.
            .Audited(DocumentArtifactHandler.DownloadedAction)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapGet("/invoices/{invoiceId:guid}/notes/{noteId:guid}/document", async Task<IResult> (
                Guid invoiceId,
                Guid noteId,
                HttpContext context,
                DocumentArtifactHandler documents,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var opened = await documents.OpenNoteAsync(invoiceId, noteId, caller.Context.OrganisationId, caller.UserId, cancellationToken);
                return opened.IsFailure ? Problems.From(opened.Error, context) : StreamDocument(context, opened.Value);
            })
            .Produces<Stream>(StatusCodes.Status200OK, DocumentArtifact.PdfContentType)
            .WithName("DownloadNoteDocument")
            .WithSummary("Stream the rendered credit or debit note.")
            .WithDescription("As the invoice's document: re-authorised in branch scope through the invoice, audited against the invoice.")
            .RequirePermission(BillingPermissions.CreateInvoice, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .Audited(DocumentArtifactHandler.DownloadedAction)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        billing.MapPost("/invoices/{invoiceId:guid}/print", async Task<IResult> (
                Guid invoiceId,
                PrintInvoiceRequest? request,
                HttpContext context,
                DocumentArtifactHandler documents,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var queued = await documents.PrintInvoiceAsync(invoiceId, caller.Context.OrganisationId, request?.Copies ?? 1, caller.UserId, cancellationToken);
                return queued.IsFailure ? Problems.From(queued.Error, context) : Results.Accepted(value: new PrintJobPayload(queued.Value));
            })
            .Produces<PrintJobPayload>(StatusCodes.Status202Accepted)
            .WithName("PrintInvoice")
            .WithSummary("Send the rendered invoice to the branch's print queue.")
            .WithDescription(
                "Through the print-queue port; acknowledged with the job's identifier and audited. Not available until the document "
                + "is rendered. Until the print bridge of #55 replaces the adapter, the queue acknowledges and logs the job and "
                + "nothing is printed (ADR-0014).")
            .RequirePermission(BillingPermissions.CreateInvoice, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Invoice, "invoiceId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(DocumentArtifactHandler.PrintedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapGet("/barcodes/{payload}", async Task<IResult> (
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

                var invoice = await documents.ResolveBarcodeAsync(payload, caller.Context.OrganisationId, branchId, cancellationToken);
                return invoice is null
                    ? Problems.From(Domain.BillingErrors.DocumentNotFound, context)
                    : Results.Ok(BarcodeResolutionPayload.From(invoice));
            })
            .Produces<BarcodeResolutionPayload>(StatusCodes.Status200OK)
            .WithName("ResolveInvoiceBarcode")
            .WithSummary("Resolve an I- barcode payload to the invoice it was printed on.")
            .WithDescription(
                "For a caller working in the branch that issued the invoice. Another branch's invoice, another organisation's, "
                + "a payload whose check character does not hold and a payload of nothing all read alike as not found: the "
                + "lookup confirms the existence of nothing it does not show. The payload's namespace, check character and "
                + "branch are re-validated here on every call.")
            .RequirePermission(BillingPermissions.CreateInvoice, BranchScope.CurrentBranch)
            .TouchesNoBranchOwnedResource(
                "The route parameter is an opaque barcode payload, not a resource identifier: nothing can be resolved before the "
                + "handler validates the check character, and the handler answers only an invoice of the caller's own branch, "
                + "answering everything else as not found.",
                "#155")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        return billing;
    }

    /// <summary>Streams a stored document, named by its number and nothing else, never cached.</summary>
    private static IResult StreamDocument(HttpContext context, StoredDocument document)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.File(document.Content, document.ContentType, $"{document.DocumentNumber}.pdf");
    }

    private static async Task<IResult> PostNoteAsync(
        Guid invoiceId,
        AdjustmentNoteKind kind,
        PostAdjustmentNoteRequest request,
        HttpContext context,
        InvoiceHandler handler,
        ICurrentUser caller,
        CancellationToken cancellationToken)
    {
        foreach (var (line, index) in (request.Lines ?? []).Select((line, index) => (line, index)))
        {
            if (line?.GarmentJobId is null)
            {
                return Problems.From(Domain.BillingErrors.Required($"lines[{index}].garmentJobId"), context);
            }

            if (line.TaxableValue is null)
            {
                return Problems.From(Domain.BillingErrors.Required($"lines[{index}].taxableValue"), context);
            }
        }

        var result = await handler.PostNoteAsync(
            new PostAdjustmentNoteCommand(
                invoiceId, caller.Context.OrganisationId, kind, request.ToLines(), request.Reason, caller.UserId),
            cancellationToken);
        if (result.IsFailure)
        {
            return Problems.From(result.Error, context);
        }

        context.Response.SetEntityTag(result.Value.Tag);

        return Results.Created($"/api/v1/billing/invoices/{invoiceId}", AdjustmentNotePayload.From(result.Value.Note));
    }

    private static EntityTag Precondition(HttpContext context)
        => context.Request.TryGetIfMatch(out var expected) ? expected : new EntityTag(string.Empty);
}
