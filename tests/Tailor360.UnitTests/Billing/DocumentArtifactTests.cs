using Shouldly;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Domain.Invoicing;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The rendered document's row (#155): requested pending, completed once with what was stored, failed for good
/// after the bounded attempts; and the model a document is rendered from, which names the customer as the
/// document does and nothing more.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DocumentArtifactTests
{
    private static readonly Guid JobOne = BillingTestData.Id("job-1");
    private static readonly Guid Cashier = BillingTestData.Id("cashier");

    [Fact]
    public void IsRequestedPendingCompletedOnceAndNeverAgain()
    {
        var artifact = DocumentArtifact.Request(BillingTestData.Id("art"), BillingTestData.Organisation, BillingTestData.MainBranch, DocumentKind.Invoice, BillingTestData.Id("inv"), " INV-MAIN-2627-000001 ", BillingTestData.Now);

        artifact.Status.ShouldBe(DocumentArtifactStatus.Pending);
        artifact.IsCompleted.ShouldBeFalse();
        artifact.Version.ShouldBe(1);
        artifact.DocumentNumber.ShouldBe("INV-MAIN-2627-000001");

        artifact.Complete("documents/x", DocumentArtifact.PdfContentType, 0, new string('a', 64), BillingTestData.Now).Error.Code.ShouldBe("billing.value-required");
        artifact.Complete("documents/x", DocumentArtifact.PdfContentType, 10, "short", BillingTestData.Now).Error.Code.ShouldBe("billing.value-required");

        artifact.Complete("documents/x", DocumentArtifact.PdfContentType, 1234, new string('a', 64), BillingTestData.Now.AddSeconds(5)).IsSuccess.ShouldBeTrue();
        artifact.IsCompleted.ShouldBeTrue();
        artifact.ObjectKey.ShouldBe("documents/x");
        artifact.SizeBytes.ShouldBe(1234);
        artifact.Sha256.ShouldBe(new string('a', 64));
        artifact.Attempts.ShouldBe(1);
        artifact.LastError.ShouldBeNull();
        artifact.CompletedAt.ShouldBe(BillingTestData.Now.AddSeconds(5));

        artifact.Complete("documents/y", DocumentArtifact.PdfContentType, 1, new string('b', 64), BillingTestData.Now).Error.Code.ShouldBe("billing.document-already-rendered");
        artifact.ObjectKey.ShouldBe("documents/x");
    }

    [Fact]
    public void FailsForGoodAfterTheBoundedAttemptsAndKeepsTheCodeNotTheStack()
    {
        var artifact = DocumentArtifact.Request(BillingTestData.Id("art"), BillingTestData.Organisation, BillingTestData.MainBranch, DocumentKind.CreditNote, BillingTestData.Id("cn"), "CN-MAIN-2627-000001", BillingTestData.Now);

        for (var attempt = 1; attempt < DocumentArtifact.MaximumAttempts; attempt++)
        {
            artifact.RecordFailure("billing.document-store-unavailable", BillingTestData.Now).ShouldBeFalse();
            artifact.Status.ShouldBe(DocumentArtifactStatus.Pending);
        }

        artifact.RecordFailure(new string('e', DocumentArtifact.MaximumErrorLength + 50), BillingTestData.Now).ShouldBeTrue();
        artifact.Status.ShouldBe(DocumentArtifactStatus.Failed);
        artifact.Attempts.ShouldBe(DocumentArtifact.MaximumAttempts);
        artifact.LastError!.Length.ShouldBe(DocumentArtifact.MaximumErrorLength);
    }

    [Fact]
    public void TheInvoiceModelCarriesTheDocumentAndNothingElseAboutThePerson()
    {
        var invoice = InvoiceTests.Draft([InvoiceTests.Line(JobOne, "BLOUSE")]).Value;
        invoice.Post("INV-MAIN-2627-000007", "I-7K3M9QW2XZ4B", "2627", new DateOnly(2026, 9, 12), BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
        var model = DocumentModels.Invoice(invoice, "Main branch", "Goods once tailored are not returned.");

        model["kind"].ShouldBe("Invoice");
        model["number"].ShouldBe("INV-MAIN-2627-000007");
        model["issuedOn"].ShouldBe("12-09-2026");
        model["financialYear"].ShouldBe("2627");
        model["barcodePayload"].ShouldBe("I-7K3M9QW2XZ4B");
        model["renderedAt"].ShouldBe(BillingTestData.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var supplier = (IReadOnlyDictionary<string, object?>)model["supplier"]!;
        supplier["gstin"].ShouldBe(BillingTestData.WellFormedGstin);
        supplier["legalName"].ShouldBe("Example Tailors Private Limited", "the name as issued, frozen on the invoice");
        supplier["tradeName"].ShouldBe("Example Tailors");
        supplier["branchName"].ShouldBe("Main branch");
        var customer = (IReadOnlyDictionary<string, object?>)model["customer"]!;
        customer.Keys.ShouldBe(["customerNumber", "displayName", "addressLine", "locality", "postcode"], ignoreOrder: true);
        var line = ((IEnumerable<object?>)model["lines"]!).Cast<IReadOnlyDictionary<string, object?>>().Single();
        line["lineTotal"].ShouldBe(567m);
        ((IEnumerable<object?>)line["taxes"]!).Count().ShouldBe(2);
        var totals = (IReadOnlyDictionary<string, object?>)model["totals"]!;
        totals["grandTotal"].ShouldBe(567m);
        totals["balanceDue"].ShouldBe(567m);
        model["terms"].ShouldBe("Goods once tailored are not returned.");

        // A note's model names the invoice it is against and the note's own figures.
        var note = invoice.PostNote(BillingTestData.Id("cn"), AdjustmentNoteKind.Credit, "CN-MAIN-2627-000001", [new AdjustmentNoteLineRequest(JobOne, 90m)], "Lining twice.", BillingTestData.Today, BillingTestData.Now, Cashier).Value;
        var noteModel = DocumentModels.Note(invoice, note, "Main branch");
        noteModel["kind"].ShouldBe("CreditNote");
        noteModel["number"].ShouldBe("CN-MAIN-2627-000001");
        noteModel["relatedNumber"].ShouldBe("INV-MAIN-2627-000007");
        noteModel["reason"].ShouldBe("Lining twice.");
        ((IReadOnlyDictionary<string, object?>)noteModel["totals"]!)["grandTotal"].ShouldBe(94.5m);
    }
}
