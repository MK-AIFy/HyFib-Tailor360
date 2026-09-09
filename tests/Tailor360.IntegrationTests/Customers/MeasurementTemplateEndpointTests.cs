using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// Measurement-template administration over HTTP: drafting, reviewing, publishing and what is refused.
/// </summary>
/// <remarks>
/// These run against a real PostgreSQL because most of what is under test is a property of the database rather
/// than of the application: the immutability of a published version is a trigger, "at most one published version"
/// is a partial unique index, and the separation of duties is decided by a count of administrators the query has
/// to actually run.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class MeasurementTemplateEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task AnAdministratorDraftsReviewsAndPublishesATemplateWithoutADeployment()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync("mt-publish", "203.0.113.220");
        using var reviewer = await ReviewerAsync("mt-publish-rev", "203.0.113.230");

        var template = await CreateAsync(drafter, Code("MT_BLOUSE"));
        var version = await DraftAsync(drafter, template, "Version 1");

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/fields", Field("chest_bust"), Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var validation = await drafter.GetAsync($"{Root}/{template}/versions/{version}/validation");

        validation.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var report = JsonDocument.Parse(await validation.Content.ReadAsStringAsync(Token)))
        {
            report.RootElement.GetProperty("isReadyToPublish").GetBoolean().ShouldBeTrue();
        }

        (await drafter.PostAsync($"{Root}/{template}/versions/{version}/submit", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync($"{Root}/{template}/versions/{version}/approve", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var published = await reviewer.PostAsync(
            $"{Root}/{template}/versions/{version}/publish",
            new { reason = "Reviewed with the Tailor Master." },
            Key());

        published.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await published.Content.ReadAsStringAsync(Token));

        body.RootElement.GetProperty("publishedVersionId").GetGuid().ShouldBe(version);
        body.RootElement.GetProperty("versions").EnumerateArray().Single()
            .GetProperty("status").GetString().ShouldBe("Published");
    }

    [Fact]
    public async Task RefusesEveryEditOnceAVersionLeavesDraft()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync("mt-immutable", "203.0.113.221");
        using var reviewer = await ReviewerAsync("mt-immutable-rev", "203.0.113.231");

        var template = await CreateAsync(drafter, Code("MT_SALWAR"));
        var version = await DraftAsync(drafter, template, "Version 1");
        var field = await AddFieldAsync(drafter, template, version, "waist");

        (await drafter.PostAsync($"{Root}/{template}/versions/{version}/submit", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Submitted is already too late: a reviewer reads a version that cannot change under them.
        var edited = await drafter.PutAsync(
            $"{Root}/{template}/versions/{version}/fields/{field}", Field("waist"), Key());

        edited.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(edited)).ShouldBe("measurements.version-not-editable");

        (await reviewer.PostAsync($"{Root}/{template}/versions/{version}/approve", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync(
                $"{Root}/{template}/versions/{version}/publish", new { reason = "Live." }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterPublication = await drafter.PostAsync(
            $"{Root}/{template}/versions/{version}/fields", Field("hip"), Key());

        afterPublication.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(afterPublication)).ShouldBe("measurements.version-not-editable");
    }

    [Fact]
    public async Task RefusesToPublishAVersionValidationRefuses()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync("mt-invalid", "203.0.113.222");
        using var reviewer = await ReviewerAsync("mt-invalid-rev", "203.0.113.232");

        var template = await CreateAsync(drafter, Code("MT_GOWN"));
        var version = await DraftAsync(drafter, template, "Version 1");

        // Required, and hidden by a rule that always matches: a capture could never be completed.
        var alwaysHidden = Field("sleeve_length") with
        {
            Rule = new { effect = "HiddenWhen", anyOf = Array.Empty<object>() },
        };

        (await drafter.PostAsync($"{Root}/{template}/versions/{version}/fields", alwaysHidden, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var validation = await drafter.GetAsync($"{Root}/{template}/versions/{version}/validation");

        using (var report = JsonDocument.Parse(await validation.Content.ReadAsStringAsync(Token)))
        {
            report.RootElement.GetProperty("isReadyToPublish").GetBoolean().ShouldBeFalse();
            report.RootElement.GetProperty("findings").EnumerateArray()
                .ShouldContain(finding => finding.GetProperty("code").GetString()
                    == "required-field-never-shown");
        }

        (await drafter.PostAsync($"{Root}/{template}/versions/{version}/submit", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync($"{Root}/{template}/versions/{version}/approve", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await reviewer.PostAsync(
            $"{Root}/{template}/versions/{version}/publish", new { reason = "Trying it on." }, Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("measurements.publish-validation-failed");
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("retire")]
    public async Task RefusesAPublishLevelActWithoutAReason(string verb)
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync($"mt-reason-{verb}", "203.0.113.223");
        using var reviewer = await ReviewerAsync($"mt-reason-{verb}-rev", "203.0.113.233");

        var template = await CreateAsync(drafter, Code($"MT_REASON_{verb.ToUpperInvariant()}"));
        var version = await DraftAsync(drafter, template, "Version 1");

        await AddFieldAsync(drafter, template, version, "chest_bust");

        (await drafter.PostAsync($"{Root}/{template}/versions/{version}/submit", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync($"{Root}/{template}/versions/{version}/approve", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await reviewer.PostAsync(
            $"{Root}/{template}/versions/{version}/{verb}", new { reason = "   " }, Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("measurements.value-required");
    }

    [Fact]
    public async Task TheSubmitterCannotApproveTheirOwnWork()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The separation of duties, over the wire. The account holds both permissions, so this is not a
        // permission refusal: it is the rule that the person who wrote a template is not, by that alone, the
        // person who decides it is right.
        using var both = await AdministrationHarness.AdministratorAsync(
            fixture, "mt-selfapprove", "203.0.113.226", CustomersPermissions.EditTemplates,
            CustomersPermissions.PublishTemplates);
        using var other = await ReviewerAsync("mt-selfapprove-rev", "203.0.113.236");

        var template = await CreateAsync(both, Code("MT_SELF"));
        var version = await DraftAsync(both, template, "Version 1");

        await AddFieldAsync(both, template, version, "chest_bust");

        (await both.PostAsync($"{Root}/{template}/versions/{version}/submit", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await both.PostAsync($"{Root}/{template}/versions/{version}/approve", new { }, Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(refused)).ShouldBe("measurements.submitter-cannot-publish");

        // Anybody else holding the permission may.
        (await other.PostAsync($"{Root}/{template}/versions/{version}/approve", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RefusesEveryTemplateRouteToACallerHoldingNothing()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var nobody = await AdministrationHarness.AdministratorAsync(
            fixture, "mt-nothing", "203.0.113.224", CustomersPermissions.Read);

        (await nobody.GetAsync(Root)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.PostAsync(Root, new { code = Code("MT_NO"), name = "No" }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RefusesToPublishWithOnlyTheDraftingPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await AdministrationHarness.AdministratorAsync(
            fixture, "mt-drafter", "203.0.113.225", CustomersPermissions.EditTemplates);

        var template = await CreateAsync(drafter, Code("MT_DRAFTER"));
        var version = await DraftAsync(drafter, template, "Version 1");

        await AddFieldAsync(drafter, template, version, "chest_bust");

        (await drafter.PostAsync($"{Root}/{template}/versions/{version}/submit", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK, "drafting includes submitting for review");

        (await drafter.PostAsync($"{Root}/{template}/versions/{version}/approve", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden, "approving is a reviewer's act");
        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/publish", new { reason = "Mine." }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SeedsTheSixTemplatesOnceAndLeavesThemUnpublished()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var scope = fixture.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IMeasurementTemplateReferenceDataSeeder>();
        var organisationId = Guid.CreateVersion7();

        var first = await seeder.SeedTemplatesAsync(organisationId, Token);

        first.Created.ShouldBeTrue();
        first.TemplateCount.ShouldBe(6);
        first.FieldCount.ShouldBeGreaterThan(80, "the six seeded field sets together");

        // Nothing is published, and that is the point: every field set in the specification is marked "proposed
        // and to be confirmed", and business review of each is an acceptance criterion of #27.
        first.AwaitingPublication.ShouldBe(6);

        var second = await seeder.SeedTemplatesAsync(organisationId, Token);

        second.Created.ShouldBeFalse("a second run changes nothing");
        second.TemplateCount.ShouldBe(first.TemplateCount);
        second.FieldCount.ShouldBe(first.FieldCount);
    }

    [Fact]
    public async Task EverySeededTemplateWouldPassItsOwnPublishValidation()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The seed is transcribed from a document by hand, so the risk is a number or a key that the domain
        // refuses. Seeding proves the fields were accepted; this proves the version would actually publish, which
        // is what an Owner will try to do after reviewing it.
        using var scope = fixture.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IMeasurementTemplateReferenceDataSeeder>();
        var handler = scope.ServiceProvider.GetRequiredService<MeasurementTemplateHandler>();
        var organisationId = Guid.CreateVersion7();

        await seeder.SeedTemplatesAsync(organisationId, Token);

        var templates = await handler.ListAsync(organisationId, Token);

        templates.Count.ShouldBe(6);

        foreach (var administered in templates)
        {
            var draft = administered.Template.Versions.Single();
            var report = await handler.ValidateAsync(
                administered.Template.Id, draft.Id, organisationId, Token);

            report.IsSuccess.ShouldBeTrue();
            report.Value.HasErrors.ShouldBeFalse(
                $"'{administered.Template.Code}' reports: "
                + string.Join(
                    "; ",
                    report.Value.Findings
                        .Where(finding => finding.Severity.ToString() == "Error")
                        .Select(finding => $"{finding.Code} at {finding.Target}")));
        }
    }

    private const string Root = "/api/v1/customers/measurement-templates";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static string Code(string stem) => $"{stem}_{RunToken}";

    /// <summary>An administrator who drafts and submits, and cannot approve their own work.</summary>
    private Task<AdministrationHarness.AdministratorClient> DrafterAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CustomersPermissions.EditTemplates);

    /// <summary>A second administrator, who reviews, approves, publishes and retires.</summary>
    /// <remarks>
    /// Separate from the drafter on purpose. "The submitter does not also approve" is the rule under test in
    /// <see cref="TheSubmitterCannotApproveTheirOwnWork"/> and a precondition of every other lifecycle test here;
    /// using one client for both would make them pass or fail depending on how many administrators the shared test
    /// database happened to hold.
    /// </remarks>
    private Task<AdministrationHarness.AdministratorClient> ReviewerAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CustomersPermissions.PublishTemplates);

    private static async Task<Guid> CreateAsync(
        AdministrationHarness.AdministratorClient client, string code)
    {
        var response = await client.PostAsync(
            Root, new { code, name = code, description = "Written by a test." }, Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.GetProperty("measurementTemplateId").GetGuid();
    }

    private static async Task<Guid> DraftAsync(
        AdministrationHarness.AdministratorClient client, Guid template, string name)
    {
        var response = await client.PostAsync(
            $"{Root}/{template}/versions",
            new { name, notes = (string?)null, defaultDisplayUnit = "Inch", cloneFromVersionId = (Guid?)null },
            Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.GetProperty("versions").EnumerateArray().First()
            .GetProperty("templateVersionId").GetGuid();
    }

    private static async Task<Guid> AddFieldAsync(
        AdministrationHarness.AdministratorClient client, Guid template, Guid version, string key)
    {
        var response = await client.PostAsync($"{Root}/{template}/versions/{version}/fields", Field(key), Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.GetProperty("versions").EnumerateArray().First()
            .GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("key").GetString() == key)
            .GetProperty("templateFieldId").GetGuid();
    }

    private static FieldBody Field(string key) => new(
        key,
        key.Replace('_', ' '),
        null,
        "Bodice",
        0,
        "Millimetre",
        8,
        1,
        true,
        100,
        2000,
        200,
        1800,
        "Body measurement. Round the fullest part, tape level.",
        "blouse_front_v1",
        null,
        "From one point to the other, tape level.",
        null,
        []);

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The field request as the wire carries it.</summary>
    internal sealed record FieldBody(
        string Key,
        string Label,
        string? LabelTamil,
        string GroupName,
        int DisplayOrder,
        string CanonicalUnit,
        int InchFraction,
        int CentimetreDecimals,
        bool IsRequired,
        decimal MinimumMillimetres,
        decimal MaximumMillimetres,
        decimal? WarnBelowMillimetres,
        decimal? WarnAboveMillimetres,
        string HelpText,
        string? DiagramKey,
        Guid? DiagramMediaId,
        string? DiagramAlt,
        object? Rule,
        object[] Options);
}
