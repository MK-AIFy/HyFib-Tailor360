using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// The subject-access export over HTTP: who may take one, what is in it, what is recorded about it,
/// and how the copy stops existing.
/// </summary>
/// <remarks>
/// <para>
/// Integration tests because the interesting parts are all outside the process. <c>customers.export</c>
/// is declared <c>RequiresMfa</c> and <c>RequiresReason</c>, which only the real host evaluates; the
/// check constraints that keep a purged row consistent are in the database; the audit entries are
/// written to another context; and the whole point of the download route is that it re-authorises,
/// which a direct handler call would skip.
/// </para>
/// <para>
/// The rules under test are <c>docs/nfr/data-classification.md</c> section 9 — an export is
/// purpose-bound, marked, audited, expiring and streamed rather than linked — and section 10, which
/// puts an export download among the reads that must be audited explicitly.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CustomerExportEndpointTests(WebApplicationFixture fixture)
{
    // A branch of this class's own, for the reason CustomerMergeEndpointTests states: BranchAsync
    // creates a branch only when it is missing, so sharing one would give these records customer
    // numbers carrying another class's branch code.
    private static readonly Guid BranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000e5");

    private const string BranchCode = "EXP1";

    private const string Reason = "Subject-access request received at the counter and verified against "
        + "the number on file.";

    /// <summary>
    /// What an exporting account holds: the counter permissions, the export, and organisation reach.
    /// </summary>
    /// <remarks>
    /// The reach is the part worth naming. <c>customers.export</c> is declared
    /// <c>PermissionScope.Organisation</c> and the endpoint demands <c>BranchScope.Organisation</c>,
    /// which <c>BranchScopeAuthorisationHandler</c> satisfies from
    /// <c>PlatformPermissions.ReadAllBranches</c> and from nothing else — not from the role's declared
    /// reach. So holding <c>customers.export</c> alone is refused with 403 before the handler is
    /// reached, which <see cref="AnAccountWithoutOrganisationReachCannotExport"/> asserts on purpose.
    /// A subject-access request is answered for the organisation rather than for the counter somebody
    /// happens to be standing at, which is why the two travel together.
    /// </remarks>
    private static readonly string[] Exporter =
    [
        .. CustomerHarness.Reception,
        CustomersPermissions.Export,
        PlatformPermissions.ReadAllBranches,

        // For the merged-away case below, which has to create the merge before it can export across it.
        CustomersPermissions.Merge,
    ];

    /// <summary>
    /// One token per run, folded into every name these tests create, so the suite survives being run
    /// twice against a database that is not dropped in between (CLAUDE.md section 5).
    /// </summary>
    private static readonly string RunToken = AdministrationHarness.UniqueToken(8);

    /// <summary>
    /// The whole journey: generate, read the receipt, download the document, and find both the
    /// generation and the read in the trail.
    /// </summary>
    [Fact]
    public async Task AnExportIsGeneratedDownloadedAndAuditedTwice()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-happy", "203.0.113.220");
        var customerId = await SubjectAsync(exporter, "exp-happy");

        var receipt = await GenerateAsync(exporter, customerId);

        receipt.CustomerId.ShouldBe(customerId);
        receipt.DocumentCode.ShouldBe("customers.subject-access-export");
        receipt.Classification.ShouldBe("Personal");
        receipt.ByteCount.ShouldBeGreaterThan(0);
        receipt.ExpiresAt.ShouldBeGreaterThan(receipt.GeneratedAt);
        receipt.SupersededCount.ShouldBe(0);

        // The receipt is a receipt: it says nothing about the person it is about.
        var receiptBody = JsonSerializer.Serialize(receipt);
        receiptBody.ShouldNotContain("example.invalid");
        receiptBody.ShouldNotContain("+919000");

        var download = await exporter.GetAsync(
            $"/api/v1/customers/{customerId}/exports/{receipt.ExportId}");

        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        // The filename is the export's identity and nothing else. A name or a customer number here
        // would put personal data in a header and in a downloads folder (CLAUDE.md rule 8).
        var disposition = download.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        FileNameOf(disposition).ShouldBe($"customers.subject-access-export-{receipt.ExportId}.json");

        var trail = await TrailAsync(customerId);

        trail.ShouldContain(entry => entry.Action == CustomerExportHandler.GeneratedAction);
        trail.ShouldContain(entry => entry.Action == CustomerExportHandler.DownloadedAction);

        // Section 10 wants the read audited, not merely the generation. Asserting both actions is the
        // only way to tell a slice that audits the read from one that audits the write twice.
        var generated = trail.Single(entry => entry.Action == CustomerExportHandler.GeneratedAction);
        generated.Reason.ShouldBe(Reason);
    }

    /// <summary>The document holds what section 5.2 and section 5.3 say belongs in a subject export.</summary>
    [Fact]
    public async Task TheDocumentCarriesTheProfileTheConsentHistoryAndItsOwnMarking()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-body", "203.0.113.221");
        var customerId = await SubjectAsync(exporter, "exp-body");

        await CustomerHarness.SeededPurposesAsync(fixture);

        // Granted, then withdrawn, with the withdrawal later than the grant so the ordering the
        // document promises is actually being asserted rather than coincidentally satisfied.
        var granted = DateTimeOffset.UtcNow.AddDays(-2);

        await CustomerHarness.ConsentAsync(
            fixture, customerId, "marketing_messages", ConsentDecision.Granted, granted);
        await CustomerHarness.ConsentAsync(
            fixture, customerId, "marketing_messages", ConsentDecision.Withdrawn, granted.AddDays(1));

        var receipt = await GenerateAsync(exporter, customerId);
        var document = await DocumentAsync(exporter, customerId, receipt.ExportId);

        var root = document.RootElement;

        // Marked, as section 9 requires: what it is, how it is classified, who made it and when.
        var header = root.GetProperty("document");
        header.GetProperty("code").GetString().ShouldBe("customers.subject-access-export");
        header.GetProperty("classification").GetString().ShouldBe("Personal");
        header.GetProperty("exportId").GetGuid().ShouldBe(receipt.ExportId);
        header.GetProperty("generatedByUserId").ValueKind.ShouldNotBe(JsonValueKind.Null);
        header.GetProperty("branchScope").GetString().ShouldBe("organisation");

        var profile = root.GetProperty("profile");
        profile.GetProperty("customerId").GetGuid().ShouldBe(customerId);
        profile.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        profile.GetProperty("phone").GetString().ShouldStartWith("+91");

        // The full history, not the current answer. A withdrawal after a grant is two records, and an
        // export that showed only the second would be answering a different question.
        var consent = root.GetProperty("consentHistory").EnumerateArray().ToList();
        consent.Count.ShouldBe(2);
        consent[0].GetProperty("decision").GetString().ShouldBe("Granted");
        consent[1].GetProperty("decision").GetString().ShouldBe("Withdrawn");

        // Honest rather than empty: the system records no measurements at all yet, which is a
        // different statement from "this person has none".
        var measurements = root.GetProperty("measurements");
        measurements.GetProperty("held").GetBoolean().ShouldBeFalse();
        measurements.GetProperty("note").GetString().ShouldNotBeNullOrWhiteSpace();

        // Purpose-bound: what section 5.2.1 says never leaves Customers stays out.
        var raw = document.RootElement.GetRawText();
        raw.ShouldNotContain("duplicate");
        raw.ShouldNotContain("mergeReason");
    }

    /// <summary>Generating a second export destroys the first, so one copy exists at a time.</summary>
    [Fact]
    public async Task GeneratingAnExportDestroysTheEarlierCopy()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-supersede", "203.0.113.222");
        var customerId = await SubjectAsync(exporter, "exp-supersede");

        var first = await GenerateAsync(exporter, customerId);
        var second = await GenerateAsync(exporter, customerId);

        second.SupersededCount.ShouldBe(1);

        // The first is gone as data and still there as evidence.
        var stale = await exporter.GetAsync($"/api/v1/customers/{customerId}/exports/{first.ExportId}");

        stale.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthenticationClient.CodeAsync(stale)).ShouldBe("customers.export-expired");

        var rows = await ExportRowsAsync(customerId);

        rows.Count.ShouldBe(2);

        var purged = rows.Single(row => row.Id == first.ExportId);
        purged.Document.ShouldBeNull();
        purged.PurgedAt.ShouldNotBeNull();
        purged.PurgeReason.ShouldBe(CustomerExportPurgeReason.Superseded);
        purged.Reason.ShouldBe(Reason);
        purged.ByteCount.ShouldBeGreaterThan(0);

        // And the newest one still works.
        (await exporter.GetAsync($"/api/v1/customers/{customerId}/exports/{second.ExportId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>An expired export is refused and its copy is destroyed by the cleanup pass.</summary>
    /// <remarks>
    /// The expiry is moved in the database rather than by waiting, because the alternative is a test
    /// that sleeps for the configured lifetime, and a test nobody will run is not a test.
    /// </remarks>
    [Fact]
    public async Task AnExpiredExportIsRefusedAndItsCopyIsDestroyed()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-expiry", "203.0.113.223");
        var customerId = await SubjectAsync(exporter, "exp-expiry");

        var receipt = await GenerateAsync(exporter, customerId);

        await ExpireAsync(receipt.ExportId);

        // Refused before the cleanup job has run: the endpoint does not serve an expired copy even
        // while one is still sitting in the table.
        var refused = await exporter.GetAsync(
            $"/api/v1/customers/{customerId}/exports/{receipt.ExportId}");

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("customers.export-expired");

        // But refusing to serve it is not the same as not holding it, which is what the job is for.
        (await ExportRowsAsync(customerId)).Single().Document.ShouldNotBeNull();

        var purged = await PurgeAsync();

        purged.ShouldBeGreaterThanOrEqualTo(1);

        var row = (await ExportRowsAsync(customerId)).Single();

        row.Document.ShouldBeNull();
        row.PurgedAt.ShouldNotBeNull();
        row.PurgeReason.ShouldBe(CustomerExportPurgeReason.Expired);

        // The evidence survives the copy, and the destruction is itself recorded.
        row.Reason.ShouldBe(Reason);
        (await TrailAsync(customerId))
            .ShouldContain(entry => entry.Action == CustomerExportHandler.PurgedAction);

        // Running the job again over the same row changes nothing.
        (await PurgeAsync()).ShouldBe(0);
    }

    /// <summary>
    /// An export identifier belonging to a different customer is answered as one that does not exist.
    /// </summary>
    /// <remarks>
    /// The <c>identifier-editing</c> case in <c>matrix.yaml</c> for this route. The identifier names a
    /// file holding one person's name, number, address and consent history, so a distinguishable
    /// refusal would let a caller pair a real export identifier with each customer in turn and learn
    /// who has recently been the subject of a request.
    /// </remarks>
    [Fact]
    public async Task AnExportOfAnotherCustomerIsAnsweredAsOneThatDoesNotExist()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-idor", "203.0.113.224");

        var subject = await SubjectAsync(exporter, "exp-idor-a");
        var other = await SubjectAsync(exporter, "exp-idor-b");

        var receipt = await GenerateAsync(exporter, subject);

        // A real export identifier, asked for under the wrong customer.
        var mismatched = await exporter.GetAsync(
            $"/api/v1/customers/{other}/exports/{receipt.ExportId}");

        // An identifier belonging to no export at all.
        var invented = await exporter.GetAsync(
            $"/api/v1/customers/{other}/exports/{Guid.CreateVersion7()}");

        mismatched.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        mismatched.StatusCode.ShouldBe(invented.StatusCode);
        (await AuthenticationClient.CodeAsync(mismatched)).ShouldBe("customers.export-not-found");
        (await AuthenticationClient.CodeAsync(mismatched))
            .ShouldBe(await AuthenticationClient.CodeAsync(invented));

        // And the export is still readable where it belongs, so the refusal was about the pairing and
        // not about the export having been spent.
        (await exporter.GetAsync($"/api/v1/customers/{subject}/exports/{receipt.ExportId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// An export asked for under a merged-away identifier answers with the surviving record, and says
    /// that it followed a merge.
    /// </summary>
    /// <remarks>
    /// A folded record keeps answering to its old number — the merge leaves it searchable as an alias
    /// on the survivor — so somebody exercising a subject-access right will quote whichever number they
    /// were given, which may well be the old one. Reading the folded record directly would produce a
    /// document that is not a refusal and looks complete, and holds a name, a pointer, and none of the
    /// person's consent history. That is the worst answer available, which is why this is asserted
    /// rather than left to the reader of <c>ExportStore</c>.
    /// </remarks>
    [Fact]
    public async Task AnExportOfAMergedAwayRecordAnswersWithTheSurvivor()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-merged", "203.0.113.236");

        var survivor = await SubjectAsync(exporter, "exp-merged-s");
        var folded = await SubjectAsync(exporter, "exp-merged-f");

        await CustomerHarness.SeededPurposesAsync(fixture);
        await CustomerHarness.ConsentAsync(
            fixture, survivor, "marketing_messages", ConsentDecision.Granted,
            DateTimeOffset.UtcNow.AddDays(-1));

        await MergeAsync(exporter, survivor, folded);

        // Asked for under the identifier that no longer holds anything.
        var receipt = await GenerateAsync(exporter, folded);

        receipt.CustomerId.ShouldBe(survivor);

        var document = await DocumentAsync(exporter, survivor, receipt.ExportId);
        var header = document.RootElement.GetProperty("document");

        header.GetProperty("requestedCustomerId").GetGuid().ShouldBe(folded);
        header.GetProperty("followedAMerge").GetBoolean().ShouldBeTrue();

        // The survivor's record, with the survivor's consent history — not the shell.
        document.RootElement.GetProperty("profile").GetProperty("customerId").GetGuid()
            .ShouldBe(survivor);
        document.RootElement.GetProperty("consentHistory").GetArrayLength().ShouldBe(1);
    }

    /// <summary>A caller without the permission is refused, whatever else they hold.</summary>
    [Fact]
    public async Task ACounterAccountCannotExportACustomer()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-denied-e", "203.0.113.225");
        var customerId = await SubjectAsync(exporter, "exp-denied");

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);

        // Everything Reception holds — which is every customer permission except the export itself.
        using var reception = await CustomerHarness.CounterAsync(
            fixture, "exp-denied-r", "203.0.113.226", BranchId, CustomerHarness.Reception);

        var refused = await reception.PostAsync(
            $"/api/v1/customers/{customerId}/export",
            new { reason = Reason },
            Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And nothing was generated on the way to being refused.
        (await ExportRowsAsync(customerId)).ShouldBeEmpty();
    }

    /// <summary>
    /// A session that has only presented a password cannot export, because the permission demands a
    /// second factor.
    /// </summary>
    /// <remarks>
    /// The refusal is <c>security.sign-in-incomplete</c> rather than
    /// <c>security.second-factor-required</c>, and the two are not interchangeable. This account has no
    /// factor enrolled at all, so its session never finished signing in; the second-factor answer is
    /// for a session that could satisfy the demand and has not yet. Asserting the exact code is what
    /// keeps this test about the gate it names.
    /// </remarks>
    [Fact]
    public async Task ASessionWithoutASecondFactorCannotExport()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-mfa-e", "203.0.113.227");
        var customerId = await SubjectAsync(exporter, "exp-mfa");

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);

        // The same permissions, but opened through CounterAsync, which enrols no second factor.
        using var unenrolled = await CustomerHarness.CounterAsync(
            fixture, "exp-mfa-u", "203.0.113.228", BranchId, Exporter);

        var refused = await unenrolled.PostAsync(
            $"/api/v1/customers/{customerId}/export",
            new { reason = Reason },
            Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("security.sign-in-incomplete");

        (await ExportRowsAsync(customerId)).ShouldBeEmpty();
    }

    /// <summary>An export without a reason is refused, because the permission demands one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnExportWithoutAReasonIsRefused(string? reason)
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync($"exp-reason{Slug(reason)}", $"203.0.113.{229 + Row(reason)}");
        var customerId = await SubjectAsync(exporter, $"exp-reason{Slug(reason)}");

        var refused = await exporter.PostAsync(
            $"/api/v1/customers/{customerId}/export",
            new { reason },
            Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await ExportRowsAsync(customerId)).ShouldBeEmpty();
    }

    /// <summary>A customer of another organisation is not acknowledged.</summary>
    [Fact]
    public async Task ACustomerOfAnotherOrganisationCannotBeExported()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-tenant", "203.0.113.233");

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        var elsewhere = await CustomerHarness.CustomerOfAnotherOrganisationAsync(fixture, BranchId);

        var refused = await exporter.PostAsync(
            $"/api/v1/customers/{elsewhere}/export",
            new { reason = Reason },
            Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("customers.customer-not-found");
    }

    /// <summary>
    /// Holding <c>customers.export</c> is not enough without organisation reach.
    /// </summary>
    /// <remarks>
    /// The distinction this asserts is the one that makes an organisation-scoped permission mean
    /// anything. A subject-access request is answered for the organisation, not for the counter the
    /// person happens to be standing at, so an account that can only reach its own branch is refused —
    /// with 403 before the handler runs, not with a 404 that would imply the record was the problem.
    /// The account here differs from an exporting one by exactly one permission.
    /// </remarks>
    [Fact]
    public async Task AnAccountWithoutOrganisationReachCannotExport()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var exporter = await ExporterAsync("exp-reach-e", "203.0.113.234");
        var customerId = await SubjectAsync(exporter, "exp-reach");

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);

        string[] withoutReach = [.. CustomerHarness.Reception, CustomersPermissions.Export];

        using var narrow = await CustomerHarness.ManagerAsync(
            fixture, $"exp-reach-n-{RunToken}", "203.0.113.235", BranchId, withoutReach);

        var refused = await narrow.PostAsync(
            $"/api/v1/customers/{customerId}/export",
            new { reason = Reason },
            Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ExportRowsAsync(customerId)).ShouldBeEmpty();
    }

    /// <summary>
    /// An account that may take an export: the counter permissions plus <c>customers.export</c>, a
    /// second factor enrolled, and <b>organisation reach</b>.
    /// </summary>
    /// <remarks>
    /// The reach is the part worth stating. <c>customers.export</c> is declared
    /// <c>PermissionScope.Organisation</c> and the endpoint demands <c>BranchScope.Organisation</c>,
    /// so an account holding the permission without organisation reach is refused before the handler is
    /// reached — which <see cref="AnAccountWithoutOrganisationReachCannotExport"/> asserts on purpose.
    /// </remarks>
    private async Task<AuthenticationClient> ExporterAsync(string prefix, string clientAddress)
    {
        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);

        return await CustomerHarness.ManagerAsync(
            fixture, $"{prefix}-{RunToken}", clientAddress, BranchId, Exporter);
    }

    /// <summary>
    /// A customer for one test to export, distinct enough not to be anybody else's duplicate.
    /// </summary>
    /// <remarks>
    /// Every field the duplicate scorer looks at carries a per-subject token as well as the per-run
    /// one. These tests are not about deduplication, and two subjects sharing a native name and a
    /// postcode is exactly the pair the check is built to refuse — which it did, turning every test in
    /// this class red for a reason that had nothing to do with exports.
    /// </remarks>
    private static async Task<Guid> SubjectAsync(AuthenticationClient client, string prefix)
    {
        var token = AdministrationHarness.UniqueToken(6);

        var created = await client.PostAsync(
            "/api/v1/customers",
            new
            {
                displayName = $"Lakshmi {prefix} {token} {RunToken}",
                nativeName = $"லட்சுமி {token}",
                phone = CustomerHarness.UniquePhone(),
                email = $"{prefix}.{token}.{RunToken}@example.invalid",
                addressLine = $"{token} Avinashi Road",
                locality = $"Peelamedu {token}",
                postcode = Postcode(token),
                language = "ta-IN",

                // These subjects are deliberately new people, and saying so is what the flag is for.
                // Every one of them still differs in name, native name, address and postcode; the flag
                // is the belt to that pair of braces, so that a future scoring change cannot turn this
                // class red for a reason that has nothing to do with exports.
                duplicatesReviewed = true,
            },
            Key());

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            $"creating the subject answered {(int)created.StatusCode} with "
            + await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var body = (await AuthenticationClient.ReadAsync<CreatedCustomer>(created)).ShouldNotBeNull();

        return body.CustomerId;
    }

    private static async Task<ExportReceiptBody> GenerateAsync(
        AuthenticationClient client,
        Guid customerId)
    {
        var response = await client.PostAsync(
            $"/api/v1/customers/{customerId}/export",
            new { reason = Reason },
            Key());

        // The body on a refusal, not only the status: a 403 and a 404 are the same number of
        // characters to read and completely different problems to chase.
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            $"generating an export answered {(int)response.StatusCode} with "
            + await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await AuthenticationClient.ReadAsync<ExportReceiptBody>(response)).ShouldNotBeNull();
    }

    private static async Task<JsonDocument> DocumentAsync(
        AuthenticationClient client,
        Guid customerId,
        Guid exportId)
    {
        var response = await client.GetAsync($"/api/v1/customers/{customerId}/exports/{exportId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private async Task<IReadOnlyList<ExportRow>> ExportRowsAsync(Guid customerId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();

        return
        [
            .. await context.CustomerExports
                .AsNoTracking()
                .Where(export => export.CustomerId == customerId)
                .OrderBy(export => export.GeneratedAt)
                .Select(export => new ExportRow(
                    export.Id,
                    export.Document,
                    export.ByteCount,
                    export.Reason,
                    export.PurgedAt,
                    export.PurgeReason))
                .ToListAsync(TestContext.Current.CancellationToken),
        ];
    }

    private async Task ExpireAsync(Guid exportId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();

        var export = await context.CustomerExports
            .FirstOrDefaultAsync(row => row.Id == exportId, TestContext.Current.CancellationToken);

        export.ShouldNotBeNull();

        // Straight to the column. The lifetime is bounded by the domain and the check constraint keeps
        // expires_at after generated_at, so both timestamps move together.
        await context.Database.ExecuteSqlAsync(
            $"""
             UPDATE customers.customer_exports
             SET generated_at = now() - interval '3 days',
                 expires_at = now() - interval '1 day'
             WHERE id = {exportId}
             """,
            TestContext.Current.CancellationToken);
    }

    private async Task<int> PurgeAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CustomerExportHandler>();

        var result = await handler.PurgeExpiredAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        return result.Value;
    }

    private async Task<IReadOnlyList<TrailRow>> TrailAsync(Guid customerId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return
        [
            .. await context.AuditEvents
                .AsNoTracking()
                .Where(entry => entry.EntityId == customerId)
                .OrderBy(entry => entry.OccurredAt)
                .Select(entry => new TrailRow(entry.Action, entry.Reason))
                .ToListAsync(TestContext.Current.CancellationToken),
        ];
    }

    private static string FileNameOf(ContentDispositionHeaderValue disposition)
        => (disposition.FileName ?? disposition.FileNameStar ?? string.Empty).Trim('"');

    private static async Task MergeAsync(AuthenticationClient client, Guid survivor, Guid folded)
    {
        var survivorRead = await client.GetAsync($"/api/v1/customers/{survivor}");
        var foldedRead = await client.GetAsync($"/api/v1/customers/{folded}");

        survivorRead.StatusCode.ShouldBe(HttpStatusCode.OK);
        foldedRead.StatusCode.ShouldBe(HttpStatusCode.OK);

        var survivorVersion = (await AuthenticationClient.ReadAsync<VersionedCustomer>(survivorRead))
            .ShouldNotBeNull().Version;
        var foldedVersion = (await AuthenticationClient.ReadAsync<VersionedCustomer>(foldedRead))
            .ShouldNotBeNull().Version;

        var merged = await client.PostAsync(
            $"/api/v1/customers/{survivor}/merge",
            new
            {
                mergedCustomerId = folded,
                mergedCustomerVersion = foldedVersion,
                reason = "One person with two records, confirmed at the counter.",
            },
            Key(),
            ("If-Match", $"\"{survivorVersion}\""));

        merged.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"merging answered {(int)merged.StatusCode} with "
            + await merged.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static (string, string) Key()
        => ("Idempotency-Key", Guid.CreateVersion7().ToString());

    /// <summary>A Coimbatore postcode that differs per subject, so no two share one.</summary>
    private static string Postcode(string token)
    {
        var digits = new string([.. token.Where(char.IsAsciiDigit)]);

        return "6410" + (digits.Length >= 2 ? digits[^2..] : digits.PadLeft(2, '4'));
    }

    private static string Slug(string? reason) => reason switch
    {
        null => "n",
        "" => "e",
        _ => "b",
    };

    private static int Row(string? reason) => reason switch
    {
        null => 0,
        "" => 1,
        _ => 2,
    };

    private sealed record CreatedCustomer(Guid CustomerId);

    private sealed record VersionedCustomer(Guid CustomerId, string Version);

    private sealed record ExportReceiptBody(
        Guid ExportId,
        Guid CustomerId,
        string DocumentCode,
        int DocumentVersion,
        string Classification,
        string ContentType,
        int ByteCount,
        DateTimeOffset GeneratedAt,
        DateTimeOffset ExpiresAt,
        int SupersededCount);

    private sealed record ExportRow(
        Guid Id,
        byte[]? Document,
        int ByteCount,
        string? Reason,
        DateTimeOffset? PurgedAt,
        CustomerExportPurgeReason? PurgeReason);

    private sealed record TrailRow(string Action, string? Reason);
}
