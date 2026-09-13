using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Catalog;

/// <summary>
/// The published <see cref="IDesignSelectionValidator"/> resolved through the host, against a version
/// built through the administration routes, and the rule-shaped publish checks reaching the validation
/// report under their own name (#138).
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class DesignSelectionValidatorTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();
    private static readonly string[] PaddingCodes = ["LIGHT", "MOULDED_CUP"];
    private static readonly string[] NoneOnly = ["NONE"];
    private static readonly string[] CupOnly = ["KATORI_CUP"];
    private static readonly string[] KatoriOnly = ["KATORI"];
    private static readonly string[] HalterOnly = ["HALTER"];

    [Fact]
    public async Task EvaluatesADraftsSelectionsThroughTheContractAndSelectsTheOneOptionARuleObliges()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("dsv-eval", "203.0.113.230");
        var version = await DraftAsync(owner, "Evaluated through the contract");
        var blouse = await AddCategoryAsync(owner, version, Code("BLOUSE_PATTERN"));
        var cut = await AddGroupAsync(owner, version, blouse, "blouse_cut", 0);
        var lining = await AddGroupAsync(owner, version, blouse, "lining", 1);
        var padding = await AddGroupAsync(owner, version, blouse, "padding", 2, required: false);
        await AddOptionAsync(owner, version, cut, "PLAIN_DART");
        await AddOptionAsync(owner, version, cut, "KATORI");
        await AddOptionAsync(owner, version, lining, "NONE");
        await AddOptionAsync(owner, version, lining, "FULL");
        await AddOptionAsync(owner, version, lining, "KATORI_CUP");
        await AddOptionAsync(owner, version, padding, "LIGHT");
        await AddOptionAsync(owner, version, padding, "MOULDED_CUP");
        await AddRuleAsync(owner, version, blouse, "Requires",
            new { groupCode = "padding", form = "In", optionCodes = PaddingCodes },
            new { groupCode = "lining", form = "NotEquals", optionCodes = NoneOnly });
        await AddRuleAsync(owner, version, blouse, "Requires",
            new { groupCode = "blouse_cut", form = "Equals", optionCodes = KatoriOnly },
            new { groupCode = "lining", form = "Equals", optionCodes = CupOnly });

        using var scope = fixture.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDesignSelectionValidator>();

        // A katori cut with nothing chosen for the lining: DR-nn's one admissible option is selected on
        // the customer's behalf and said; the required lining is thereby satisfied.
        var katori = await validator.ValidateAsync(
            new DesignSelectionRequest(
                SessionTestData.OrganisationId, version, blouse, SessionTestData.HomeBranchId,
                new DateOnly(2026, 9, 12),
                [new DesignSelection("blouse_cut", KatoriOnly)],
                false),
            Token);
        katori.IsSuccess.ShouldBeTrue(katori.IsFailure ? katori.Error.Message : string.Empty);
        katori.Value.IsConfirmable.ShouldBeTrue(string.Join("; ", katori.Value.Violations.Select(found => found.Message)));
        katori.Value.AutoSelections.ShouldHaveSingleItem().OptionCode.ShouldBe("KATORI_CUP");

        // Padding with no lining: two options would satisfy the rule, so it is a prompt, and blocking.
        var padded = await validator.ValidateAsync(
            new DesignSelectionRequest(
                SessionTestData.OrganisationId, version, blouse, SessionTestData.HomeBranchId,
                new DateOnly(2026, 9, 12),
                [new DesignSelection("blouse_cut", ["PLAIN_DART"]), new DesignSelection("padding", ["LIGHT"])],
                false),
            Token);
        padded.IsSuccess.ShouldBeTrue();
        padded.Value.IsConfirmable.ShouldBeFalse();
        padded.Value.Violations.ShouldContain(found => found.Code == "design.requires-choice");
        padded.Value.AutoSelections.ShouldBeEmpty();

        // An unknown version is a refusal, not an empty evaluation.
        var unknown = await validator.ValidateAsync(
            new DesignSelectionRequest(
                SessionTestData.OrganisationId, Guid.CreateVersion7(), blouse, SessionTestData.HomeBranchId,
                new DateOnly(2026, 9, 12), [], false),
            Token);
        unknown.IsFailure.ShouldBeTrue();
        unknown.Error.Code.ShouldBe("catalog.version-not-found");

        // Another organisation's version is not found either — not forbidden, not evaluated: the contract
        // never confirms that a version exists to a caller it does not belong to.
        var elsewhere = await validator.ValidateAsync(
            new DesignSelectionRequest(
                Guid.CreateVersion7(), version, blouse, SessionTestData.HomeBranchId,
                new DateOnly(2026, 9, 12), [new DesignSelection("blouse_cut", KatoriOnly)], false),
            Token);
        elsewhere.IsFailure.ShouldBeTrue();
        elsewhere.Error.Code.ShouldBe("catalog.version-not-found");
    }

    [Fact]
    public async Task TheValidationReportAttributesARuleShapedFindingToTheDesignChecks()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("dsv-report", "203.0.113.231");
        var version = await DraftAsync(owner, "A rule naming an option that is not there");
        var gown = await AddCategoryAsync(owner, version, Code("GOWN"));
        var neckline = await AddGroupAsync(owner, version, gown, "neckline", 0);
        await AddOptionAsync(owner, version, neckline, "ROUND");
        await AddRuleAsync(owner, version, gown, "Note",
            new { groupCode = "neckline", form = "Equals", optionCodes = HalterOnly },
            null,
            note: "Line the halter band.");

        using var report = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{version}/validation")).Content
                .ReadAsStringAsync(Token));

        report.RootElement.GetProperty("publishable").GetBoolean().ShouldBeFalse();
        var finding = report.RootElement.GetProperty("findings").EnumerateArray()
            .Single(found => found.GetProperty("code").GetString() == "design.rule-unknown-option");
        finding.GetProperty("validator").GetString().ShouldBe("design");
        finding.GetProperty("target").GetString().ShouldStartWith("designRules[DR-");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static async Task<(string Name, string Value)[]> VersionKeyAsync(
        AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/catalog/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();
        return [.. Key(), ("If-Match", read.Headers.ETag.ToString())];
    }

    private static string Code(string stem) => $"{stem}_{RunToken}";

    private Task<AdministrationHarness.AdministratorClient> OwnerAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CatalogPermissions.Edit, CatalogPermissions.Publish);

    private static async Task<Guid> DraftAsync(AdministrationHarness.AdministratorClient client, string name)
    {
        var response = await client.PostAsync("/api/v1/catalog/versions", new { name }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("version").GetProperty("catalogVersionId").GetGuid();
    }

    private static async Task<Guid> AddCategoryAsync(
        AdministrationHarness.AdministratorClient client, Guid version, string code)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories",
            new
            {
                code,
                name = code,
                nameTamil = (string?)null,
                description = "A synthetic category written by a test.",
                parentCategoryId = (Guid?)null,
                displayOrder = 0,
                activeFrom = (DateOnly?)null,
                activeTo = (DateOnly?)null,
                featureFlagKey = (string?)null,
                branchIds = new[] { SessionTestData.HomeBranchId },
                reason = "Written by an integration test.",
            },
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("categoryId").GetGuid();
    }

    private static async Task<Guid> AddGroupAsync(
        AdministrationHarness.AdministratorClient client,
        Guid version,
        Guid categoryId,
        string code,
        int displayOrder,
        bool required = true)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{categoryId}/design-groups",
            new
            {
                code,
                name = code.Replace('_', ' '),
                nameTamil = (string?)null,
                selectionMode = "SingleChoice",
                required,
                displayOrder,
                activeFrom = (DateOnly?)null,
                activeTo = (DateOnly?)null,
                branchIds = new[] { SessionTestData.HomeBranchId },
                reason = (string?)null,
            },
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("designOptionGroupId").GetGuid();
    }

    private static async Task AddOptionAsync(
        AdministrationHarness.AdministratorClient client, Guid version, Guid groupId, string code)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/design-groups/{groupId}/options",
            new
            {
                code,
                name = code.Replace('_', ' '),
                nameTamil = (string?)null,
                helpText = "What the choice means for the finished garment.",
                illustrationKey = (string?)null,
                illustrationAlt = "The shape in words.",
                priceListItemCode = (string?)null,
                timeImpactDays = 0,
                displayOrder = 0,
                active = true,
                reason = (string?)null,
            },
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task AddRuleAsync(
        AdministrationHarness.AdministratorClient client,
        Guid version,
        Guid categoryId,
        string type,
        object antecedent,
        object? consequent,
        string? note = null)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{categoryId}/design-rules",
            new { type, antecedent, consequent, note, why = (string?)null, reason = (string?)null },
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
    }
}
