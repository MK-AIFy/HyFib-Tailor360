using Shouldly;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// The rule engine (#138) against the worked examples of <c>docs/prd/design-options.md</c> section 9 and
/// the three-state semantics of section 8: each operand form, each rule type, the fixed evaluation
/// order, the one auto-selection the document permits, and the properties two implementations must
/// share — determinism, order-independence of the input, no invented option.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DesignRuleEngineTests
{
    private static readonly DateOnly Today = new(2026, 9, 12);

    // The BLOUSE_PATTERN groups the seeded rules read (section 9.1), with the rules DR-01, DR-02, DR-04,
    // DR-05, DR-06 and DR-31 as the document states them.
    private static readonly Blouse Seed = Blouse.Build();

    [Fact]
    public void ANeatSelectionIsConfirmableAndCarriesNoNote()
    {
        // Walkthrough 1: katori cut with its cup lining, a deep round back, three-quarter sleeves, hooks.
        var evaluated = Evaluate(
            ("blouse_cut", ["KATORI"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND_DEEP"]),
            ("sleeve_style", ["THREE_QUARTER"]), ("closure", ["HOOK"]), ("lining", ["KATORI_CUP"]));

        evaluated.IsConfirmable.ShouldBeTrue(string.Join("; ", evaluated.Violations.Select(found => found.Message)));
        evaluated.Violations.ShouldBeEmpty();
        evaluated.AutoSelections.ShouldBeEmpty();
        evaluated.Notes.ShouldBeEmpty();
    }

    [Fact]
    public void AnUnsetGroupMakesEveryConditionFalseNegationsIncluded()
    {
        // DR-01 is satisfied vacuously when no padding is chosen (section 3 rule 2, section 8): the
        // antecedent is over an unset group. And `lining ≠ NONE` over an unset lining is false too, so
        // nothing about the lining is said — only the required group's own absence.
        var evaluated = Evaluate(
            ("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]));

        evaluated.Violations.Select(found => found.Code).ShouldBe(["design.required-group-unset"]);
        evaluated.Violations[0].GroupCode.ShouldBe("lining");
        evaluated.Violations[0].RuleIdentifier.ShouldBeNull();
    }

    [Fact]
    public void RequiresWithOneAdmissibleOptionSelectsItAndSaysSo()
    {
        // DR-02: `blouse_cut = KATORI requires lining = KATORI_CUP` — exactly one option, so rule 9 lets
        // the engine choose it on the customer's behalf, and the summary says so.
        var evaluated = Evaluate(
            ("blouse_cut", ["KATORI"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]));

        evaluated.IsConfirmable.ShouldBeTrue();
        var auto = evaluated.AutoSelections.ShouldHaveSingleItem();
        auto.RuleIdentifier.ShouldBe("DR-02");
        auto.GroupCode.ShouldBe("lining");
        auto.OptionCode.ShouldBe("KATORI_CUP");
        evaluated.Violations.ShouldBeEmpty("the auto-selection satisfies the required lining group");
    }

    [Fact]
    public void RequiresWithTwoAdmissibleOptionsIsAPromptNeverAChoice()
    {
        // DR-01: `padding in (LIGHT, MOULDED_CUP) requires lining ≠ NONE` — FULL or KATORI_CUP would do,
        // and choosing between a ₹70 lining and a ₹90 one is not the engine's to make.
        var evaluated = Evaluate(
            ("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]), ("padding", ["LIGHT"]));

        evaluated.IsConfirmable.ShouldBeFalse();
        evaluated.AutoSelections.ShouldBeEmpty();
        var prompt = evaluated.Violations.Single(found => found.Code == "design.requires-choice");
        prompt.RuleIdentifier.ShouldBe("DR-01");
        prompt.GroupCode.ShouldBe("lining");
        prompt.OptionCodes.ShouldBe(["FULL", "KATORI_CUP"]);
        prompt.RelatedGroupCode.ShouldBe("padding");
    }

    [Fact]
    public void RequiresIsSatisfiedByWhatWasChosenAndByWhatANegationAdmits()
    {
        var evaluated = Evaluate(
            ("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]), ("padding", ["LIGHT"]), ("lining", ["FULL"]));

        evaluated.IsConfirmable.ShouldBeTrue();
        evaluated.Violations.ShouldBeEmpty();

        var violated = Evaluate(
            ("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]), ("padding", ["LIGHT"]), ("lining", ["NONE"]));

        violated.IsConfirmable.ShouldBeFalse();
        violated.Violations.Single().RuleIdentifier.ShouldBe("DR-01");
    }

    [Fact]
    public void ExcludesRefusesBothSidesChosenAndNarrowsWhatRequiresMayPick()
    {
        // DR-04: sleeveless excludes lace edging; DR-31: a shaped sleeve excludes sleeveless.
        var evaluated = Evaluate(
            ("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["SLEEVELESS"]), ("closure", ["HOOK"]), ("lining", ["NONE"]),
            ("finish", ["PIPING", "LACE_EDGE"]), ("sleeve_shape", ["PUFF"]));

        evaluated.IsConfirmable.ShouldBeFalse();
        var codes = evaluated.Violations.Select(found => (found.Code, found.RuleIdentifier)).ToArray();
        codes.ShouldContain(("design.excluded", "DR-04"));
        codes.ShouldContain(("design.excluded", "DR-31"));
        evaluated.Violations.Single(found => found.RuleIdentifier == "DR-04").OptionCodes.ShouldBe(["LACE_EDGE"]);
    }

    [Fact]
    public void RequiresResolvesTransitivelyToAFixedPoint()
    {
        // A requires B, B requires C — each with exactly one admissible option — so selecting A yields all
        // three (section 4 rule 2), in rule order.
        var groups = Seed.Groups;
        var rules = new List<DesignRule>(Seed.Rules)
        {
            Rule(40, DesignRuleType.Requires,
                new DesignRuleOperand("lining", DesignOperandForm.Equals, ["FULL"]),
                new DesignRuleOperand("padding", DesignOperandForm.Equals, ["LIGHT"])),
            Rule(41, DesignRuleType.Requires,
                new DesignRuleOperand("padding", DesignOperandForm.Equals, ["LIGHT"]),
                new DesignRuleOperand("finish", DesignOperandForm.Includes, ["PIPING"])),
        };

        var evaluated = DesignRuleEngine.Evaluate(
            groups, rules,
            Selections(("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
                ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]), ("lining", ["FULL"])),
            CatalogTestData.MainBranch, Today, false);

        evaluated.IsConfirmable.ShouldBeTrue(string.Join("; ", evaluated.Violations.Select(found => found.Message)));
        evaluated.AutoSelections.Select(auto => (auto.RuleIdentifier, auto.OptionCode))
            .ShouldBe([("DR-40", "LIGHT"), ("DR-41", "PIPING")]);
        // DR-06's note fires only for the moulded cup; the light padding the chain added attaches none.
        evaluated.Notes.ShouldBeEmpty();
    }

    [Fact]
    public void ANoteNeverBlocksAndIsAttachedLast()
    {
        // DR-05 for a zip; DR-06 for a moulded cup, which DR-01 then requires a lining for.
        var evaluated = Evaluate(
            ("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["SHORT"]), ("closure", ["ZIP_BACK"]), ("padding", ["MOULDED_CUP"]),
            ("lining", ["FULL"]));

        evaluated.IsConfirmable.ShouldBeTrue();
        evaluated.Notes.Select(note => note.RuleIdentifier).ShouldBe(["DR-05", "DR-06"]);
        evaluated.Notes[0].Text.ShouldContain("zip tape");
    }

    [Fact]
    public void ARequiresAttachmentRuleReadsTheGarment()
    {
        var rules = new List<DesignRule>(Seed.Rules)
        {
            Rule(8, DesignRuleType.RequiresAttachment,
                new DesignRuleOperand("finish", DesignOperandForm.Includes, ["CONTRAST_BINDING"]), null),
        };
        var selections = Selections(
            ("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]), ("lining", ["NONE"]), ("finish", ["CONTRAST_BINDING"]));

        var without = DesignRuleEngine.Evaluate(Seed.Groups, rules, selections, CatalogTestData.MainBranch, Today, false);
        without.IsConfirmable.ShouldBeFalse();
        without.Violations.Single().Code.ShouldBe("design.reference-image-required");

        var with = DesignRuleEngine.Evaluate(Seed.Groups, rules, selections, CatalogTestData.MainBranch, Today, true);
        with.IsConfirmable.ShouldBeTrue();
    }

    [Fact]
    public void RefusesAnUnknownGroupAnUnknownOptionARetiredOptionAndTwoChoicesInASingleGroup()
    {
        var evaluated = Evaluate(
            ("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND", "V_NECK"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["CAPE"]), ("closure", ["HOOK"]), ("lining", ["NONE"]), ("collar", ["MANDARIN"]),
            ("sleeve_shape", ["FRILL"]));

        // In group order, whatever order the selections arrived in.
        evaluated.Violations.Select(found => (found.Code, found.GroupCode)).ShouldBe(
        [
            ("design.unknown-group", "collar"),
            ("design.too-many-selected", "front_neck"),
            ("design.option-not-offerable", "sleeve_shape"),
            ("design.unknown-option", "sleeve_style"),
            // The unknown option left the required sleeve group with nothing chosen, which is said too.
            ("design.required-group-unset", "sleeve_style"),
        ]);
    }

    [Fact]
    public void AGroupNotOfferedHereTodayIsNeitherRequiredNorSelectable()
    {
        // The finish group is offered only at the main branch; at the second branch it is neither
        // required nor selectable, and a required group outside its active period is not demanded.
        var evaluated = DesignRuleEngine.Evaluate(
            Seed.Groups, Seed.Rules,
            Selections(("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
                ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]), ("lining", ["NONE"]), ("finish", ["PIPING"])),
            CatalogTestData.SecondBranch, Today, false);

        evaluated.Violations.Select(found => found.Code).ShouldBe(["design.option-not-offerable"]);
    }

    [Fact]
    public void AnOptionARuleSelectedIsStillCheckedAgainstWhatAnotherRuleForbids()
    {
        // DR-42 obliges the one lace edge whenever the sleeve is sleeveless; DR-04 forbids exactly that
        // pair. The publish checks refuse the pair, but a version published before they existed must
        // still evaluate safely: the auto-selection is made, and then said to clash, never confirmable.
        var rules = new List<DesignRule>(Seed.Rules)
        {
            Rule(42, DesignRuleType.Requires,
                new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]),
                new DesignRuleOperand("finish", DesignOperandForm.Includes, ["LACE_EDGE"])),
        };

        var evaluated = DesignRuleEngine.Evaluate(
            Seed.Groups, rules,
            Selections(("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
                ("sleeve_style", ["SLEEVELESS"]), ("closure", ["HOOK"]), ("lining", ["NONE"])),
            CatalogTestData.MainBranch, Today, false);

        // DR-04 narrowed the finish group first, so DR-42 has nothing admissible left to pick.
        evaluated.IsConfirmable.ShouldBeFalse();
        evaluated.AutoSelections.ShouldBeEmpty();
        var unsatisfiable = evaluated.Violations.ShouldHaveSingleItem();
        unsatisfiable.Code.ShouldBe("design.requires-unsatisfiable");
        unsatisfiable.RuleIdentifier.ShouldBe("DR-42");
    }

    [Fact]
    public void AnAutoSelectionThatWakesAnExcludesIsSaidAsAClash()
    {
        // DR-43 adds the puff sleeve shape whenever the closure is a tie; DR-31 then forbids the
        // sleeveless style beside any shaped sleeve. Nothing the customer chose clashed on its own — the
        // clash is between what they chose and what a rule added, and it blocks.
        var rules = new List<DesignRule>(Seed.Rules)
        {
            Rule(43, DesignRuleType.Requires,
                new DesignRuleOperand("closure", DesignOperandForm.Equals, ["TIE_BACK"]),
                new DesignRuleOperand("sleeve_shape", DesignOperandForm.Equals, ["PUFF"])),
        };

        var evaluated = DesignRuleEngine.Evaluate(
            Seed.Groups, rules,
            Selections(("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
                ("sleeve_style", ["SLEEVELESS"]), ("closure", ["TIE_BACK"]), ("lining", ["NONE"])),
            CatalogTestData.MainBranch, Today, false);

        evaluated.AutoSelections.Select(auto => (auto.RuleIdentifier, auto.OptionCode)).ShouldBe([("DR-43", "PUFF")]);
        evaluated.IsConfirmable.ShouldBeFalse();
        var clash = evaluated.Violations.ShouldHaveSingleItem();
        clash.Code.ShouldBe("design.excluded");
        clash.RuleIdentifier.ShouldBe("DR-31");
        clash.OptionCodes.ShouldBe(["SLEEVELESS"]);
        clash.Message.ShouldContain("on the customer's behalf");
    }

    [Fact]
    public void ARequirementLeftOpenIsSettledByWhatALaterRuleSelects()
    {
        // DR-50 obliges a lining of either kind; DR-51, read later, obliges the full one. DR-50 alone
        // would be a prompt; DR-51 selects FULL, and that satisfies DR-50 too, so nothing is asked.
        var rules = new List<DesignRule>(Seed.Rules)
        {
            Rule(50, DesignRuleType.Requires,
                new DesignRuleOperand("padding", DesignOperandForm.Equals, ["LIGHT"]),
                new DesignRuleOperand("lining", DesignOperandForm.In, ["FULL", "KATORI_CUP"])),
            Rule(51, DesignRuleType.Requires,
                new DesignRuleOperand("padding", DesignOperandForm.Equals, ["LIGHT"]),
                new DesignRuleOperand("lining", DesignOperandForm.Equals, ["FULL"])),
        };

        var evaluated = DesignRuleEngine.Evaluate(
            Seed.Groups, rules,
            Selections(("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
                ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]), ("padding", ["LIGHT"])),
            CatalogTestData.MainBranch, Today, false);

        evaluated.IsConfirmable.ShouldBeTrue(string.Join("; ", evaluated.Violations.Select(found => found.Message)));
        evaluated.AutoSelections.Select(auto => (auto.RuleIdentifier, auto.OptionCode)).ShouldBe([("DR-51", "FULL")]);
        evaluated.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void AnExcludesWokenByAnAutoSelectionNarrowsWhatALaterRequiresMayPick()
    {
        // A tie closure obliges a puff sleeve (DR-60); a puff sleeve forbids piping (DR-61); the tie
        // also obliges one of piping or contrast binding (DR-62). Read together: PUFF is added, the
        // piping is forbidden, and the binding is the one option left — selected, not prompted.
        var rules = new List<DesignRule>(Seed.Rules)
        {
            Rule(60, DesignRuleType.Requires,
                new DesignRuleOperand("closure", DesignOperandForm.Equals, ["TIE_BACK"]),
                new DesignRuleOperand("sleeve_shape", DesignOperandForm.Equals, ["PUFF"])),
            Rule(61, DesignRuleType.Excludes,
                new DesignRuleOperand("sleeve_shape", DesignOperandForm.Equals, ["PUFF"]),
                new DesignRuleOperand("finish", DesignOperandForm.Includes, ["PIPING"])),
            Rule(62, DesignRuleType.Requires,
                new DesignRuleOperand("closure", DesignOperandForm.Equals, ["TIE_BACK"]),
                new DesignRuleOperand("finish", DesignOperandForm.In, ["PIPING", "CONTRAST_BINDING"])),
        };

        var evaluated = DesignRuleEngine.Evaluate(
            Seed.Groups, rules,
            Selections(("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
                ("sleeve_style", ["SHORT"]), ("closure", ["TIE_BACK"]), ("lining", ["NONE"])),
            CatalogTestData.MainBranch, Today, false);

        evaluated.IsConfirmable.ShouldBeTrue(string.Join("; ", evaluated.Violations.Select(found => found.Message)));
        evaluated.AutoSelections.Select(auto => (auto.RuleIdentifier, auto.OptionCode))
            .ShouldBe([("DR-60", "PUFF"), ("DR-62", "CONTRAST_BINDING")]);
        evaluated.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void ASingleChoiceGroupHoldingAnotherValueIsAConflictNotAPrompt()
    {
        // DR-02 obliges the katori cup, and the customer chose a full lining: one candidate, but the
        // group is single-choice and taken, so nothing is overwritten and the answer names what it holds.
        var evaluated = Evaluate(
            ("blouse_cut", ["KATORI"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
            ("sleeve_style", ["SHORT"]), ("closure", ["HOOK"]), ("lining", ["FULL"]));

        evaluated.IsConfirmable.ShouldBeFalse();
        evaluated.AutoSelections.ShouldBeEmpty();
        var conflict = evaluated.Violations.ShouldHaveSingleItem();
        conflict.Code.ShouldBe("design.requires-conflict");
        conflict.RuleIdentifier.ShouldBe("DR-02");
        conflict.GroupCode.ShouldBe("lining");
        conflict.OptionCodes.ShouldBe(["FULL"]);
        conflict.RelatedGroupCode.ShouldBe("blouse_cut");
    }

    [Fact]
    public void AnExcludesOverAnUnsetAntecedentDoesNotFireAndNeitherDoesAnAttachmentRule()
    {
        // DR-31 reads `sleeve_shape ≠ PLAIN`; with no shape chosen it is false, so a sleeveless style
        // stands. A requires-attachment rule over an unset group asks for nothing either.
        var rules = new List<DesignRule>(Seed.Rules)
        {
            Rule(8, DesignRuleType.RequiresAttachment,
                new DesignRuleOperand("finish", DesignOperandForm.Includes, ["CONTRAST_BINDING"]), null),
        };

        var evaluated = DesignRuleEngine.Evaluate(
            Seed.Groups, rules,
            Selections(("blouse_cut", ["PLAIN_DART"]), ("front_neck", ["ROUND"]), ("back_neck", ["ROUND"]),
                ("sleeve_style", ["SLEEVELESS"]), ("closure", ["HOOK"]), ("lining", ["NONE"])),
            CatalogTestData.MainBranch, Today, false);

        evaluated.IsConfirmable.ShouldBeTrue(string.Join("; ", evaluated.Violations.Select(found => found.Message)));
        evaluated.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void AGroupOutsideItsActiveDatesIsNeitherRequiredNorSelectable()
    {
        // A required group that opens tomorrow: today its option cannot be chosen and its absence is not
        // demanded; tomorrow both hold.
        var version = CatalogTestData.Draft();
        var category = version.AddCategory(
            CatalogTestData.Id("dated"), CatalogTestData.Id("dated-key"), null,
            CatalogTestData.CategoryOf("BLOUSE_PATTERN"), CatalogTestData.Now, null).Value.Id;
        var group = version.AddDesignGroup(
            CatalogTestData.Id("g-hem"), CatalogTestData.Id("gk-hem"), category,
            new DesignGroupDetails("hem", "Hem", null, DesignSelectionMode.SingleChoice, true, 0,
                Today.AddDays(1), null, [CatalogTestData.MainBranch]),
            CatalogTestData.Now, null).Value;
        version.AddDesignOption(
            CatalogTestData.Id("o-hem"), CatalogTestData.Id("ok-hem"), group.Id,
            new DesignOptionDetails("STRAIGHT", "Straight", null, "Help.", null, "Alt.", null, 0, 0, true),
            CatalogTestData.Now, null).IsSuccess.ShouldBeTrue();
        var groups = version.DesignGroupsOf(category).ToArray();
        var chosen = Selections(("hem", ["STRAIGHT"]));

        var today = DesignRuleEngine.Evaluate(groups, [], chosen, CatalogTestData.MainBranch, Today, false);
        today.Violations.Select(found => found.Code).ShouldBe(["design.option-not-offerable"]);
        DesignRuleEngine.Evaluate(groups, [], [], CatalogTestData.MainBranch, Today, false).Violations.ShouldBeEmpty();

        var tomorrow = DesignRuleEngine.Evaluate(groups, [], chosen, CatalogTestData.MainBranch, Today.AddDays(1), false);
        tomorrow.IsConfirmable.ShouldBeTrue();
        DesignRuleEngine.Evaluate(groups, [], [], CatalogTestData.MainBranch, Today.AddDays(1), false)
            .Violations.Select(found => found.Code).ShouldBe(["design.required-group-unset"]);
    }

    [Fact]
    public void TheSameInputsGiveTheSameAnswerWhateverTheOrderOfTheSelections()
    {
        var random = new Random(20260912);
        var pool = Seed.Groups.SelectMany(group => group.Options.Select(option => (group.Code, option.Code))).ToArray();

        for (var round = 0; round < 200; round++)
        {
            var picked = pool.OrderBy(_ => random.Next()).Take(random.Next(0, 9))
                .GroupBy(pair => pair.Item1)
                .Select(byGroup => (byGroup.Key, byGroup.Select(pair => pair.Item2).ToArray()))
                .ToArray();

            var forward = DesignRuleEngine.Evaluate(
                Seed.Groups, Seed.Rules, Selections(picked), CatalogTestData.MainBranch, Today, round % 2 == 0);
            var reversed = DesignRuleEngine.Evaluate(
                Seed.Groups, Seed.Rules, Selections([.. picked.Reverse()]), CatalogTestData.MainBranch, Today, round % 2 == 0);
            var again = DesignRuleEngine.Evaluate(
                Seed.Groups, Seed.Rules, Selections(picked), CatalogTestData.MainBranch, Today, round % 2 == 0);

            Flatten(reversed).ShouldBe(Flatten(forward), $"round {round}: the input order changed the answer");
            Flatten(again).ShouldBe(Flatten(forward), $"round {round}: two evaluations disagreed");

            // No violation, auto-selection or note ever names an option the version does not hold, and an
            // auto-selection is always exactly one admissible option of a group the rule read.
            foreach (var violation in forward.Violations.Where(found => found.Code != "design.unknown-group"
                                                                        && found.Code != "design.unknown-option"))
            {
                foreach (var code in violation.OptionCodes)
                {
                    pool.ShouldContain((violation.GroupCode!, code));
                }

                violation.OptionCodes.ShouldBe([.. violation.OptionCodes.Order(StringComparer.Ordinal)], "codes are listed in order");
            }

            foreach (var auto in forward.AutoSelections)
            {
                pool.ShouldContain((auto.GroupCode, auto.OptionCode));
            }
        }
    }

    /// <summary>The whole answer as one string, so two answers compare by value rather than by list identity.</summary>
    private static string Flatten(DesignEvaluationResult result)
        => string.Join(
            "\n",
            [
                result.IsConfirmable.ToString(),
                .. result.Violations.Select(found =>
                    $"{found.Code}|{found.RuleIdentifier}|{found.GroupCode}|{string.Join(",", found.OptionCodes)}|"
                    + $"{found.RelatedGroupCode}|{string.Join(",", found.RelatedOptionCodes)}|{found.Blocks}"),
                .. result.AutoSelections.Select(auto => $"auto|{auto.RuleIdentifier}|{auto.GroupCode}|{auto.OptionCode}"),
                .. result.Notes.Select(note => $"note|{note.RuleIdentifier}|{note.Text}"),
            ]);

    private static DesignEvaluationResult Evaluate(params (string Group, string[] Options)[] selections)
        => DesignRuleEngine.Evaluate(
            Seed.Groups, Seed.Rules, Selections(selections), CatalogTestData.MainBranch, Today, false);

    private static IReadOnlyList<DesignSelectionInput> Selections(params (string Group, string[] Options)[] selections)
        => [.. selections.Select(selection => new DesignSelectionInput(selection.Group, selection.Options))];

    private static DesignRule Rule(int number, DesignRuleType type, DesignRuleOperand antecedent, DesignRuleOperand? consequent, string? note = null)
    {
        var version = CatalogTestData.Draft();
        var category = version.AddCategory(
            CatalogTestData.Id("cat"), CatalogTestData.Id("cat-key"), null,
            CatalogTestData.CategoryOf("BLOUSE_PATTERN"), CatalogTestData.Now, null).Value.Id;
        foreach (var code in new[] { "blouse_cut", "front_neck", "back_neck", "sleeve_style", "sleeve_shape", "closure", "lining", "padding", "finish" })
        {
            version.AddDesignGroup(
                CatalogTestData.Id($"g-{code}"), CatalogTestData.Id($"gk-{code}"), category,
                new DesignGroupDetails(code, code, null, DesignSelectionMode.SingleChoice, false, 0, null, null, [CatalogTestData.MainBranch]),
                CatalogTestData.Now, null).IsSuccess.ShouldBeTrue();
        }

        var added = version.AddDesignRule(
            CatalogTestData.Id($"r-{number}"), CatalogTestData.Id($"rk-{number}"), category, number,
            new DesignRuleDetails(type, antecedent, consequent, note, null), CatalogTestData.Now, null);
        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Message : string.Empty);
        return added.Value;
    }

    /// <summary>The seeded BLOUSE_PATTERN groups and rules of section 9.1, built through the aggregate.</summary>
    private sealed class Blouse
    {
        public required IReadOnlyCollection<DesignOptionGroup> Groups { get; init; }

        public required IReadOnlyCollection<DesignRule> Rules { get; init; }

        public static Blouse Build()
        {
            var version = CatalogTestData.Draft();
            var blouse = version.AddCategory(
                CatalogTestData.Id("blouse"), CatalogTestData.Id("blouse-key"), null,
                CatalogTestData.CategoryOf("BLOUSE_PATTERN", branches: [CatalogTestData.MainBranch, CatalogTestData.SecondBranch]),
                CatalogTestData.Now, null).Value.Id;

            Group(version, blouse, "blouse_cut", true, DesignSelectionMode.SingleChoice, 0, ["PLAIN_DART", "PRINCESS_CUT", "KATORI", "PAITHANI"]);
            Group(version, blouse, "front_neck", true, DesignSelectionMode.SingleChoice, 1, ["ROUND", "DEEP_ROUND", "V_NECK", "SWEETHEART", "BOAT", "HIGH_NECK", "SQUARE"]);
            Group(version, blouse, "back_neck", true, DesignSelectionMode.SingleChoice, 2, ["ROUND", "ROUND_DEEP", "V_DEEP", "U_DEEP", "KEYHOLE", "HIGH_NECK"]);
            Group(version, blouse, "sleeve_style", true, DesignSelectionMode.SingleChoice, 3, ["SLEEVELESS", "CAP", "SHORT", "ELBOW", "THREE_QUARTER", "FULL"]);
            Group(version, blouse, "sleeve_shape", false, DesignSelectionMode.SingleChoice, 4, ["PLAIN", "PUFF", "BELL", "FRILL"], retired: ["FRILL"]);
            Group(version, blouse, "closure", true, DesignSelectionMode.SingleChoice, 5, ["HOOK", "HOOK_FRONT", "ZIP_BACK", "ZIP_SIDE", "TIE_BACK"]);
            Group(version, blouse, "lining", true, DesignSelectionMode.SingleChoice, 6, ["NONE", "FULL", "KATORI_CUP"]);
            Group(version, blouse, "padding", false, DesignSelectionMode.SingleChoice, 7, ["NONE", "LIGHT", "MOULDED_CUP"]);
            Group(version, blouse, "finish", false, DesignSelectionMode.MultipleChoice, 8, ["PIPING", "CONTRAST_BINDING", "LACE_EDGE"], branches: [CatalogTestData.MainBranch]);

            Add(version, blouse, 1, DesignRuleType.Requires,
                new DesignRuleOperand("padding", DesignOperandForm.In, ["LIGHT", "MOULDED_CUP"]),
                new DesignRuleOperand("lining", DesignOperandForm.NotEquals, ["NONE"]));
            Add(version, blouse, 2, DesignRuleType.Requires,
                new DesignRuleOperand("blouse_cut", DesignOperandForm.Equals, ["KATORI"]),
                new DesignRuleOperand("lining", DesignOperandForm.Equals, ["KATORI_CUP"]));
            Add(version, blouse, 4, DesignRuleType.Excludes,
                new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]),
                new DesignRuleOperand("finish", DesignOperandForm.Includes, ["LACE_EDGE"]));
            Add(version, blouse, 5, DesignRuleType.Note,
                new DesignRuleOperand("closure", DesignOperandForm.In, ["ZIP_BACK", "ZIP_SIDE"]), null,
                "Match the zip tape to the shell fabric; check the zip runs freely after lining.");
            Add(version, blouse, 6, DesignRuleType.Note,
                new DesignRuleOperand("padding", DesignOperandForm.Equals, ["MOULDED_CUP"]), null,
                "Confirm the cup size against the customer's reference garment before cutting.");
            Add(version, blouse, 31, DesignRuleType.Excludes,
                new DesignRuleOperand("sleeve_shape", DesignOperandForm.NotEquals, ["PLAIN"]),
                new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]));

            return new Blouse { Groups = [.. version.DesignGroupsOf(blouse)], Rules = [.. version.DesignRulesOf(blouse)] };
        }

        private static void Group(
            CatalogVersion version,
            Guid category,
            string code,
            bool required,
            DesignSelectionMode mode,
            int order,
            string[] options,
            string[]? retired = null,
            IReadOnlyCollection<Guid>? branches = null)
        {
            var group = version.AddDesignGroup(
                CatalogTestData.Id($"group-{code}"), CatalogTestData.Id($"group-key-{code}"), category,
                new DesignGroupDetails(code, code, null, mode, required, order, null, null,
                    branches ?? [CatalogTestData.MainBranch, CatalogTestData.SecondBranch]),
                CatalogTestData.Now, null);
            group.IsSuccess.ShouldBeTrue(group.IsFailure ? group.Error.Message : string.Empty);

            var position = 0;
            foreach (var option in options)
            {
                version.AddDesignOption(
                        CatalogTestData.Id($"option-{code}-{option}"), CatalogTestData.Id($"option-key-{code}-{option}"),
                        group.Value.Id,
                        new DesignOptionDetails(option, option, null, "Help.", null, "Alt.", null, 0, position++,
                            retired is null || !retired.Contains(option)),
                        CatalogTestData.Now, null)
                    .IsSuccess.ShouldBeTrue();
            }
        }

        private static void Add(CatalogVersion version, Guid category, int number, DesignRuleType type, DesignRuleOperand antecedent, DesignRuleOperand? consequent, string? note = null)
        {
            var added = version.AddDesignRule(
                CatalogTestData.Id($"rule-{number}"), CatalogTestData.Id($"rule-key-{number}"), category, number,
                new DesignRuleDetails(type, antecedent, consequent, note, null), CatalogTestData.Now, null);
            added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Message : string.Empty);
        }
    }
}
