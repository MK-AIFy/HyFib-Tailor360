using Shouldly;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Catalogue;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// The rule-shaped publish checks (#138): each one with a minimal failing version, and a well-formed
/// version they all pass.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DesignRuleValidatorTests
{
    private static readonly Guid Blouse = CatalogTestData.Id("blouse");
    private readonly DesignRuleValidator _validator = new();

    [Fact]
    public async Task FindsNothingWrongWithTheSeededBlouseRules()
    {
        var findings = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(1, "Requires", Operand("padding", "In", "LIGHT", "MOULDED_CUP"), Operand("lining", "NotEquals", "NONE")),
                Rule(2, "Requires", Operand("blouse_cut", "Equals", "KATORI"), Operand("lining", "Equals", "KATORI_CUP")),
                Rule(4, "Excludes", Operand("sleeve_style", "Equals", "SLEEVELESS"), Operand("finish", "Includes", "LACE_EDGE")),
                Rule(6, "Note", Operand("padding", "Equals", "MOULDED_CUP"), null),
            ]));

        findings.ShouldBeEmpty(string.Join("; ", findings.Select(finding => finding.Message)));
    }

    [Fact]
    public async Task RefusesARuleNamingAnUnknownOrRetiredOption()
    {
        var findings = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(7, "Requires", Operand("padding", "Equals", "HEAVY"), Operand("lining", "Equals", "FULL")),
                Rule(8, "Note", Operand("sleeve_shape", "Equals", "FRILL"), null),
            ]));

        Errors(findings).Single(finding => finding.Code == "design.rule-unknown-option")
            .Target.ShouldBe("designRules[DR-07].antecedent.optionCodes");
        Errors(findings).Single(finding => finding.Code == "design.rule-names-retired-option")
            .Message.ShouldContain("DR-08");
    }

    [Fact]
    public async Task WarnsAboutAMultipleChoiceFormOverASingleChoiceGroup()
    {
        var findings = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules: [Rule(9, "Note", Operand("lining", "Includes", "FULL"), null)]));

        // A warning: the document lists no such publish error, and over one choice the form reads as `=`.
        var odd = findings.ShouldHaveSingleItem();
        odd.Code.ShouldBe("design.rule-form-needs-multiple-choice");
        odd.Severity.ShouldBe(CatalogFindingSeverity.Warning);
    }

    [Fact]
    public async Task RefusesOverlapsThatOnlyTheGroupsOptionsReveal()
    {
        // `lining = FULL requires lining ≠ NONE`: FULL satisfies both sides, though neither names it twice.
        // `lining ≠ NONE excludes lining ≠ FULL`: KATORI_CUP satisfies both. `any selection` overlaps all.
        var findings = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(20, "Requires", Operand("lining", "Equals", "FULL"), Operand("lining", "NotEquals", "NONE")),
                Rule(21, "Excludes", Operand("lining", "NotEquals", "NONE"), Operand("lining", "NotEquals", "FULL")),
                Rule(22, "Excludes", Operand("lining", "AnySelection"), Operand("lining", "Equals", "NONE")),
                // Disjoint: choosing FULL forbids the other two, which says something and contradicts nothing.
                Rule(23, "Excludes", Operand("lining", "Equals", "FULL"), Operand("lining", "NotEquals", "FULL")),
            ]));

        var overlaps = Errors(findings).Where(finding => finding.Code == "design.rule-operands-overlap").ToArray();
        overlaps.Select(finding => finding.Target).ShouldBe(
            ["designRules[DR-20].consequent.optionCodes", "designRules[DR-21].consequent.optionCodes", "designRules[DR-22].consequent.optionCodes"]);
        overlaps[0].Message.ShouldContain("lining.FULL");
        overlaps[1].Message.ShouldContain("lining.KATORI_CUP");
    }

    [Fact]
    public async Task ACycleIsOverOptionsNotGroups()
    {
        // Two rules over the same two groups in opposite directions are not a cycle when they fire on
        // different options: FULL obliges LIGHT, and MOULDED_CUP obliges KATORI_CUP.
        var apart = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(24, "Requires", Operand("lining", "Equals", "FULL"), Operand("padding", "Equals", "LIGHT")),
                Rule(25, "Requires", Operand("padding", "Equals", "MOULDED_CUP"), Operand("lining", "Equals", "KATORI_CUP")),
            ]));
        apart.ShouldNotContain(finding => finding.Code == "design.requires-cycle");

        // The same two groups, and now LIGHT obliges FULL back: a circle over the options themselves,
        // through a negation that admits FULL.
        var around = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(24, "Requires", Operand("lining", "Equals", "FULL"), Operand("padding", "Equals", "LIGHT")),
                Rule(26, "Requires", Operand("padding", "Equals", "LIGHT"), Operand("lining", "NotEquals", "NONE")),
            ]));
        var cycle = Errors(around).Single(finding => finding.Code == "design.requires-cycle");
        cycle.Message.ShouldContain("DR-24, DR-26");
        cycle.Message.ShouldContain("lining.FULL → padding.LIGHT → lining.FULL");
    }

    [Fact]
    public async Task ARequiresIsEmptiedByAnyExcludesThatCanFireBesideIt()
    {
        // DR-27 obliges a lining whenever the padding is light; DR-28 forbids every lining whenever the
        // sleeve is sleeveless. Different groups, so a customer can choose both, and then has nowhere to go.
        var acrossGroups = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(27, "Requires", Operand("padding", "Equals", "LIGHT"), Operand("lining", "NotEquals", "NONE")),
                Rule(28, "Excludes", Operand("sleeve_style", "Equals", "SLEEVELESS"), Operand("lining", "NotEquals", "NONE")),
            ]));
        Errors(acrossGroups).Count(finding => finding.Code == "design.requires-emptied-by-excludes").ShouldBe(2);

        // The same group and disjoint triggers — light padding against a moulded cup — can never fire
        // together on a single choice, so the pair is fine.
        var disjoint = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(27, "Requires", Operand("padding", "Equals", "LIGHT"), Operand("lining", "NotEquals", "NONE")),
                Rule(29, "Excludes", Operand("padding", "Equals", "MOULDED_CUP"), Operand("lining", "NotEquals", "NONE")),
            ]));
        disjoint.ShouldNotContain(finding => finding.Code == "design.requires-emptied-by-excludes");

        // An excludes that removes only part of the wanted set leaves something to choose.
        var partial = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(27, "Requires", Operand("padding", "Equals", "LIGHT"), Operand("lining", "NotEquals", "NONE")),
                Rule(30, "Excludes", Operand("padding", "Equals", "LIGHT"), Operand("lining", "Equals", "FULL")),
            ]));
        partial.ShouldNotContain(finding => finding.Code == "design.requires-emptied-by-excludes");
    }

    [Fact]
    public async Task ARequiredGroupMadeUnsatisfiableByAConditionalRuleIsRefusedToo()
    {
        var findings = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules: [Rule(19, "Excludes", Operand("sleeve_style", "Equals", "SLEEVELESS"), Operand("lining", "In", "NONE", "FULL", "KATORI_CUP"))]));

        var trap = Errors(findings).Single(finding => finding.Code == "design.required-group-unsatisfiable");
        trap.Message.ShouldContain("whenever sleeve_style = SLEEVELESS");
        trap.Target.ShouldBe("designRules[DR-19].consequent.optionCodes");
    }

    [Fact]
    public async Task RefusesARequiresCycleNamingEveryRuleInIt()
    {
        var findings = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(10, "Requires", Operand("padding", "Equals", "LIGHT"), Operand("lining", "Equals", "FULL")),
                Rule(11, "Requires", Operand("lining", "Equals", "FULL"), Operand("finish", "Includes", "PIPING")),
                Rule(12, "Requires", Operand("finish", "Includes", "PIPING"), Operand("padding", "Equals", "LIGHT")),
            ]));

        var cycle = Errors(findings).Single(finding => finding.Code == "design.requires-cycle");
        cycle.Message.ShouldContain("DR-10");
        cycle.Message.ShouldContain("DR-11");
        cycle.Message.ShouldContain("DR-12");
    }

    [Fact]
    public async Task RefusesOverlappingOperandsAndARequiresNothingCouldSatisfy()
    {
        var findings = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(13, "Excludes", Operand("lining", "In", "FULL", "KATORI_CUP"), Operand("lining", "Equals", "FULL")),
                Rule(14, "Requires", Operand("padding", "Equals", "LIGHT"), Operand("sleeve_shape", "Equals", "FRILL")),
            ]));

        Errors(findings).ShouldContain(finding => finding.Code == "design.rule-operands-overlap");
        Errors(findings).ShouldContain(finding => finding.Code == "design.requires-nothing-offerable");
    }

    [Fact]
    public async Task RefusesARequiresWhoseGroupIsNotOfferedWhereTheAntecedentIs()
    {
        // finish is offered at the main branch only; padding at both.
        var findings = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules: [Rule(15, "Requires", Operand("padding", "Equals", "LIGHT"), Operand("finish", "Includes", "PIPING"))]));

        Errors(findings).ShouldContain(finding => finding.Code == "design.requires-not-offered-where-antecedent-is");
    }

    [Fact]
    public async Task RefusesARequiresEmptiedByAnExcludesAgainstBothIdentifiers()
    {
        var findings = await CheckAsync(Candidate(
            groups: BlouseGroups(),
            rules:
            [
                Rule(16, "Requires", Operand("padding", "Equals", "LIGHT"), Operand("lining", "In", "FULL", "KATORI_CUP")),
                Rule(17, "Excludes", Operand("padding", "Equals", "LIGHT"), Operand("lining", "NotEquals", "NONE")),
            ]));

        var pair = Errors(findings).Where(finding => finding.Code == "design.requires-emptied-by-excludes").ToArray();
        pair.Select(finding => finding.Target).ShouldBe(
            ["designRules[DR-16].consequent.optionCodes", "designRules[DR-17].consequent.optionCodes"]);
    }

    [Fact]
    public async Task RefusesARequiredGroupNoGarmentCouldSatisfy()
    {
        var findings = await CheckAsync(Candidate(
            groups:
            [
                Group("lining", true, "SingleChoice", [Option("NONE", active: false), Option("FULL", active: false)],
                    branches: [CatalogTestData.MainBranch]),
                Group("closure", true, "SingleChoice", [Option("HOOK"), Option("ZIP_BACK")]),
            ],
            rules: [Rule(18, "Excludes", Operand(null, "Always"), Operand("closure", "In", "HOOK", "ZIP_BACK"))]));

        Errors(findings).ShouldContain(finding => finding.Code == "design.required-group-has-no-active-option");
        Errors(findings).ShouldContain(finding => finding.Code == "design.required-group-not-offered-at-branch");
        Errors(findings).ShouldContain(finding => finding.Code == "design.required-group-unsatisfiable");
    }

    [Fact]
    public async Task WarnsAboutAGroupWithMoreOptionsThanThePickerIsThoughtToHold()
    {
        var findings = await CheckAsync(Candidate(
            groups:
            [
                Group("motif", false, "MultipleChoice",
                    [.. Enumerable.Range(1, DesignRuleValidator.ProposedOptionCeiling + 1).Select(n => Option($"MOTIF_{n}"))]),
            ],
            rules: []));

        findings.ShouldAllBe(finding => finding.Severity == CatalogFindingSeverity.Warning);
        findings.Single().Code.ShouldBe("design.group-has-many-options");
    }

    private async Task<IReadOnlyList<CatalogFinding>> CheckAsync(CatalogPublicationCandidate candidate)
        => await _validator.ValidatePublicationAsync(candidate, TestContext.Current.CancellationToken);

    private static IReadOnlyList<CatalogFinding> Errors(IEnumerable<CatalogFinding> findings)
        => [.. findings.Where(finding => finding.Severity == CatalogFindingSeverity.Error)];

    private static IReadOnlyList<CatalogDesignGroupView> BlouseGroups()
        =>
        [
            Group("blouse_cut", true, "SingleChoice", [Option("PLAIN_DART"), Option("KATORI")]),
            Group("sleeve_style", true, "SingleChoice", [Option("SLEEVELESS"), Option("SHORT")]),
            Group("sleeve_shape", false, "SingleChoice", [Option("PLAIN"), Option("FRILL", active: false)]),
            Group("lining", true, "SingleChoice", [Option("NONE"), Option("FULL"), Option("KATORI_CUP")]),
            Group("padding", false, "SingleChoice", [Option("NONE"), Option("LIGHT"), Option("MOULDED_CUP")]),
            Group("finish", false, "MultipleChoice", [Option("PIPING"), Option("LACE_EDGE")],
                branches: [CatalogTestData.MainBranch]),
        ];

    private static CatalogPublicationCandidate Candidate(
        IReadOnlyList<CatalogDesignGroupView> groups,
        IReadOnlyList<CatalogDesignRuleView> rules)
        => new(
            CatalogTestData.Id("version"),
            CatalogTestData.Organisation,
            1,
            [
                new CatalogCategoryView(
                    Blouse, CatalogTestData.Id("blouse-key"), "BLOUSE_PATTERN", "Blouse", null, null, null, null,
                    [CatalogTestData.MainBranch, CatalogTestData.SecondBranch]),
            ],
            [],
            CatalogCodeHistory.Empty,
            CatalogCodeHistory.Empty,
            groups,
            rules,
            CatalogCodeHistory.Empty,
            CatalogCodeHistory.Empty);

    private static CatalogDesignGroupView Group(
        string code,
        bool required,
        string mode,
        IReadOnlyList<CatalogDesignOptionView> options,
        IReadOnlyCollection<Guid>? branches = null)
        => new(
            CatalogTestData.Id($"group-{code}"),
            CatalogTestData.Id($"group-key-{code}"),
            Blouse,
            code,
            code,
            mode,
            required,
            0,
            null,
            null,
            branches ?? [CatalogTestData.MainBranch, CatalogTestData.SecondBranch],
            options);

    private static CatalogDesignOptionView Option(string code, bool active = true)
        => new(
            CatalogTestData.Id($"option-{code}"),
            CatalogTestData.Id($"option-key-{code}"),
            code,
            code,
            active,
            true,
            true,
            true,
            null,
            0,
            0);

    private static CatalogDesignRuleView Rule(int number, string type, CatalogDesignOperandView antecedent, CatalogDesignOperandView? consequent)
        => new(
            CatalogTestData.Id($"rule-{number}"),
            CatalogTestData.Id($"rule-key-{number}"),
            Blouse,
            number,
            $"DR-{number:00}",
            type,
            antecedent,
            consequent,
            type == "Note" ? "A note." : null);

    private static CatalogDesignOperandView Operand(string? groupCode, string form, params string[] optionCodes)
        => new(groupCode, form, optionCodes);
}
