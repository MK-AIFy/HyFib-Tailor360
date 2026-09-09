using Shouldly;
using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The rule language, exercised on the rules the seeded templates actually use.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConditionalRuleTests
{
    private static readonly IReadOnlyDictionary<string, string> NoValues =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> NoSelections =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

    [Fact]
    public void ASleeveFieldIsHiddenOnlyWhenTheGarmentIsSleeveless()
    {
        // Hidden when design.sleeve_style = SLEEVELESS — the rule every seeded template carries.
        var rule = new ConditionalRule(
            RuleEffect.HiddenWhen,
            [new RuleClause(RuleScope.DesignSelection, "sleeve_style", RuleOperator.IsAnyOf, ["SLEEVELESS"])]);

        Evaluate(rule, ("sleeve_style", "SLEEVELESS")).IsShown.ShouldBeFalse();
        Evaluate(rule, ("sleeve_style", "CAP")).IsShown.ShouldBeTrue();
    }

    [Fact]
    public void AFieldWhoseRuleCannotBeDecidedIsShownAndSaysSo()
    {
        // Measurements taken at the counter outside an order have no design selections. Hiding on missing
        // information would drop a measurement the tailor needs; showing an unnecessary one costs a skipped field.
        var rule = new ConditionalRule(
            RuleEffect.HiddenWhen,
            [new RuleClause(RuleScope.DesignSelection, "sleeve_style", RuleOperator.IsAnyOf, ["SLEEVELESS"])]);

        var evaluation = rule.Evaluate(NoValues, NoSelections);

        evaluation.IsShown.ShouldBeTrue();
        evaluation.Decided.ShouldBeFalse("the capture wizard marks an undecided field optional");
    }

    [Fact]
    public void AShownWhenRuleReadsAnotherFieldOfTheSameVersion()
    {
        // Shown when waist_finish is ELASTIC or BOTH — the salwar elastic length.
        var rule = new ConditionalRule(
            RuleEffect.ShownWhen,
            [new RuleClause(RuleScope.Field, "waist_finish", RuleOperator.IsAnyOf, ["ELASTIC", "BOTH"])]);

        rule.Evaluate(Values(("waist_finish", "ELASTIC")), NoSelections).IsShown.ShouldBeTrue();
        rule.Evaluate(Values(("waist_finish", "BOTH")), NoSelections).IsShown.ShouldBeTrue();
        rule.Evaluate(Values(("waist_finish", "DRAWSTRING")), NoSelections).IsShown.ShouldBeFalse();
    }

    [Fact]
    public void AnExcludesClauseReadsAMultipleSelection()
    {
        // Hidden when design.aari_placement excludes FRONT — the placement is a set, not a single value.
        var rule = new ConditionalRule(
            RuleEffect.HiddenWhen,
            [new RuleClause(RuleScope.DesignSelection, "aari_placement", RuleOperator.Excludes, ["FRONT"])]);

        Evaluate(rule, ("aari_placement", "FRONT", "NECK")).IsShown.ShouldBeTrue();
        Evaluate(rule, ("aari_placement", "BACK", "NECK")).IsShown.ShouldBeFalse();
    }

    [Fact]
    public void ClausesAreCombinedWithOr()
    {
        // Hidden when design.sleeve_style = SLEEVELESS or design.aari_placement excludes SLEEVE — either hides it.
        var rule = new ConditionalRule(
            RuleEffect.HiddenWhen,
            [
                new RuleClause(RuleScope.DesignSelection, "sleeve_style", RuleOperator.IsAnyOf, ["SLEEVELESS"]),
                new RuleClause(RuleScope.DesignSelection, "aari_placement", RuleOperator.Excludes, ["SLEEVE"]),
            ]);

        var selections = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["sleeve_style"] = new HashSet<string>(StringComparer.Ordinal) { "CAP" },
            ["aari_placement"] = new HashSet<string>(StringComparer.Ordinal) { "SLEEVE", "FRONT" },
        };

        rule.Evaluate(NoValues, selections).IsShown.ShouldBeTrue();

        selections["sleeve_style"] = new HashSet<string>(StringComparer.Ordinal) { "SLEEVELESS" };
        rule.Evaluate(NoValues, selections).IsShown.ShouldBeFalse();
    }

    [Fact]
    public void AClauseThatMatchesDecidesEvenWhenAnotherOperandIsUnset()
    {
        // Or only needs one true clause. A rule that waited for every operand would leave a field showing that the
        // selections already say should be hidden.
        var rule = new ConditionalRule(
            RuleEffect.HiddenWhen,
            [
                new RuleClause(RuleScope.DesignSelection, "sleeve_style", RuleOperator.IsAnyOf, ["SLEEVELESS"]),
                new RuleClause(RuleScope.DesignSelection, "dupatta", RuleOperator.IsAnyOf, ["NONE"]),
            ]);

        Evaluate(rule, ("sleeve_style", "SLEEVELESS")).IsShown.ShouldBeFalse();
    }

    [Fact]
    public void ARuleWithNoClausesAlwaysDecidesTheSameWay()
    {
        new ConditionalRule(RuleEffect.HiddenWhen, []).HidesUnconditionally.ShouldBeTrue();
        new ConditionalRule(RuleEffect.HiddenWhen, []).Evaluate(NoValues, NoSelections).IsShown.ShouldBeFalse();
        new ConditionalRule(RuleEffect.ShownWhen, []).Evaluate(NoValues, NoSelections).IsShown.ShouldBeTrue();
    }

    [Fact]
    public void ARuleThatComparesAgainstNothingIsRefused()
    {
        var key = FieldKey.Create("sleeve_length").Value;
        var rule = new ConditionalRule(
            RuleEffect.HiddenWhen,
            [new RuleClause(RuleScope.DesignSelection, "sleeve_style", RuleOperator.IsAnyOf, [])]);

        rule.Validate(key).Error.Code.ShouldBe("measurements.clause-has-no-values");
    }

    [Fact]
    public void AClauseThatMatchesSettlesTheRuleEvenWhileAnotherOperandIsUnreadable()
    {
        // Shown when the waist is elastic, or when the design says churidar. At the counter, outside an
        // order, the design half cannot be read at all — but the field half already said yes, and an OR
        // whose left side is true is true whatever the right side turns out to be.
        var rule = new ConditionalRule(
            RuleEffect.ShownWhen,
            [
                new RuleClause(RuleScope.Field, "waist_finish", RuleOperator.IsAnyOf, ["ELASTIC"]),
                new RuleClause(RuleScope.DesignSelection, "leg_style", RuleOperator.IsAnyOf, ["CHURIDAR"]),
            ]);

        var evaluation = rule.Evaluate(Values(("waist_finish", "ELASTIC")), NoSelections);

        evaluation.IsShown.ShouldBeTrue();
        evaluation.Decided.ShouldBeTrue(
            "a matching clause is conclusive, and reporting it undecided would let the wizard make a "
            + "required field optional");
    }

    [Fact]
    public void AHiddenWhenRuleIsAlsoSettledByTheClauseThatMatched()
    {
        // The same argument in the other direction: one matching clause hides the field for certain, so
        // nothing about the unreadable clause can bring it back.
        var rule = new ConditionalRule(
            RuleEffect.HiddenWhen,
            [
                new RuleClause(RuleScope.Field, "waist_finish", RuleOperator.IsAnyOf, ["DRAWSTRING"]),
                new RuleClause(RuleScope.DesignSelection, "leg_style", RuleOperator.IsAnyOf, ["CHURIDAR"]),
            ]);

        var evaluation = rule.Evaluate(Values(("waist_finish", "DRAWSTRING")), NoSelections);

        evaluation.IsShown.ShouldBeFalse();
        evaluation.Decided.ShouldBeTrue();
    }

    [Fact]
    public void ARuleStaysUndecidedWhileNothingHasMatchedAndSomethingIsUnreadable()
    {
        // The guard on the two tests above: it is *matching* that settles a disjunction, not merely having
        // read one clause. A clause that was read and said no leaves the unread one still deciding.
        var rule = new ConditionalRule(
            RuleEffect.ShownWhen,
            [
                new RuleClause(RuleScope.Field, "waist_finish", RuleOperator.IsAnyOf, ["ELASTIC"]),
                new RuleClause(RuleScope.DesignSelection, "leg_style", RuleOperator.IsAnyOf, ["CHURIDAR"]),
            ]);

        var evaluation = rule.Evaluate(Values(("waist_finish", "DRAWSTRING")), NoSelections);

        evaluation.IsShown.ShouldBeTrue("an undecidable rule shows the field");
        evaluation.Decided.ShouldBeFalse();
    }

    private static RuleEvaluation Evaluate(ConditionalRule rule, params (string Group, string First, string? Second)[] selections)
    {
        var map = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var (group, first, second) in selections)
        {
            var set = new HashSet<string>(StringComparer.Ordinal) { first };

            if (second is not null)
            {
                set.Add(second);
            }

            map[group] = set;
        }

        return rule.Evaluate(NoValues, map);
    }

    private static RuleEvaluation Evaluate(ConditionalRule rule, (string Group, string Value) selection)
        => Evaluate(rule, (selection.Group, selection.Value, (string?)null));

    private static Dictionary<string, string> Values(params (string Key, string Value)[] values)
        => values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
