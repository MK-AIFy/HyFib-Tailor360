using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Customers;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The parent's outstanding acceptance criterion (#42, #326): <em>the PDF's totals and tax components match
/// the persisted calculation snapshot</em>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DocumentArtifactTests"/> already proves the pipeline — posting requests an artefact, the worker
/// renders and stores it, the stored checksum equals the bytes. It never compares a <em>printed figure</em>
/// with a <em>stored figure</em>, so a renderer handed the wrong model, or printing a column it had mislabelled,
/// would pass every test in this suite. That comparison is what this class adds.
/// </para>
/// <para>
/// <strong>Both sides are read at arm's length.</strong> The printed side comes from the bytes in the object
/// store, read back through <c>OpenInvoiceAsync</c> and extracted from the PDF's own text layer — never the
/// <c>DocumentModel</c> the renderer was handed, which would only prove the renderer agrees with itself. The
/// stored side comes through <see cref="IPricingService.FindSnapshotAsync"/>, the published contract — never
/// <c>billing.calculation_snapshots</c>, because a test that read the table would assert against something no
/// consumer can see.
/// </para>
/// <para>
/// <strong>Two scenes, not one.</strong> Intra-state prints CGST and SGST and must not print IGST; inter-state
/// prints IGST and must not print CGST or SGST. One scene cannot catch a place-of-supply regression, which is
/// the mistake that puts the wrong tax on a legally reviewable document.
/// </para>
/// <para>
/// <strong>This class asserts agreement, not correctness.</strong> Whether the stored arithmetic is right is the
/// accountant's question, put to them by <c>docs/billing/accountant-document-review.md</c> and still open under
/// OD-05. What is proved here is narrower and worth proving on its own: the paper says what the system stored.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed partial class PdfSnapshotAgreementTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    /// <summary>
    /// Every figure the page prints equals the figure the snapshot holds, to the paisa — and a component the
    /// snapshot holds as zero is absent from the page rather than printed as zero.
    /// </summary>
    /// <param name="placeOfSupply">The state supplied to: the branch's own state, or another.</param>
    /// <param name="stem">A stem every code of the scene carries.</param>
    /// <param name="scheme">The scheme that place of supply must produce.</param>
    [Theory]
    [InlineData("33", "PSAI", SupplyScheme.IntraState)]
    [InlineData("29", "PSAE", SupplyScheme.InterState)]
    public async Task PdfTotalsMatchSnapshot(string placeOfSupply, string stem, SupplyScheme scheme)
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, $"pdf-{stem}", "203.0.113.190", stem, RunToken);
        using var cashier = await CashierAsync(
            fixture, $"pdf-{stem}-till", "203.0.113.191", scene.Branch, BillingPermissions.PostInvoice);

        // Priced under this case's place of supply, under its own reference, so the scene's own intra-state
        // snapshot stays where BuildAsync put it and the two cases never share a stored calculation.
        var reference = $"{scene.Reference}:pos-{placeOfSupply}";
        var priced = await PriceAsync(
            fixture, scene.Branch, reference, [.. scene.Jobs.Select(job => job.ToString())],
            scene.ItemCode, scene.SurchargeCode, placeOfSupply);
        priced.Scheme.ShouldBe(scheme, "the place of supply decides the scheme, and the scene depends on it");

        var invoiceId = await PostedInvoiceAsync(cashier, scene.OrderId, reference);
        var pdf = await StoredInvoiceBytesAsync(invoiceId);
        var lines = PdfText.Lines(pdf);

        // The stored side, through the published contract rather than the table.
        PricingResult snapshot;
        using (var scope = fixture.Services.CreateScope())
        {
            snapshot = (await scope.ServiceProvider.GetRequiredService<IPricingService>()
                .FindSnapshotAsync(SessionTestData.OrganisationId, reference, Token))
                .ShouldNotBeNull("the invoice was drafted from this reference, so a snapshot must be stored under it");
        }

        // The nine document totals. `Taxable value` and `Grand total` are emphasised in the template and are
        // printed even at zero; the other seven are suppressed at zero, which is itself a figure to assert.
        var totals = snapshot.Totals;
        AssertTotal(lines, "Subtotal", totals.Subtotal.Amount);
        AssertTotal(lines, "Discount", totals.DiscountTotal.Amount);
        AssertTotal(lines, "Taxable value", totals.TaxableValue.Amount, alwaysPrinted: true);
        AssertTotal(lines, "CGST", totals.CentralTax.Amount);
        AssertTotal(lines, "SGST", totals.StateTax.Amount);
        AssertTotal(lines, "IGST", totals.IntegratedTax.Amount);
        AssertTotal(lines, "Cess", totals.Cess.Amount);
        AssertTotal(lines, "Round-off", totals.RoundOff.Amount);
        AssertTotal(lines, "Grand total", totals.GrandTotal.Amount, alwaysPrinted: true);

        // An invoice always prints what is still owed; a note never does. This is the invoice half.
        PdfText.AmountBeside(lines, "Balance due").ShouldNotBeNull("an invoice prints what is still owed");

        // The scheme, stated as presence and absence rather than inferred from the totals above.
        if (scheme == SupplyScheme.IntraState)
        {
            totals.CentralTax.Amount.ShouldBeGreaterThan(0m, "the scene is taxed, so an intra-state supply owes CGST");
            PdfText.AmountBeside(lines, "IGST").ShouldBeNull("an intra-state supply prints no IGST");
        }
        else
        {
            totals.IntegratedTax.Amount.ShouldBeGreaterThan(0m, "the scene is taxed, so an inter-state supply owes IGST");
            PdfText.AmountBeside(lines, "CGST").ShouldBeNull("an inter-state supply prints no CGST");
            PdfText.AmountBeside(lines, "SGST").ShouldBeNull("an inter-state supply prints no SGST");
        }

        // Every line's tax components, compared as a multiset: every component the snapshot holds is printed
        // with its own kind, rate and amount, and the page prints no component the snapshot does not hold.
        PrintedTaxComponents(lines).ShouldBe(ExpectedTaxComponents(snapshot.Lines), ignoreOrder: true);

        // And the components of the scheme that does not apply are nowhere on the page at all, not even in a
        // line's own breakdown — the totals row above would not catch a per-line component printed under the
        // wrong kind, because the suppressed total is zero either way.
        var absent = scheme == SupplyScheme.IntraState ? new[] { "IGST" } : ["CGST", "SGST"];
        foreach (var kind in absent)
        {
            PrintedTaxComponents(lines).ShouldNotContain(
                component => component.Kind == kind,
                $"no line of a {scheme} supply is taxed under {kind}");
        }
    }

    /// <summary>
    /// A credit note's printed totals equal the note's own posted totals, and it prints no balance line.
    /// </summary>
    /// <remarks>
    /// A note's figures come from the invoice line's own rates rather than from a calculation snapshot — nothing
    /// is priced when a note is posted — so this is agreement with the <em>posted note</em>, not with a snapshot,
    /// and the assertion is written against the note's payload for that reason.
    /// </remarks>
    [Fact]
    public async Task NoteTotalsMatchThePostedNoteAndPrintNoBalanceDue()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "pdf-note", "203.0.113.192", "PSAN", RunToken);
        using var cashier = await CashierAsync(
            fixture, "pdf-note-till", "203.0.113.193", scene.Branch, BillingPermissions.PostInvoice, BillingPermissions.PostCreditNote);

        var invoiceId = await PostedInvoiceAsync(cashier, scene.OrderId, scene.Reference);

        // The lining alone, relieved off the first garment job: a figure that is neither the line's whole
        // taxable value nor the document's, so a renderer printing the wrong one of the three cannot pass.
        var note = await cashier.PostAsync(
            $"/api/v1/billing/invoices/{invoiceId}/credit-notes",
            new { lines = new[] { new { garmentJobId = scene.Jobs[0], taxableValue = 90m } }, reason = "Lining charged twice." },
            Key());
        note.StatusCode.ShouldBe(HttpStatusCode.Created, await note.Content.ReadAsStringAsync(Token));

        var payload = await NoteTotalsAsync(note);
        await DispatchAsync(fixture);
        (await RenderPendingAsync()).ShouldBeGreaterThanOrEqualTo(1, "the note's own document is rendered too");

        var pdf = await StoredNoteBytesAsync(invoiceId, payload.NoteId);
        var lines = PdfText.Lines(pdf);

        AssertTotal(lines, "Taxable value", payload.TaxableValue, alwaysPrinted: true);
        AssertTotal(lines, "CGST", payload.CentralTax);
        AssertTotal(lines, "SGST", payload.StateTax);
        AssertTotal(lines, "IGST", payload.IntegratedTax);
        AssertTotal(lines, "Cess", payload.Cess);
        AssertTotal(lines, "Note total", payload.GrandTotal, alwaysPrinted: true);

        // `isNote` suppresses the balance line: a note says what it moves, never what remains owed, because the
        // amount outstanding is the invoice's to state and a note that restated it would contradict it.
        PdfText.AmountBeside(lines, "Balance due").ShouldBeNull("a note prints no balance due");
        lines.ShouldNotContain(line => line.StartsWith("Grand total ", StringComparison.Ordinal), "a note's total is titled Note total");
    }

    /// <summary>
    /// Writes the accountant's sample pack — one invoice, one credit note and one debit note — to
    /// <c>artifacts/billing-document-samples/</c>, for the review <c>docs/billing/accountant-document-review.md</c>
    /// puts to them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The customer carries a <strong>Tamil display name</strong> on purpose. The template prints the display
    /// name, and a face that cannot draw Tamil fails by printing nothing rather than by failing to render, so a
    /// pack reviewed only in English would never show the one defect a Tamil-speaking customer would meet first.
    /// </para>
    /// <para>
    /// The files are deliberately <strong>not committed</strong> — <c>artifacts/</c> is outside version control,
    /// and the copies a reviewer reads are the ones attached to the pull request. This test writes them so the
    /// pack can be regenerated from a named command rather than reconstructed by hand.
    /// </para>
    /// <para>
    /// Every figure, name, address and amount in the pack is synthetic, as plan Section 2.2 requires.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WritesTheAccountantsSamplePack()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "pdf-pack", "203.0.113.194", "PSAP", RunToken);
        using var cashier = await CashierAsync(
            fixture, "pdf-pack-till", "203.0.113.195", scene.Branch, BillingPermissions.PostInvoice, BillingPermissions.PostCreditNote);

        // A second order at the same branch, for a customer whose printed name is Tamil.
        var customerId = await CustomerHarness.CustomerAsync(
            fixture, scene.Branch, displayName: "மீனா சுந்தர்");
        var (orderId, jobs) = await ConfirmOrderAsync(fixture, scene.Branch, customerId, $"O-{RunToken}-PACK", 2);

        var reference = $"order:{orderId:N}:pack";
        await PriceAsync(
            fixture, scene.Branch, reference, [.. jobs.Select(job => job.ToString())], scene.ItemCode, scene.SurchargeCode);

        var invoiceId = await PostedInvoiceAsync(cashier, orderId, reference);

        var credit = await cashier.PostAsync(
            $"/api/v1/billing/invoices/{invoiceId}/credit-notes",
            new { lines = new[] { new { garmentJobId = jobs[0], taxableValue = 90m } }, reason = "Lining charged twice." },
            Key());
        credit.StatusCode.ShouldBe(HttpStatusCode.Created, await credit.Content.ReadAsStringAsync(Token));

        var debit = await cashier.PostAsync(
            $"/api/v1/billing/invoices/{invoiceId}/debit-notes",
            new { lines = new[] { new { garmentJobId = jobs[1], taxableValue = 120m } }, reason = "Extra embroidery agreed at collection." },
            Key());
        debit.StatusCode.ShouldBe(HttpStatusCode.Created, await debit.Content.ReadAsStringAsync(Token));

        await DispatchAsync(fixture);
        (await RenderPendingAsync()).ShouldBeGreaterThanOrEqualTo(2, "both notes are rendered as well as the invoice");

        var invoicePdf = await StoredInvoiceBytesAsync(invoiceId);

        // The Tamil name reached the page as Tamil. A face without the glyphs fails silently — it renders
        // the document and simply omits the letters — so this is asserted rather than left to the eye.
        string.Concat(PdfText.Words(invoicePdf))
            .ShouldContain("மீனா", Case.Sensitive, "the customer's Tamil name must render, not drop out");

        var directory = Path.Combine(RepositoryRoot(), "artifacts", "billing-document-samples");
        Directory.CreateDirectory(directory);

        await WriteAsync(directory, "invoice.pdf", invoicePdf);
        await WriteAsync(directory, "credit-note.pdf", await StoredNoteBytesAsync(invoiceId, (await NoteTotalsAsync(credit)).NoteId));
        await WriteAsync(directory, "debit-note.pdf", await StoredNoteBytesAsync(invoiceId, (await NoteTotalsAsync(debit)).NoteId));
    }

    /// <summary>Writes one sample and asserts it is a PDF with something in it.</summary>
    private static async Task WriteAsync(string directory, string name, byte[] bytes)
    {
        bytes.Length.ShouldBeGreaterThan(1024, $"{name} must be a rendered document rather than an empty file");
        await File.WriteAllBytesAsync(Path.Combine(directory, name), bytes, Token);
    }

    /// <summary>The repository root, found by the solution file above the test assembly.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HyFib.Tailor360.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root above " + AppContext.BaseDirectory);
    }

    /// <summary>Posts a draft of <paramref name="reference"/>, renders it, and answers the invoice.</summary>
    private async Task<Guid> PostedInvoiceAsync(AdministrationHarness.AdministratorClient cashier, Guid orderId, string reference)
    {
        var (invoiceId, tag) = await DraftAsync(cashier, orderId, reference);
        var posted = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(Token));

        await DispatchAsync(fixture);
        (await RenderPendingAsync()).ShouldBeGreaterThanOrEqualTo(1, "posting requests the artefact and the worker's pass renders it");
        return invoiceId;
    }

    /// <summary>Renders every pending artefact, as the worker's hosted job does.</summary>
    private async Task<int> RenderPendingAsync()
    {
        var completed = 0;
        for (var pass = 0; pass < 40; pass++)
        {
            using var scope = fixture.Services.CreateScope();
            var rendered = await scope.ServiceProvider.GetRequiredService<DocumentArtifactHandler>().RenderPendingAsync(cancellationToken: Token);
            completed += rendered;
            if (rendered == 0)
            {
                break;
            }
        }

        return completed;
    }

    /// <summary>
    /// The bytes actually stored for an invoice.
    /// </summary>
    /// <remarks>
    /// The success assertion is load-bearing, not ceremony. An artefact that was never rendered answers
    /// <c>billing.document-not-available</c>, and a test that treated that as "nothing to compare" would report
    /// green on a broken render pipeline — the assertion below is what makes a missing document a failure.
    /// </remarks>
    private async Task<byte[]> StoredInvoiceBytesAsync(Guid invoiceId)
        => await BytesAsync(handler => handler.OpenInvoiceAsync(invoiceId, SessionTestData.OrganisationId, null, Token));

    /// <summary>The bytes actually stored for one note of an invoice, on the same terms.</summary>
    private async Task<byte[]> StoredNoteBytesAsync(Guid invoiceId, Guid noteId)
        => await BytesAsync(handler => handler.OpenNoteAsync(invoiceId, noteId, SessionTestData.OrganisationId, null, Token));

    private async Task<byte[]> BytesAsync(Func<DocumentArtifactHandler, Task<Result<StoredDocument>>> open)
    {
        using var scope = fixture.Services.CreateScope();
        var opened = await open(scope.ServiceProvider.GetRequiredService<DocumentArtifactHandler>());
        opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Error.Code : "the document must have been rendered and stored");

        await using var content = opened.Value.Content;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, Token);

        var bytes = buffer.ToArray();
        bytes[..5].ShouldBe("%PDF-"u8.ToArray(), "what was stored is a PDF");
        return bytes;
    }

    /// <summary>
    /// Asserts the figure printed beside <paramref name="label"/>, including its absence.
    /// </summary>
    /// <remarks>
    /// The template omits a zero amount unless the row is emphasised, so "absent" and "zero" are the same
    /// statement about the same figure and both have to be asserted — a test that only looked for printed
    /// amounts would pass a page that had silently dropped a row it should have shown.
    /// </remarks>
    /// <param name="lines">The page's text lines.</param>
    /// <param name="label">The label the template prints.</param>
    /// <param name="expected">What the stored figure says.</param>
    /// <param name="alwaysPrinted">Whether the template emphasises the row and so prints it even at zero.</param>
    private static void AssertTotal(IReadOnlyList<string> lines, string label, decimal expected, bool alwaysPrinted = false)
    {
        var printed = PdfText.AmountBeside(lines, label);

        if (expected == 0m && !alwaysPrinted)
        {
            printed.ShouldBeNull($"{label} is zero in the snapshot, and the template omits a zero row it does not emphasise");
            return;
        }

        printed.ShouldNotBeNull($"{label} is {expected} in the snapshot and must be printed");
        printed.Value.ShouldBe(expected, $"{label}: the page prints {printed.Value} and the snapshot holds {expected}");
    }

    /// <summary>Every tax component the page prints, in the template's own per-line shape.</summary>
    private static List<TaxComponentOnPage> PrintedTaxComponents(IReadOnlyList<string> lines)
        => [.. lines
            .SelectMany(line => TaxComponent().Matches(line).Cast<Match>())
            .Select(match => new TaxComponentOnPage(
                match.Groups[1].Value,
                decimal.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                PdfText.Amount(match.Groups[3].Value)))];

    /// <summary>Every tax component the snapshot holds, in the same shape, to compare the two as multisets.</summary>
    private static List<TaxComponentOnPage> ExpectedTaxComponents(IReadOnlyList<PricedLine> lines)
        => [.. lines
            .SelectMany(line => line.Taxes)
            .Select(tax => new TaxComponentOnPage(tax.Kind, tax.RatePercent, tax.Amount.Amount))];

    /// <summary>The totals of a posted note, read from the answer the route gave.</summary>
    private static async Task<NoteTotals> NoteTotalsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var root = body.RootElement;
        var totals = root.GetProperty("totals");
        decimal Amount(string name) => totals.GetProperty(name).GetDecimal();

        return new NoteTotals(
            root.GetProperty("noteId").GetGuid(),
            Amount("taxableValue"),
            Amount("centralTax"),
            Amount("stateTax"),
            Amount("integratedTax"),
            Amount("cess"),
            Amount("grandTotal"));
    }

    /// <summary>
    /// A tax component as the template prints one beneath a table row: <c>CGST 2.5% ₹13.50</c>.
    /// </summary>
    /// <remarks>
    /// Matched inside a text line rather than anchored to one, because a row's description column shares its
    /// baseline with the amount columns beside it and the extracted line therefore carries both.
    /// </remarks>
    [GeneratedRegex(@"\b(CGST|SGST|IGST|CESS)\s+([0-9]+(?:\.[0-9]{1,2})?)%\s+(-?₹[0-9,]+\.[0-9]{2})", RegexOptions.CultureInvariant)]
    private static partial Regex TaxComponent();

    /// <summary>One tax component, from the page or from the snapshot, compared by value.</summary>
    /// <param name="Kind">CGST, SGST, IGST or CESS.</param>
    /// <param name="RatePercent">The rate applied.</param>
    /// <param name="Amount">The amount, to the paisa.</param>
    private sealed record TaxComponentOnPage(string Kind, decimal RatePercent, decimal Amount);

    /// <summary>The figures a posted note answered with.</summary>
    /// <param name="NoteId">The note.</param>
    /// <param name="TaxableValue">What it moves.</param>
    /// <param name="CentralTax">CGST on that.</param>
    /// <param name="StateTax">SGST on that.</param>
    /// <param name="IntegratedTax">IGST on that.</param>
    /// <param name="Cess">Cess on that.</param>
    /// <param name="GrandTotal">The note's total.</param>
    private sealed record NoteTotals(
        Guid NoteId, decimal TaxableValue, decimal CentralTax, decimal StateTax, decimal IntegratedTax, decimal Cess, decimal GrandTotal);
}
