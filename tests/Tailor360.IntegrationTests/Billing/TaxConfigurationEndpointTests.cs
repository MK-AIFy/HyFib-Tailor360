using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The tax configuration and GST registration routes (#145) end to end: the round trip, publication and
/// what it freezes — at the database as well as at the API — the checks, the registrations and their
/// overlap, and deny-by-default over every route.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class TaxConfigurationEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();
    private static int _preconditionClientNumber;

    [Fact]
    public async Task AnAdministratorDraftsAVersionAddsCodesAndReadsThemBack()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("tax-add", "203.0.113.240");
        var version = await DraftAsync(owner, "Round trip");

        var added = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes",
            CodeBody(Code("STITCHING")),
            await VersionKeyAsync(owner, version));
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var code = JsonDocument.Parse(await added.Content.ReadAsStringAsync(Token));
        var codeId = code.RootElement.GetProperty("taxCodeId").GetGuid();
        code.RootElement.GetProperty("rates").EnumerateArray()
            .Select(rate => (rate.GetProperty("kind").GetString(), rate.GetProperty("ratePercent").GetDecimal()))
            .ShouldBe([("Cgst", 2.5m), ("Sgst", 2.5m), ("Igst", 5m)]);

        var edited = await owner.PutAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes/{codeId}",
            CodeBody(Code("STITCHING"), half: 6m),
            await VersionKeyAsync(owner, version));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var read = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/billing/tax-configuration/versions/{version}")).Content.ReadAsStringAsync(Token));
        var codes = read.RootElement.GetProperty("taxCodes").EnumerateArray().ToArray();
        codes.Length.ShouldBe(1);
        codes[0].GetProperty("classification").GetString().ShouldBe("998822");
        codes[0].GetProperty("rates").EnumerateArray().Last().GetProperty("ratePercent").GetDecimal().ShouldBe(12m);
        read.RootElement.GetProperty("version").GetProperty("status").GetString().ShouldBe("Draft");

        var listed = await owner.GetAsync("/api/v1/billing/tax-configuration/versions");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var list = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(Token));
        list.RootElement.EnumerateArray().ShouldContain(summary => summary.GetProperty("taxConfigurationVersionId").GetGuid() == version);

        (await owner.PostAsync(
                $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes/{codeId}/delete",
                new { reason = "Withdrawn." },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RefusesACodeCollisionAStaleTagAndAMalformedRate()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("tax-refuse", "203.0.113.241");
        var version = await DraftAsync(owner, "Refusals");
        var staleKey = await VersionKeyAsync(owner, version);
        await AddCodeAsync(owner, version, Code("BLOUSE"));

        var collided = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes",
            CodeBody(Code("BLOUSE")),
            await VersionKeyAsync(owner, version));
        collided.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await collided.Content.ReadAsStringAsync(Token)).ShouldContain("billing.code-not-unique");

        var stale = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes", CodeBody(Code("OTHER")), staleKey);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        var malformed = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes",
            new
            {
                code = Code("BAD"),
                description = "Bad",
                classification = "9988",
                kind = "Services",
                active = true,
                rates = new[] { new { kind = "Cgst", ratePercent = 101m } },
                reason = (string?)null,
            },
            await VersionKeyAsync(owner, version));
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await malformed.Content.ReadAsStringAsync(Token);
        problem.ShouldContain("billing.rate-out-of-range");
        problem.ShouldContain("rates[0].ratePercent");
    }

    [Fact]
    public async Task PublicationIsRefusedWhileAComponentPairIsIncompleteAndFreezesTheVersionOnceItPasses()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("tax-publish", "203.0.113.242");
        var version = await DraftAsync(owner, "Publication");
        var half = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes",
            new
            {
                code = Code("HALF"),
                description = "Half a pair",
                classification = "998822",
                kind = "Services",
                active = true,
                rates = new[] { new { kind = "Cgst", ratePercent = 2.5m } },
                reason = (string?)null,
            },
            await VersionKeyAsync(owner, version));
        half.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var halfBody = JsonDocument.Parse(await half.Content.ReadAsStringAsync(Token));
        var halfId = halfBody.RootElement.GetProperty("taxCodeId").GetGuid();

        var report = await owner.GetAsync($"/api/v1/billing/tax-configuration/versions/{version}/validation");
        report.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var reportBody = JsonDocument.Parse(await report.Content.ReadAsStringAsync(Token));
        reportBody.RootElement.GetProperty("canPublish").GetBoolean().ShouldBeFalse();
        reportBody.RootElement.GetProperty("findings").EnumerateArray()
            .Select(finding => finding.GetProperty("code").GetString())
            .ShouldContain("billing.intra-state-pair-incomplete");

        var refused = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/publish",
            new { reason = "Trying anyway." },
            await VersionKeyAsync(owner, version));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var refusal = await refused.Content.ReadAsStringAsync(Token);
        refusal.ShouldContain("billing.publish-validation-failed");
        refusal.ShouldContain($"taxCodes[{Code("HALF")}].rates");

        // Complete the pair, and publication succeeds; the version is then frozen by the API and the database.
        (await owner.PutAsync(
                $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes/{halfId}",
                CodeBody(Code("HALF")),
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var published = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/publish",
            new { reason = "Approved by the accountant." },
            await VersionKeyAsync(owner, version));
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));
        using var publication = JsonDocument.Parse(await published.Content.ReadAsStringAsync(Token));
        publication.RootElement.GetProperty("published").GetProperty("version").GetProperty("status").GetString().ShouldBe("Published");

        var frozen = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes",
            CodeBody(Code("LATE")),
            await VersionKeyAsync(owner, version));
        frozen.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await frozen.Content.ReadAsStringAsync(Token)).ShouldContain("billing.version-not-editable");

        // At the database: a rate change on a published code's component, an insert into its codes and
        // a delete of its components are each refused by the trigger, whoever asks.
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var tamper = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.tax_components SET rate_percent = 9 WHERE tax_code_id = {halfId}", Token));
        tamper.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        var insert = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO billing.tax_codes (id, tax_code_key, tax_configuration_version_id, organisation_id, code, description, classification, kind, active)
             VALUES ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {version}, {SessionTestData.OrganisationId}, 'SNEAKED', 'Sneaked in', '9988', 1, true)
             """, Token));
        insert.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        var delete = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM billing.tax_components WHERE tax_code_id = {halfId}", Token));
        delete.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        var unpublish = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.tax_configuration_versions SET status = 0, published_at = NULL WHERE id = {version}", Token));
        unpublish.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        var respell = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.tax_codes SET code = 'RESPELLED' WHERE id = {halfId}", Token));
        respell.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        var dropCode = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM billing.tax_codes WHERE id = {halfId}", Token));
        dropCode.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        // Moving a published code into a draft — the re-parenting that would take a row out of the
        // published version without touching the version row — is refused on the parent it leaves.
        var scratch = await DraftAsync(owner, "A draft to smuggle into");
        var reparent = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.tax_codes SET tax_configuration_version_id = {scratch} WHERE id = {halfId}", Token));
        reparent.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        // Publishing a clone supersedes it, and the retired version still reads exactly as it did.
        var clone = await DraftAsync(owner, "Successor", cloneFrom: version);
        var superseded = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{clone}/publish",
            new { reason = "The next year's list." },
            await VersionKeyAsync(owner, clone));
        superseded.StatusCode.ShouldBe(HttpStatusCode.OK, await superseded.Content.ReadAsStringAsync(Token));
        using var successor = JsonDocument.Parse(await superseded.Content.ReadAsStringAsync(Token));
        successor.RootElement.GetProperty("supersededVersionId").GetGuid().ShouldBe(version);
        using var retired = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/billing/tax-configuration/versions/{version}")).Content.ReadAsStringAsync(Token));
        retired.RootElement.GetProperty("version").GetProperty("status").GetString().ShouldBe("Retired");
        retired.RootElement.GetProperty("taxCodes").EnumerateArray().Single().GetProperty("code").GetString().ShouldBe(Code("HALF"));
    }

    [Fact]
    public async Task RecordsARegistrationAndRefusesAnOverlappingOneAndAMalformedGstin()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("gst-add", "203.0.113.243");
        // A branch of this run's own, so registrations left by earlier runs cannot overlap.
        var branch = Guid.CreateVersion7();

        var recorded = await owner.PostAsync(
            "/api/v1/billing/gst-registrations",
            RegistrationBody(branch, from: new DateOnly(2026, 4, 1), to: new DateOnly(2027, 3, 31)),
            Key());
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Token));
        recorded.Headers.ETag.ShouldNotBeNull();
        using var registration = JsonDocument.Parse(await recorded.Content.ReadAsStringAsync(Token));
        var registrationId = registration.RootElement.GetProperty("gstRegistrationId").GetGuid();
        registration.RootElement.GetProperty("gstin").GetString().ShouldBe("33AAACH7409R1Z8");

        var overlapping = await owner.PostAsync(
            "/api/v1/billing/gst-registrations",
            RegistrationBody(branch, from: new DateOnly(2027, 3, 31), to: null),
            Key());
        overlapping.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await overlapping.Content.ReadAsStringAsync(Token)).ShouldContain("billing.registration-overlaps");

        var adjacent = await owner.PostAsync(
            "/api/v1/billing/gst-registrations",
            RegistrationBody(branch, from: new DateOnly(2027, 4, 1), to: null),
            Key());
        adjacent.StatusCode.ShouldBe(HttpStatusCode.Created, await adjacent.Content.ReadAsStringAsync(Token));

        var malformed = await owner.PostAsync(
            "/api/v1/billing/gst-registrations",
            RegistrationBody(Guid.CreateVersion7(), from: new DateOnly(2026, 4, 1), to: null, gstin: "33AAACH7409R1ZW"),
            Key());
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await malformed.Content.ReadAsStringAsync(Token)).ShouldContain("billing.gstin-not-well-formed");

        // Amending the first to run into the second is the same conflict; ending it earlier is fine.
        var read = await owner.GetAsync($"/api/v1/billing/gst-registrations/{registrationId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tag = read.Headers.ETag!.ToString();
        var collided = await owner.PutAsync(
            $"/api/v1/billing/gst-registrations/{registrationId}",
            RegistrationBody(branch, from: new DateOnly(2026, 4, 1), to: new DateOnly(2027, 4, 1)),
            [.. Key(), ("If-Match", tag)]);
        collided.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var shortened = await owner.PutAsync(
            $"/api/v1/billing/gst-registrations/{registrationId}",
            RegistrationBody(branch, from: new DateOnly(2026, 4, 1), to: new DateOnly(2026, 12, 31)),
            [.. Key(), ("If-Match", tag)]);
        shortened.StatusCode.ShouldBe(HttpStatusCode.OK, await shortened.Content.ReadAsStringAsync(Token));

        using var listed = JsonDocument.Parse(
            await (await owner.GetAsync("/api/v1/billing/gst-registrations")).Content.ReadAsStringAsync(Token));
        listed.RootElement.EnumerateArray().Count(row => row.GetProperty("branchId").GetGuid() == branch).ShouldBe(2);
    }

    [Fact]
    public async Task ADraftOlderThanThePublishedVersionStillSupersedesIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The draft is created first, so its identifier sorts before the version that is published
        // meanwhile; when it is published the two updates run in identifier order, the draft's first.
        // The one-published constraint is judged at commit, so the order does not matter.
        using var owner = await OwnerAsync("tax-order", "203.0.113.246");
        var older = await DraftAsync(owner, "Drafted first, published second");
        await AddCodeAsync(owner, older, Code("OLDER"));
        var newer = await DraftAsync(owner, "Drafted second, published first");
        await AddCodeAsync(owner, newer, Code("NEWER"));

        var first = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{newer}/publish",
            new { reason = "Published first." },
            await VersionKeyAsync(owner, newer));
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Token));

        var second = await owner.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{older}/publish",
            new { reason = "Published second." },
            await VersionKeyAsync(owner, older));
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync(Token));
        using var publication = JsonDocument.Parse(await second.Content.ReadAsStringAsync(Token));
        publication.RootElement.GetProperty("supersededVersionId").GetGuid().ShouldBe(newer);
    }

    [Fact]
    public async Task RefusesToPublishWithOnlyTheDraftingPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await AdministrationHarness.AdministratorAsync(
            fixture, "tax-drafter", "203.0.113.247", BillingPermissions.ManagePriceLists);
        var version = await DraftAsync(drafter, "Drafted by somebody who may not publish");
        await AddCodeAsync(drafter, version, Code("DRAFTER"));

        (await drafter.PostAsync(
                $"/api/v1/billing/tax-configuration/versions/{version}/publish",
                new { reason = "Not mine to do." },
                await VersionKeyAsync(drafter, version)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("describe")]
    [InlineData("add-code")]
    [InlineData("edit-code")]
    [InlineData("delete-code")]
    [InlineData("publish")]
    public async Task EveryVersionCommandRequiresTheTagTheAdministratorRead(string operation)
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("tax-precondition", $"2001:db8:91::{Interlocked.Increment(ref _preconditionClientNumber):x}");
        var version = await DraftAsync(owner, $"Preconditions {operation}");
        var codeId = await AddCodeAsync(owner, version, Code($"PRE_{operation.Replace('-', '_').ToUpperInvariant()}"));
        var root = $"/api/v1/billing/tax-configuration/versions/{version}";

        // Capture the first screen's tag, then let a second screen move the version.
        var stale = await VersionKeyAsync(owner, version);
        (await owner.PutAsync(
                root,
                new { name = "Moved by a colleague", notes = (string?)null, effectiveFrom = "2026-04-01", reason = (string?)null },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = operation switch
        {
            "describe" => await owner.PutAsync(root, new { name = "Stale", notes = (string?)null, effectiveFrom = "2026-04-01", reason = (string?)null }, stale),
            "add-code" => await owner.PostAsync($"{root}/tax-codes", CodeBody(Code("STALE")), stale),
            "edit-code" => await owner.PutAsync($"{root}/tax-codes/{codeId}", CodeBody(Code("STALE")), stale),
            "delete-code" => await owner.PostAsync($"{root}/tax-codes/{codeId}/delete", new { reason = "Stale." }, stale),
            _ => await owner.PostAsync($"{root}/publish", new { reason = "Stale." }, stale),
        };
        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, $"{operation}: {await response.Content.ReadAsStringAsync(Token)}");

        // And with no tag at all, the request is not even considered.
        var untagged = operation switch
        {
            "describe" => await owner.PutAsync(root, new { name = "Untagged", notes = (string?)null, effectiveFrom = "2026-04-01", reason = (string?)null }, Key()),
            "add-code" => await owner.PostAsync($"{root}/tax-codes", CodeBody(Code("UNTAGGED")), Key()),
            "edit-code" => await owner.PutAsync($"{root}/tax-codes/{codeId}", CodeBody(Code("UNTAGGED")), Key()),
            "delete-code" => await owner.PostAsync($"{root}/tax-codes/{codeId}/delete", new { reason = "Untagged." }, Key()),
            _ => await owner.PostAsync($"{root}/publish", new { reason = "Untagged." }, Key()),
        };
        untagged.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, operation);
    }

    [Fact]
    public async Task RefusesASecondDraftThatTookTheSameVersionNumber()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITaxConfigurationStore>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var number = await store.NextVersionNumberAsync(SessionTestData.OrganisationId, Token);

        // Two administrators starting a draft in the same moment both read this maximum. Both drafts
        // are added to one context because that is the deterministic way to make the index fire; the
        // loser is answered a conflict rather than the five hundred an uncaught exception would be.
        foreach (var name in new[] { "First past the post", "Second past the post" })
        {
            var draft = TaxConfigurationVersion.CreateDraft(
                ids.NewId(), SessionTestData.OrganisationId, number, name, null, new DateOnly(2026, 4, 1), clock.UtcNow, null);
            draft.IsSuccess.ShouldBeTrue();
            store.Add(draft.Value);
        }

        var saved = await store.SaveDraftAsync(Token);
        saved.IsFailure.ShouldBeTrue();
        saved.Error.Code.ShouldBe("billing.draft-number-conflict");
    }

    [Fact]
    public async Task TheDatabaseRefusesOverlappingRegistrationsThatBypassTheHandler()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("gst-exclude", "203.0.113.248");
        var branch = Guid.CreateVersion7();
        var recorded = await owner.PostAsync(
            "/api/v1/billing/gst-registrations",
            RegistrationBody(branch, from: new DateOnly(2026, 4, 1), to: null),
            Key());
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Token));

        // A second row written straight to the table, as a repair script or a lost race would: the
        // exclusion constraint, not the handler's read, is what refuses it.
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var overlapping = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO billing.gst_registrations
                 (id, organisation_id, branch_id, gstin, state_code, legal_name, trade_name, effective_from, effective_to,
                  created_at, created_by, updated_at, updated_by)
             VALUES ({Guid.CreateVersion7()}, {SessionTestData.OrganisationId}, {branch}, '33AAACH7409R1Z8', '33', 'Example', NULL,
                     {new DateOnly(2027, 1, 1)}, NULL, now(), NULL, now(), NULL)
             """, Token));
        overlapping.SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
        overlapping.ConstraintName.ShouldBe(BillingDbContext.OneRegistrationInForceConstraint);
    }

    [Fact]
    public async Task RefusesADraftAndARegistrationWithoutAFirstDay()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("tax-noday", "203.0.113.249");
        var draft = await owner.PostAsync(
            "/api/v1/billing/tax-configuration/versions", new { name = "No first day" }, Key());
        draft.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await draft.Content.ReadAsStringAsync(Token)).ShouldContain("effectiveFrom");

        var registration = await owner.PostAsync(
            "/api/v1/billing/gst-registrations",
            new { branchId = Guid.CreateVersion7(), gstin = "33AAACH7409R1Z8", stateCode = "33", legalName = "Example" },
            Key());
        registration.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await registration.Content.ReadAsStringAsync(Token)).ShouldContain("effectiveFrom");
    }

    [Fact]
    public async Task RefusesEveryRouteToAHolderOfNoBillingPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("tax-deny-o", "203.0.113.244");
        var version = await DraftAsync(owner, "Denied");
        var codeId = await AddCodeAsync(owner, version, Code("DENIED"));
        var key = await VersionKeyAsync(owner, version);

        using var reader = await AdministrationHarness.AdministratorAsync(
            fixture, "tax-deny-r", "203.0.113.245", CatalogPermissions.Read);

        var attempts = new (string Method, string Path, object? Body)[]
        {
            ("GET", "/api/v1/billing/tax-configuration/versions", null),
            ("POST", "/api/v1/billing/tax-configuration/versions", new { name = "x", effectiveFrom = "2026-04-01" }),
            ("GET", $"/api/v1/billing/tax-configuration/versions/{version}", null),
            ("PUT", $"/api/v1/billing/tax-configuration/versions/{version}", new { name = "x", effectiveFrom = "2026-04-01" }),
            ("GET", $"/api/v1/billing/tax-configuration/versions/{version}/validation", null),
            ("POST", $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes", CodeBody("X_Y")),
            ("PUT", $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes/{codeId}", CodeBody("X_Y")),
            ("POST", $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes/{codeId}/delete", new { reason = "x" }),
            ("POST", $"/api/v1/billing/tax-configuration/versions/{version}/publish", new { reason = "x" }),
            ("GET", "/api/v1/billing/gst-registrations", null),
            ("POST", "/api/v1/billing/gst-registrations", RegistrationBody(Guid.CreateVersion7(), new DateOnly(2026, 4, 1), null)),
            ("GET", $"/api/v1/billing/gst-registrations/{Guid.CreateVersion7()}", null),
            ("PUT", $"/api/v1/billing/gst-registrations/{Guid.CreateVersion7()}", RegistrationBody(Guid.CreateVersion7(), new DateOnly(2026, 4, 1), null)),
        };

        foreach (var (method, path, body) in attempts)
        {
            var response = method switch
            {
                "GET" => await reader.GetAsync(path),
                "PUT" => await reader.PutAsync(path, body!, key),
                _ => await reader.PostAsync(path, body!, key),
            };
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static async Task<(string Name, string Value)[]> VersionKeyAsync(
        AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/billing/tax-configuration/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();
        return [.. Key(), ("If-Match", read.Headers.ETag.ToString())];
    }

    private static string Code(string stem) => $"{stem}_{RunToken}";

    private Task<AdministrationHarness.AdministratorClient> OwnerAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, BillingPermissions.ManagePriceLists, BillingPermissions.PublishPriceList);

    private static async Task<Guid> DraftAsync(
        AdministrationHarness.AdministratorClient client, string name, Guid? cloneFrom = null)
    {
        var response = await client.PostAsync(
            "/api/v1/billing/tax-configuration/versions",
            new { name, notes = (string?)null, effectiveFrom = "2026-04-01", cloneFromVersionId = cloneFrom },
            Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("version").GetProperty("taxConfigurationVersionId").GetGuid();
    }

    private static object CodeBody(string code, decimal half = 2.5m)
        => new
        {
            code,
            description = "Tailoring services",
            classification = "998822",
            kind = "Services",
            active = true,
            rates = new[]
            {
                new { kind = "Cgst", ratePercent = half },
                new { kind = "Sgst", ratePercent = half },
                new { kind = "Igst", ratePercent = half * 2 },
            },
            reason = (string?)null,
        };

    private static async Task<Guid> AddCodeAsync(AdministrationHarness.AdministratorClient client, Guid version, string code)
    {
        var response = await client.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes", CodeBody(code), await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("taxCodeId").GetGuid();
    }

    private static object RegistrationBody(Guid branch, DateOnly from, DateOnly? to, string gstin = "33AAACH7409R1Z8")
        => new
        {
            branchId = branch,
            gstin,
            stateCode = "33",
            legalName = "Example Tailors Private Limited",
            tradeName = "Example Tailors",
            effectiveFrom = from,
            effectiveTo = to,
            reason = "Written by an integration test.",
        };
}
