using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Integration.Infrastructure.Documents;

/// <summary>
/// Renders the statutory documents with QuestPDF (ADR-0014): one template per document kind, the model
/// read by name. Deterministic for a model: the creation and modification dates in the file come from the
/// model's <c>renderedAt</c>, so two renderings of one posted document are byte-for-byte the same and a
/// checksum is worth keeping.
/// </summary>
public sealed class QuestPdfRenderer : IPdfRenderer
{
    /// <summary>The invoice template.</summary>
    public const string InvoiceTemplate = "billing.invoice";

    /// <summary>The credit-note template.</summary>
    public const string CreditNoteTemplate = "billing.credit-note";

    /// <summary>The debit-note template.</summary>
    public const string DebitNoteTemplate = "billing.debit-note";

    /// <summary>The receipt template: 80 mm continuous, for the counter's roll printer (plan D15, #35).</summary>
    public const string ReceiptTemplate = "billing.receipt";

    /// <inheritdoc />
    public Task<Result> RenderAsync(string templateKey, IReadOnlyDictionary<string, object?> model, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(destination);

        if (templateKey is not (InvoiceTemplate or CreditNoteTemplate or DebitNoteTemplate or ReceiptTemplate))
        {
            return Task.FromResult(Result.Failure(Error.Validation("integration.template-not-known", "No template of that name is rendered here.", "templateKey")));
        }

        DocumentFonts.EnsureRegistered();
        var values = new DocumentModel(model);
        var renderedAt = DateTimeOffset.TryParse(values.Text("renderedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : DateTimeOffset.UnixEpoch;

        var document = Document.Create(container => container.Page(page =>
        {
            if (templateKey == ReceiptTemplate)
            {
                // The roll: 80 mm wide, as long as the receipt needs, no header or footer band.
                page.ContinuousSize(ReceiptTemplateLayout.WidthMillimetres, Unit.Millimetre);
                page.Margin(4, Unit.Millimetre);
                page.DefaultTextStyle(style => style.FontSize(8.5f).FontFamily(DocumentFonts.Family, DocumentFonts.TamilFamily).FontColor(Colors.Black));
                page.Content().Element(content => ReceiptTemplateLayout.Body(content, values));
                return;
            }

            page.Size(PageSizes.A4);
            page.Margin(14, Unit.Millimetre);
            page.DefaultTextStyle(style => style.FontSize(9.5f).FontFamily(DocumentFonts.Family, DocumentFonts.TamilFamily).FontColor(Colors.Black));
            page.Header().Element(header => BillingDocumentTemplate.Header(header, templateKey, values));
            page.Content().Element(content => BillingDocumentTemplate.Body(content, templateKey, values));
            page.Footer().Element(footer => BillingDocumentTemplate.Footer(footer, values));
        }))
        .WithMetadata(new DocumentMetadata
        {
            Title = $"{BillingDocumentTemplate.TitleOf(templateKey)} {values.Text("number")}",
            Subject = BillingDocumentTemplate.TitleOf(templateKey),
            Author = templateKey == ReceiptTemplate ? values.Text("branchName") : values.Section("supplier").Text("legalName"),
            Creator = "HyFib Tailor 360",
            Producer = "HyFib Tailor 360",
            Language = "en-IN",
            CreationDate = renderedAt,
            ModifiedDate = renderedAt,
        });

        try
        {
            document.GeneratePdf(destination);
        }
        catch (Exception exception) when (exception is QuestPDF.Drawing.Exceptions.DocumentLayoutException or QuestPDF.Drawing.Exceptions.DocumentComposeException or QuestPDF.Drawing.Exceptions.DocumentDrawingException)
        {
            // Refused rather than thrown: the port returns a result, and the caller counts the attempt.
            return Task.FromResult(Result.Failure(Error.Unavailable("integration.render-failed", exception.GetType().Name)));
        }

        return Task.FromResult(Result.Success());
    }
}
