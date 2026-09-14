using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
public sealed partial class QuestPdfRenderer : IPdfRenderer
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
        })
        // ADR-0014 section 5 / #512: PDF/UA-1 turns on the structure tree QuestPDF otherwise omits — the
        // document and page landmarks (Document, Header, Content, Footer) are then emitted for every
        // template with no further code, which is what closes A11Y-DP-04 (declared reading order).
        // BillingDocumentTemplate adds the table-header roles and the Tamil language span this conformance
        // level requires of it (A11Y-DP-02, A11Y-DP-05); the receipt carries no such element and needs none.
        .WithSettings(new DocumentSettings { PDFUA_Conformance = PDFUA_Conformance.PDFUA_1 });

        byte[] rendered;
        try
        {
            using var buffer = new MemoryStream();
            document.GeneratePdf(buffer);
            rendered = buffer.ToArray();
        }
        catch (Exception exception) when (exception is QuestPDF.Drawing.Exceptions.DocumentLayoutException or QuestPDF.Drawing.Exceptions.DocumentComposeException or QuestPDF.Drawing.Exceptions.DocumentDrawingException)
        {
            // Refused rather than thrown: the port returns a result, and the caller counts the attempt.
            return Task.FromResult(Result.Failure(Error.Unavailable("integration.render-failed", exception.GetType().Name)));
        }

        rendered = StabiliseFileIdentifier(rendered, $"{templateKey}|{values.Text("number")}|{values.Text("renderedAt")}");
        destination.Write(rendered, 0, rendered.Length);

        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// PDF/UA-1 (#512) has QuestPDF write a random file identifier — the trailer's <c>/ID</c> pair and the
    /// matching <c>xmpMM:DocumentID</c>/<c>InstanceID</c> in the embedded XMP packet — freshly on every
    /// call, which breaks the determinism ADR-0014 promises and this renderer is tested on. Both forms
    /// encode the same sixteen bytes, so replacing them with sixteen bytes derived from the model keeps the
    /// file's own internal consistency and makes two renderings of one model byte-for-byte identical again.
    /// Every replacement is the same length as what it replaces, so no offset in the file moves.
    /// </summary>
    private static byte[] StabiliseFileIdentifier(byte[] pdf, string seed)
    {
        var text = Encoding.Latin1.GetString(pdf);
        if (!TrailerFileIdentifier().IsMatch(text))
        {
            // Nothing QuestPDF wrote randomly this time; leave the bytes exactly as generated.
            return pdf;
        }

        var stableId = SHA256.HashData(Encoding.UTF8.GetBytes(seed))[..16];
        var hex = Convert.ToHexString(stableId);
        var uuid = string.Create(36, hex, static (span, source) =>
        {
            source.AsSpan(0, 8).CopyTo(span);
            span[8] = '-';
            source.AsSpan(8, 4).CopyTo(span[9..]);
            span[13] = '-';
            source.AsSpan(12, 4).CopyTo(span[14..]);
            span[18] = '-';
            source.AsSpan(16, 4).CopyTo(span[19..]);
            span[23] = '-';
            source.AsSpan(20, 12).CopyTo(span[24..]);
        }).ToLowerInvariant();

        text = TrailerFileIdentifier().Replace(text, $"/ID [<{hex}> <{hex}>]");
        text = XmpDocumentId().Replace(text, uuid);
        text = XmpInstanceId().Replace(text, uuid);

        return Encoding.Latin1.GetBytes(text);
    }

    [GeneratedRegex(@"/ID\s*\[\s*<[0-9A-Fa-f]{32}>\s*<[0-9A-Fa-f]{32}>\s*\]")]
    private static partial Regex TrailerFileIdentifier();

    [GeneratedRegex(@"(?<=<xmpMM:DocumentID>uuid:)[0-9a-fA-F-]{36}(?=</xmpMM:DocumentID>)")]
    private static partial Regex XmpDocumentId();

    [GeneratedRegex(@"(?<=<xmpMM:InstanceID>uuid:)[0-9a-fA-F-]{36}(?=</xmpMM:InstanceID>)")]
    private static partial Regex XmpInstanceId();
}
