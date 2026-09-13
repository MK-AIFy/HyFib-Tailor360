using Shouldly;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Catalogue;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// The structural design checks of the built-in validator (#137): codes, availability, the service
/// link, the words, and a rule's groups. Built through the contract types, because the aggregate
/// refuses most of these states and the validator exists for what a repair script could leave behind.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DesignPublicationChecksTests
{
    private static readonly Guid Blouse = CatalogTestData.Id("blouse");
    private static readonly Guid Gown = CatalogTestData.Id("gown");

    private readonly BuiltInCatalogValidator _validator = new();

    [Fact]
    public async Task FindsNothingWrongWithAWellFormedDesign()
    {
        var group = Group(Blouse, "padding", options: [Option("LIGHT"), Option("NONE")]);
        var findings = await CheckAsync(Candidate(
            designGroups: [group],
            designRules:
            [
                Rule(Blouse, 1, "Requires", Operand("padding", "Equals", "LIGHT"), Operand("padding", "Equals", "NONE")),
            ],
            serviceTypes: [Service(Blouse, "STITCHING", [group.Id])]));

        findings.Where(finding => finding.Code.StartsWith("catalog.design", StringComparison.Ordinal)
                                  || finding.Code.Contains("design", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task RefusesTwoGroupsSharingACodeInOneCategoryAndTwoOptionsSharingACodeInOneGroup()
    {
        var findings = await CheckAsync(Candidate(designGroups:
        [
            Group(Blouse, "padding", id: CatalogTestData.Id("g1"), options: [Option("LIGHT"), Option("LIGHT")]),
            Group(Blouse, "padding", id: CatalogTestData.Id("g2")),
            Group(Gown, "padding", id: CatalogTestData.Id("g3")),
        ]));

        Errors(findings).Count(finding => finding.Code == "catalog.duplicate-design-group-code").ShouldBe(1);
        Errors(findings).ShouldContain(finding => finding.Code == "catalog.duplicate-design-option-code");
    }

    [Fact]
    public async Task RefusesACodeThatChangedOnAnAlreadyPublishedGroupOrOption()
    {
        var group = Group(Blouse, "padding", options: [Option("LIGHT")]);
        var option = group.Options[0];
        var findings = await CheckAsync(Candidate(
            designGroups: [group],
            groupHistory: new CatalogCodeHistory(
                new Dictionary<Guid, string> { [group.Key] = "BLOUSE.padding_old" },
                new Dictionary<string, Guid>(StringComparer.Ordinal) { ["BLOUSE.padding_old"] = group.Key }),
            optionHistory: new CatalogCodeHistory(
                new Dictionary<Guid, string> { [option.Key] = "BLOUSE.padding.HEAVY" },
                new Dictionary<string, Guid>(StringComparer.Ordinal) { ["BLOUSE.padding.HEAVY"] = option.Key })));

        Errors(findings).Count(finding => finding.Code == "catalog.published-code-changed").ShouldBe(2);
    }

    [Fact]
    public async Task RefusesAGroupOfferedWhereItsCategoryIsNot()
    {
        var findings = await CheckAsync(Candidate(designGroups:
        [
            Group(Blouse, "padding", branches: [CatalogTestData.MainBranch, CatalogTestData.SecondBranch]),
        ]));

        Errors(findings).ShouldContain(
            finding => finding.Code == "catalog.design-group-branches-not-subset-of-category");
    }

    [Fact]
    public async Task RefusesAServiceTypeOfferingAGroupOfAnotherCategoryOrOfNoVersion()
    {
        var gownNeck = Group(Gown, "neckline");
        var findings = await CheckAsync(Candidate(
            designGroups: [gownNeck],
            serviceTypes:
            [
                Service(Blouse, "STITCHING", [gownNeck.Id, CatalogTestData.Id("a-group-that-is-not-here")]),
            ]));

        Errors(findings).ShouldContain(finding => finding.Code == "catalog.design-group-of-another-category");
        Errors(findings).ShouldContain(finding => finding.Code == "catalog.design-group-not-in-version");
    }

    [Fact]
    public async Task RefusesAnOptionWithoutAltTextOrHelpTextAndWarnsAboutOneWithoutADrawing()
    {
        var findings = await CheckAsync(Candidate(designGroups:
        [
            Group(Blouse, "padding", options:
            [
                Option("LIGHT", alt: false, help: false, illustration: false),
            ]),
        ]));

        Errors(findings).ShouldContain(finding => finding.Code == "catalog.design-option-missing-alt-text");
        Errors(findings).ShouldContain(finding => finding.Code == "catalog.design-option-missing-help-text");
        Warnings(findings).ShouldContain(finding => finding.Code == "catalog.design-option-no-illustration");
        Errors(findings).Single(finding => finding.Code == "catalog.design-option-missing-alt-text")
            .Target.ShouldBe("designGroups[BLOUSE.padding].options[LIGHT].illustrationAlt");
    }

    [Fact]
    public async Task RefusesARuleReadingAGroupThatIsNotOfItsCategory()
    {
        var findings = await CheckAsync(Candidate(
            designGroups: [Group(Blouse, "padding"), Group(Gown, "lining")],
            designRules:
            [
                Rule(Blouse, 4, "Requires", Operand("padding", "AnySelection"), Operand("lining", "Equals", "FULL")),
            ]));

        var found = Errors(findings).Single(finding => finding.Code == "catalog.design-rule-unknown-group");
        found.Message.ShouldContain("DR-04");
        found.Target.ShouldBe("designRules[DR-04].consequent.groupCode");
    }

    private async Task<IReadOnlyList<CatalogFinding>> CheckAsync(CatalogPublicationCandidate candidate)
        => await _validator.ValidatePublicationAsync(candidate, TestContext.Current.CancellationToken);

    private static IReadOnlyList<CatalogFinding> Errors(IEnumerable<CatalogFinding> findings)
        => [.. findings.Where(finding => finding.Severity == CatalogFindingSeverity.Error)];

    private static IReadOnlyList<CatalogFinding> Warnings(IEnumerable<CatalogFinding> findings)
        => [.. findings.Where(finding => finding.Severity == CatalogFindingSeverity.Warning)];

    private static CatalogPublicationCandidate Candidate(
        IReadOnlyList<CatalogDesignGroupView>? designGroups = null,
        IReadOnlyList<CatalogDesignRuleView>? designRules = null,
        IReadOnlyList<CatalogServiceTypeView>? serviceTypes = null,
        CatalogCodeHistory? groupHistory = null,
        CatalogCodeHistory? optionHistory = null)
        => new(
            CatalogTestData.Id("version"),
            CatalogTestData.Organisation,
            1,
            [
                new CatalogCategoryView(
                    Blouse, CatalogTestData.Id("blouse-key"), "BLOUSE", "Blouse", null, null, null, null,
                    [CatalogTestData.MainBranch]),
                new CatalogCategoryView(
                    Gown, CatalogTestData.Id("gown-key"), "GOWN", "Gown", null, null, null, null,
                    [CatalogTestData.MainBranch]),
            ],
            serviceTypes ?? [],
            CatalogCodeHistory.Empty,
            CatalogCodeHistory.Empty,
            designGroups ?? [],
            designRules ?? [],
            groupHistory ?? CatalogCodeHistory.Empty,
            optionHistory ?? CatalogCodeHistory.Empty);

    private static CatalogDesignGroupView Group(
        Guid categoryId,
        string code,
        Guid? id = null,
        IReadOnlyCollection<Guid>? branches = null,
        IReadOnlyList<CatalogDesignOptionView>? options = null)
        => new(
            id ?? CatalogTestData.Id($"group-{categoryId}-{code}"),
            CatalogTestData.Id($"group-key-{categoryId}-{code}"),
            categoryId,
            code,
            code,
            "SingleChoice",
            true,
            0,
            null,
            null,
            branches ?? [CatalogTestData.MainBranch],
            options ?? []);

    private static CatalogDesignOptionView Option(
        string code,
        bool alt = true,
        bool help = true,
        bool illustration = true)
        => new(
            CatalogTestData.Id($"option-{code}"),
            CatalogTestData.Id($"option-key-{code}"),
            code,
            code,
            true,
            illustration,
            alt,
            help,
            null,
            0,
            0);

    private static CatalogDesignRuleView Rule(
        Guid categoryId,
        int number,
        string type,
        CatalogDesignOperandView antecedent,
        CatalogDesignOperandView? consequent)
        => new(
            CatalogTestData.Id($"rule-{number}"),
            CatalogTestData.Id($"rule-key-{number}"),
            categoryId,
            number,
            $"DR-{number:00}",
            type,
            antecedent,
            consequent,
            null);

    private static CatalogDesignOperandView Operand(string groupCode, string form, params string[] optionCodes)
        => new(groupCode, form, optionCodes);

    private static CatalogServiceTypeView Service(Guid categoryId, string code, IReadOnlyList<Guid> groupIds)
        => new(
            CatalogTestData.Id($"service-{categoryId}-{code}"),
            CatalogTestData.Id($"service-key-{categoryId}-{code}"),
            categoryId,
            code,
            code,
            CatalogTestData.Id("template"),
            CatalogTestData.Id("workflow"),
            groupIds,
            "PL-X",
            CatalogTestData.Id("checklist"),
            false,
            null,
            null,
            [CatalogTestData.MainBranch]);
}
