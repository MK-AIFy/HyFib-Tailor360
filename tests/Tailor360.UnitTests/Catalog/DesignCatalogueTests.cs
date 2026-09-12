using Shouldly;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// The design catalogue as the version holds it (#30, #137): codes, the reserved <c>NONE</c>, what one
/// version can check about a rule, cloning, removal and immutability after publication.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DesignCatalogueTests
{
    [Fact]
    public void AddsAGroupItsOptionsAndARuleToACategory()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");

        var padding = Group(version, blouse, "padding", required: false).Value;
        var lining = Group(version, blouse, "lining", displayOrder: 1).Value;
        Option(version, padding.Id, "LIGHT").IsSuccess.ShouldBeTrue();
        Option(version, padding.Id, "MOULDED_CUP").IsSuccess.ShouldBeTrue();
        Option(version, lining.Id, DesignCode.None).IsSuccess.ShouldBeTrue();
        Option(version, lining.Id, "FULL").IsSuccess.ShouldBeTrue();

        var rule = version.AddDesignRule(
            CatalogTestData.Id("dr-1"),
            CatalogTestData.Id("dr-1-key"),
            blouse,
            1,
            Requires(
                new DesignRuleOperand("padding", DesignOperandForm.In, ["LIGHT", "MOULDED_CUP"]),
                new DesignRuleOperand("lining", DesignOperandForm.In, ["FULL"])),
            CatalogTestData.Now,
            null);

        rule.IsSuccess.ShouldBeTrue(rule.IsFailure ? rule.Error.Message : string.Empty);
        rule.Value.Identifier.ShouldBe("DR-01");
        rule.Value.Statement.ShouldBe("padding in (LIGHT, MOULDED_CUP) requires lining in (FULL)");
        rule.Value.Blocks.ShouldBeTrue();
        version.DesignGroupsOf(blouse).Select(group => group.Code).ShouldBe(["padding", "lining"]);
        version.FindDesignGroup(lining.Id)!.FindOptionByCode(DesignCode.None)!.IsNone.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Padding")]
    [InlineData("PADDING")]
    [InlineData("1padding")]
    [InlineData("p")]
    [InlineData("none")]
    [InlineData("default")]
    public void RefusesAGroupCodeThatIsNotLowerSnakeCaseOrIsReserved(string code)
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");

        var refused = Group(version, blouse, code);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("catalog.design-group-code-not-well-formed");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("DEFAULT")]
    [InlineData("ALL")]
    [InlineData("UNKNOWN")]
    public void RefusesAnOptionCodeThatIsNotUpperSnakeCaseOrIsForbidden(string code)
    {
        var version = CatalogTestData.Draft();
        var group = Group(version, Category(version, "BLOUSE_PATTERN"), "padding").Value.Id;

        var refused = Option(version, group, code);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("catalog.design-option-code-not-well-formed");
    }

    [Fact]
    public void RefusesASecondGroupWithTheSameCodeInOneCategoryAndAllowsItInAnother()
    {
        // Section 2: the same group code in two categories means the same thing but is two records
        // with two option lists, because a gown offers sleeve lengths a blouse does not.
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var gown = Category(version, "GOWN");

        Group(version, blouse, "sleeve_style").IsSuccess.ShouldBeTrue();
        var duplicate = Group(version, blouse, "sleeve_style");
        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.Code.ShouldBe("catalog.code-not-unique");

        Group(version, gown, "sleeve_style").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void RefusesASecondOptionWithTheSameCodeInOneGroup()
    {
        var version = CatalogTestData.Draft();
        var group = Group(version, Category(version, "BLOUSE_PATTERN"), "front_neck").Value.Id;

        Option(version, group, "ROUND").IsSuccess.ShouldBeTrue();
        var duplicate = Option(version, group, "ROUND");

        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.Code.ShouldBe("catalog.code-not-unique");
    }

    [Fact]
    public void RefusesAnOptionWithoutAltTextOrHelpText()
    {
        var version = CatalogTestData.Draft();
        var group = Group(version, Category(version, "BLOUSE_PATTERN"), "front_neck").Value.Id;

        var noAlt = version.AddDesignOption(
            CatalogTestData.Id("o1"), CatalogTestData.Id("o1k"), group,
            OptionDetails("ROUND") with { IllustrationAlt = " " }, CatalogTestData.Now, null);
        noAlt.IsFailure.ShouldBeTrue();
        noAlt.Error.Code.ShouldBe("catalog.value-required");
        noAlt.Error.Target.ShouldBe("illustrationAlt");

        var noHelp = version.AddDesignOption(
            CatalogTestData.Id("o2"), CatalogTestData.Id("o2k"), group,
            OptionDetails("ROUND") with { HelpText = string.Empty }, CatalogTestData.Now, null);
        noHelp.IsFailure.ShouldBeTrue();
        noHelp.Error.Target.ShouldBe("helpText");
    }

    [Fact]
    public void RefusesAnIllustrationReferenceWithoutItsGroup()
    {
        // Section 6: the group is part of the anchor because an option code is unique only within its
        // group and several groups share one sheet.
        var version = CatalogTestData.Draft();
        var group = Group(version, Category(version, "KIDS"), "lining").Value.Id;

        var refused = version.AddDesignOption(
            CatalogTestData.Id("o1"), CatalogTestData.Id("o1k"), group,
            OptionDetails("FULL") with { IllustrationKey = "design_kids_style_v1#FULL" },
            CatalogTestData.Now, null);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("catalog.illustration-key-not-well-formed");

        // Well formed, and anchored on another option: the picker would show that option's drawing.
        var another = version.AddDesignOption(
            CatalogTestData.Id("o3"), CatalogTestData.Id("o3k"), group,
            OptionDetails("FULL") with { IllustrationKey = "design_kids_style_v1#lining.NONE" },
            CatalogTestData.Now, null);
        another.IsFailure.ShouldBeTrue();
        another.Error.Code.ShouldBe("catalog.illustration-key-not-for-this-option");

        version.AddDesignOption(
                CatalogTestData.Id("o2"), CatalogTestData.Id("o2k"), group,
                OptionDetails("FULL") with { IllustrationKey = "design_kids_style_v1#lining.FULL" },
                CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void RefusesATimeImpactAtTheEdgeOfWhatAnIntegerHoldsAsAValidationProblem()
    {
        var version = CatalogTestData.Draft();
        var group = Group(version, Category(version, "BLOUSE_PATTERN"), "padding").Value.Id;

        var refused = version.AddDesignOption(
            CatalogTestData.Id("o1"), CatalogTestData.Id("o1k"), group,
            OptionDetails("LIGHT") with { TimeImpactDays = int.MinValue }, CatalogTestData.Now, null);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("catalog.time-impact-out-of-range");
    }

    [Fact]
    public void RefusesARuleThatReadsAGroupOfAnotherCategory()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var gown = Category(version, "GOWN");
        Group(version, blouse, "padding");
        Group(version, gown, "lining");

        var refused = version.AddDesignRule(
            CatalogTestData.Id("dr"), CatalogTestData.Id("drk"), blouse, 1,
            Requires(
                new DesignRuleOperand("padding", DesignOperandForm.AnySelection, []),
                new DesignRuleOperand("lining", DesignOperandForm.In, ["FULL"])),
            CatalogTestData.Now, null);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("catalog.rule-group-not-in-category");
        refused.Error.Target.ShouldBe("consequent.groupCode");
    }

    [Fact]
    public void RefusesAMalformedOperandANoteWithoutTextAndARequiresWithoutAConsequent()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        Group(version, blouse, "padding");
        Group(version, blouse, "lining");

        var emptyIn = AddRule(version, blouse, 1, Requires(
            new DesignRuleOperand("padding", DesignOperandForm.In, []),
            new DesignRuleOperand("lining", DesignOperandForm.In, ["FULL"])));
        emptyIn.Error.Code.ShouldBe("catalog.rule-operand-malformed");

        var twoForEquals = AddRule(version, blouse, 1, Requires(
            new DesignRuleOperand("padding", DesignOperandForm.Equals, ["LIGHT", "MOULDED_CUP"]),
            new DesignRuleOperand("lining", DesignOperandForm.In, ["FULL"])));
        twoForEquals.Error.Code.ShouldBe("catalog.rule-operand-malformed");

        // DR-01 is `padding in (LIGHT, MOULDED_CUP) requires lining ≠ NONE`: a negation is an option
        // set like any other, and the document's own rules depend on it.
        var negativeConsequent = AddRule(version, blouse, 1, Requires(
            new DesignRuleOperand("padding", DesignOperandForm.AnySelection, []),
            new DesignRuleOperand("lining", DesignOperandForm.NotEquals, ["NONE"])));
        negativeConsequent.IsSuccess.ShouldBeTrue();
        negativeConsequent.Value.Statement.ShouldBe("any selection in padding requires lining ≠ NONE");

        var unconditionalConsequent = AddRule(version, blouse, 2, Requires(
            new DesignRuleOperand("padding", DesignOperandForm.AnySelection, []),
            new DesignRuleOperand("lining", DesignOperandForm.AnySelection, [])));
        unconditionalConsequent.Error.Code.ShouldBe("catalog.rule-operand-malformed");

        var unknownForm = AddRule(version, blouse, 2, Requires(
            new DesignRuleOperand("padding", (DesignOperandForm)(-1), []),
            new DesignRuleOperand("lining", DesignOperandForm.In, ["FULL"])));
        unknownForm.Error.Code.ShouldBe("catalog.rule-operand-malformed");
        unknownForm.Error.Target.ShouldBe("antecedent.form");

        var noteWithoutText = AddRule(version, blouse, 2, new DesignRuleDetails(
            DesignRuleType.Note,
            new DesignRuleOperand("padding", DesignOperandForm.Equals, ["MOULDED_CUP"]),
            null,
            null,
            null));
        noteWithoutText.Error.Code.ShouldBe("catalog.value-required");
        noteWithoutText.Error.Target.ShouldBe("note");

        var requiresWithoutConsequent = AddRule(version, blouse, 2, new DesignRuleDetails(
            DesignRuleType.Requires,
            new DesignRuleOperand("padding", DesignOperandForm.AnySelection, []),
            null,
            null,
            null));
        requiresWithoutConsequent.Error.Code.ShouldBe("catalog.rule-consequent-required");

        var noteWithConsequent = AddRule(version, blouse, 2, new DesignRuleDetails(
            DesignRuleType.RequiresAttachment,
            DesignRuleOperand.Always,
            new DesignRuleOperand("lining", DesignOperandForm.In, ["FULL"]),
            null,
            null));
        noteWithConsequent.Error.Code.ShouldBe("catalog.rule-consequent-not-allowed");
    }

    [Fact]
    public void AcceptsAnAlwaysRuleAndANoteAndSaysNeitherNamesAnOption()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        Group(version, blouse, "closure");

        var note = AddRule(version, blouse, 5, new DesignRuleDetails(
            DesignRuleType.Note,
            new DesignRuleOperand("closure", DesignOperandForm.In, ["ZIP_BACK", "ZIP_SIDE"]),
            null,
            "Match the zip tape to the shell fabric.",
            "Craft instruction, printed on the job card"));
        note.IsSuccess.ShouldBeTrue();
        note.Value.Blocks.ShouldBeFalse();
        note.Value.Consequent.ShouldBeNull();
        note.Value.Statement.ShouldBe("closure in (ZIP_BACK, ZIP_SIDE) → \"Match the zip tape to the shell fabric.\"");

        var always = AddRule(version, blouse, 6, new DesignRuleDetails(
            DesignRuleType.Note, DesignRuleOperand.Always, null, "Cut on the bias.", null));
        always.IsSuccess.ShouldBeTrue();
        always.Value.Antecedent.GroupCode.ShouldBeNull();
    }

    [Fact]
    public void RefusesARuleNumberAlreadyHeldByThisVersion()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        Group(version, blouse, "closure");
        var details = new DesignRuleDetails(
            DesignRuleType.Note, DesignRuleOperand.Always, null, "Cut on the bias.", null);

        AddRule(version, blouse, 3, details).IsSuccess.ShouldBeTrue();
        var refused = AddRule(version, blouse, 3, details);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("catalog.code-not-unique");
        version.HighestDesignRuleNumber.ShouldBe(3);
    }

    [Fact]
    public void CloningCarriesTheDesignWithFreshRowsAndTheSameKeysAndRemapsTheServiceLink()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var padding = Group(version, blouse, "padding").Value;
        var option = Option(version, padding.Id, "LIGHT").Value;
        var rule = AddRule(version, blouse, 1, new DesignRuleDetails(
            DesignRuleType.Note,
            new DesignRuleOperand("padding", DesignOperandForm.Equals, ["LIGHT"]),
            null,
            "Confirm the cup size.",
            null)).Value;
        var service = version.AddServiceType(
            CatalogTestData.Id("svc"), CatalogTestData.Id("svc-key"), blouse,
            CatalogTestData.ServiceOf("STITCHING") with { DesignOptionGroupIds = [padding.Id] },
            CatalogTestData.Now, null).Value;
        version.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var clone = version.CloneAsDraft(
            new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;

        var copiedGroup = clone.DesignGroups.ShouldHaveSingleItem();
        copiedGroup.Id.ShouldNotBe(padding.Id);
        copiedGroup.Key.ShouldBe(padding.Key);
        copiedGroup.CatalogVersionId.ShouldBe(clone.Id);
        copiedGroup.CategoryId.ShouldBe(clone.Categories.Single().Id);
        var copiedOption = copiedGroup.Options.ShouldHaveSingleItem();
        copiedOption.Id.ShouldNotBe(option.Id);
        copiedOption.Key.ShouldBe(option.Key);
        copiedOption.DesignOptionGroupId.ShouldBe(copiedGroup.Id);
        var copiedRule = clone.DesignRules.ShouldHaveSingleItem();
        copiedRule.Key.ShouldBe(rule.Key);
        copiedRule.Number.ShouldBe(1);
        copiedRule.CategoryId.ShouldBe(clone.Categories.Single().Id);

        // The service link names the copy, not the row of the version being cloned.
        var copiedService = clone.ServiceTypes.ShouldHaveSingleItem();
        copiedService.Key.ShouldBe(service.Key);
        copiedService.DesignOptionGroupIds.ShouldBe([copiedGroup.Id]);
    }

    [Fact]
    public void RemovingAGroupDropsItsRulesAndTheServiceLinkToIt()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var padding = Group(version, blouse, "padding").Value;
        var lining = Group(version, blouse, "lining").Value;
        AddRule(version, blouse, 1, Requires(
            new DesignRuleOperand("padding", DesignOperandForm.AnySelection, []),
            new DesignRuleOperand("lining", DesignOperandForm.In, ["FULL"]))).IsSuccess.ShouldBeTrue();
        AddRule(version, blouse, 2, new DesignRuleDetails(
            DesignRuleType.Note, new DesignRuleOperand("lining", DesignOperandForm.Equals, ["FULL"]),
            null, "Cut the lining wider.", null)).IsSuccess.ShouldBeTrue();
        var service = version.AddServiceType(
            CatalogTestData.Id("svc"), CatalogTestData.Id("svc-key"), blouse,
            CatalogTestData.ServiceOf("STITCHING") with { DesignOptionGroupIds = [padding.Id, lining.Id] },
            CatalogTestData.Now, null).Value;

        version.RemoveDesignGroup(padding.Id, CatalogTestData.Now, null).IsSuccess.ShouldBeTrue();

        version.DesignGroups.Select(group => group.Code).ShouldBe(["lining"]);
        version.DesignRules.Select(rule => rule.Number).ShouldBe([2], "the rule that read padding went with it");
        service.DesignOptionGroupIds.ShouldBe([lining.Id]);
    }

    [Fact]
    public void RemovingACategoryDropsItsGroupsAndRules()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var gown = Category(version, "GOWN");
        Group(version, blouse, "padding");
        Group(version, gown, "neckline");
        AddRule(version, blouse, 1, new DesignRuleDetails(
            DesignRuleType.Note, DesignRuleOperand.Always, null, "Blouse note.", null));
        AddRule(version, gown, 2, new DesignRuleDetails(
            DesignRuleType.Note, DesignRuleOperand.Always, null, "Gown note.", null));

        version.RemoveCategory(blouse, CatalogTestData.Now, null).IsSuccess.ShouldBeTrue();

        version.DesignGroups.Select(group => group.Code).ShouldBe(["neckline"]);
        version.DesignRules.Select(rule => rule.Number).ShouldBe([2]);
    }

    [Fact]
    public void RefusesEveryChangeOnAPublishedVersionAndAcceptsACorrectionOfTheWords()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var padding = Group(version, blouse, "padding").Value;
        var option = Option(version, padding.Id, "LIGHT").Value;
        var rule = AddRule(version, blouse, 1, new DesignRuleDetails(
            DesignRuleType.Note, DesignRuleOperand.Always, null, "Note.", null)).Value;
        version.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        Group(version, blouse, "lining").Error.Code.ShouldBe("catalog.version-not-editable");
        version.EditDesignGroup(padding.Id, padding.Details, CatalogTestData.Now, null)
            .Error.Code.ShouldBe("catalog.version-not-editable");
        version.RemoveDesignGroup(padding.Id, CatalogTestData.Now, null)
            .Error.Code.ShouldBe("catalog.version-not-editable");
        Option(version, padding.Id, "HEAVY").Error.Code.ShouldBe("catalog.version-not-editable");
        version.RemoveDesignOption(option.Id, CatalogTestData.Now, null)
            .Error.Code.ShouldBe("catalog.version-not-editable");
        version.EditDesignRule(rule.Id, rule.Details, CatalogTestData.Now, null)
            .Error.Code.ShouldBe("catalog.version-not-editable");
        version.RemoveDesignRule(rule.Id, CatalogTestData.Now, null)
            .Error.Code.ShouldBe("catalog.version-not-editable");

        var corrected = version.CorrectDesignOptionPresentation(
            option.Id,
            new DesignOptionPresentation(
                "Light padding", null, "A thin layer of padding.", "A thin, even layer under the cup.", 4),
            "Clearer words at the counter.",
            CatalogTestData.Now,
            null);
        corrected.IsSuccess.ShouldBeTrue();
        corrected.Value.Name.ShouldBe("Light padding");
        corrected.Value.Code.ShouldBe("LIGHT", "a correction changes what is shown and nothing anything refers to");

        version.CorrectDesignGroupPresentation(
                padding.Id, new DesignGroupPresentation("Padding", "பேடிங்", 2), string.Empty,
                CatalogTestData.Now, null)
            .Error.Code.ShouldBe("catalog.value-required");
    }

    [Fact]
    public void RefusesACorrectionOfTheWordsOnADraft()
    {
        var version = CatalogTestData.Draft();
        var padding = Group(version, Category(version, "BLOUSE_PATTERN"), "padding").Value;

        var refused = version.CorrectDesignGroupPresentation(
            padding.Id, new DesignGroupPresentation("Padding", null, 0), "Because.", CatalogTestData.Now, null);

        refused.Error.Code.ShouldBe("catalog.correction-needs-published-version");
    }

    private static Guid Category(CatalogVersion version, string code)
        => version.AddCategory(
                CatalogTestData.Id($"cat-{code}"),
                CatalogTestData.Id($"cat-key-{code}"),
                null,
                CatalogTestData.CategoryOf(code),
                CatalogTestData.Now,
                null)
            .Value.Id;

    private static Tailor360.Platform.Abstractions.Results.Result<DesignOptionGroup> Group(
        CatalogVersion version,
        Guid categoryId,
        string code,
        bool required = true,
        int displayOrder = 0)
        => version.AddDesignGroup(
            CatalogTestData.Id($"group-{categoryId}-{code}"),
            CatalogTestData.Id($"group-key-{categoryId}-{code}"),
            categoryId,
            new DesignGroupDetails(
                code,
                code.Replace('_', ' '),
                null,
                DesignSelectionMode.SingleChoice,
                required,
                displayOrder,
                null,
                null,
                [CatalogTestData.MainBranch]),
            CatalogTestData.Now,
            null);

    private static Tailor360.Platform.Abstractions.Results.Result<DesignOption> Option(
        CatalogVersion version,
        Guid groupId,
        string code)
        => version.AddDesignOption(
            CatalogTestData.Id($"option-{groupId}-{code}"),
            CatalogTestData.Id($"option-key-{groupId}-{code}"),
            groupId,
            OptionDetails(code),
            CatalogTestData.Now,
            null);

    private static DesignOptionDetails OptionDetails(string code)
        => new(
            code,
            code.Replace('_', ' '),
            null,
            "What the choice means for the garment.",
            null,
            "The shape in words.",
            null,
            0,
            0,
            true);

    private static DesignRuleDetails Requires(DesignRuleOperand antecedent, DesignRuleOperand consequent)
        => new(DesignRuleType.Requires, antecedent, consequent, null, "Because the tailor says so.");

    private static Tailor360.Platform.Abstractions.Results.Result<DesignRule> AddRule(
        CatalogVersion version,
        Guid categoryId,
        int number,
        DesignRuleDetails details)
        => version.AddDesignRule(
            CatalogTestData.Id($"rule-{number}-{details.GetHashCode()}"),
            CatalogTestData.Id($"rule-key-{number}-{details.GetHashCode()}"),
            categoryId,
            number,
            details,
            CatalogTestData.Now,
            null);
}
