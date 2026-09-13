using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// <see cref="SeededDesignCatalogue"/> against <c>docs/prd/design-options.md</c> itself (issue #139): the
/// group and rule counts of section 1, and the <c>design.&lt;group_code&gt;</c> operand contract of
/// section 8 that the seeded measurement templates read.
/// </summary>
/// <remarks>
/// The group and rule counts are read from the document's own section 1 table rather than hard-coded,
/// so that a future edit to either the seed or the document surfaces here rather than the two silently
/// drifting apart.
/// </remarks>
[Trait("Category", "Unit")]
public sealed partial class SeededDesignCatalogueTests
{
    /// <summary>The six categories, in the order section 9 seeds them.</summary>
    private static readonly string[] CategoryOrder =
        ["BLOUSE_PATTERN", "BLOUSE_AARI", "SALWAR", "LEHENGA", "GOWN", "KIDS"];

    [Fact]
    public void GroupCountsMatchTheGroupsSection1DocumentsPerCategoryAndInTotal()
    {
        var document = DesignOptionsDocument.Load();
        var (total, perCategory) = document.SeededGroupCounts();

        perCategory.Count.ShouldBe(CategoryOrder.Length,
            "section 1 names one figure per orderable category");
        total.ShouldBe(perCategory.Sum(), "the document's own total is the sum of its own per-category figures");

        SeededDesignCatalogue.Groups.Count.ShouldBe(total);

        for (var index = 0; index < CategoryOrder.Length; index++)
        {
            SeededDesignCatalogue.Groups.Count(group => group.CategoryCode == CategoryOrder[index])
                .ShouldBe(perCategory[index], $"the seeded group count for {CategoryOrder[index]}");
        }
    }

    [Fact]
    public void RuleCountMatchesTheRulesSection1DocumentsAndTheNumbersAreDR01ToDRnnWithNoGapsOrDuplicates()
    {
        var document = DesignOptionsDocument.Load();
        var (total, highest) = document.SeededRuleCount();

        SeededDesignCatalogue.Rules.Count.ShouldBe(total);

        var numbers = SeededDesignCatalogue.Rules.Select(rule => rule.Number).Order().ToArray();
        numbers.ShouldBe(Enumerable.Range(1, highest).ToArray(),
            "DR-01 to DR-44 is a contiguous run with no gap and no identifier used twice (section 4 rule 7)");
        highest.ShouldBe(total, "the document's own 'DR-01 to DR-nn' span is exactly its own rule count");
    }

    [Theory]
    [InlineData("sleeve_style", "BLOUSE_PATTERN", DesignSelectionMode.SingleChoice)]
    [InlineData("sleeve_style", "BLOUSE_AARI", DesignSelectionMode.SingleChoice)]
    [InlineData("sleeve_style", "SALWAR", DesignSelectionMode.SingleChoice)]
    [InlineData("sleeve_style", "LEHENGA", DesignSelectionMode.SingleChoice)]
    [InlineData("sleeve_style", "GOWN", DesignSelectionMode.SingleChoice)]
    [InlineData("sleeve_style", "KIDS", DesignSelectionMode.SingleChoice)]
    [InlineData("aari_placement", "BLOUSE_AARI", DesignSelectionMode.MultipleChoice)]
    [InlineData("kameez_slit", "SALWAR", DesignSelectionMode.SingleChoice)]
    [InlineData("gown_slit", "GOWN", DesignSelectionMode.SingleChoice)]
    [InlineData("dupatta", "LEHENGA", DesignSelectionMode.SingleChoice)]
    public void EveryOperandTheSeededMeasurementTemplatesReadResolvesToASeededGroupOfTheRightCategoryAndMode(
        string groupCode, string categoryCode, DesignSelectionMode expectedMode)
    {
        // Section 8's table of design.<group_code> operands the seeded measurement templates depend on.
        var group = SeededDesignCatalogue.Groups.SingleOrDefault(
            candidate => candidate.CategoryCode == categoryCode && candidate.Code == groupCode);

        group.ShouldNotBeNull(
            $"design.{groupCode} must resolve to a seeded group of {categoryCode} (section 8)");
        group.SelectionMode.ShouldBe(expectedMode);
    }

    [Fact]
    public void NoGroupCodeRepeatsWithinACategoryAndNoOptionCodeRepeatsWithinAGroup()
    {
        foreach (var byCategory in SeededDesignCatalogue.Groups.GroupBy(group => group.CategoryCode))
        {
            byCategory.Select(group => group.Code).ShouldBeUnique(
                $"group codes must be unique within {byCategory.Key} (section 2)");
        }

        foreach (var group in SeededDesignCatalogue.Groups)
        {
            group.Options.Select(option => option.Code).ShouldBeUnique(
                $"option codes must be unique within {group.CategoryCode}.{group.Code} (section 2)");
        }
    }

    [Fact]
    public void EveryOptionCarriesAWellFormedCodeAndNonEmptyHelpTextAndAlternativeText()
    {
        foreach (var group in SeededDesignCatalogue.Groups)
        {
            DesignCode.IsWellFormedGroupCode(group.Code).ShouldBeTrue($"'{group.Code}' is a group code");

            foreach (var option in group.Options)
            {
                DesignCode.IsWellFormedOptionCode(option.Code).ShouldBeTrue(
                    $"'{group.CategoryCode}.{group.Code}.{option.Code}' is an option code");

                var illustrationKey = $"{group.IllustrationSheet}#{group.Code}.{option.Code}";
                DesignCode.IsWellFormedIllustrationKey(illustrationKey).ShouldBeTrue(illustrationKey);
                DesignCode.IllustrationKeyNames(illustrationKey, group.Code, option.Code).ShouldBeTrue();

                SeededDesignCatalogue.HelpTextFor(group.Name, option.Name).ShouldNotBeNullOrWhiteSpace();
                SeededDesignCatalogue.IllustrationAltFor(group.Name, option.Name).ShouldNotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public void EveryRuleReadsGroupsAndOptionsThatExistWithinItsOwnCategory()
    {
        var groupsByCategory = SeededDesignCatalogue.Groups
            .GroupBy(group => group.CategoryCode)
            .ToDictionary(
                byCategory => byCategory.Key,
                byCategory => byCategory.ToDictionary(group => group.Code, group => group));

        foreach (var rule in SeededDesignCatalogue.Rules)
        {
            var groups = groupsByCategory.ShouldContainKeyAndGetValue(rule.CategoryCode);

            foreach (var operand in new[] { rule.Antecedent, rule.Consequent })
            {
                if (operand?.GroupCode is not { } groupCode)
                {
                    continue;
                }

                var group = groups.ShouldContainKeyAndGetValue(groupCode,
                    $"DR-{rule.Number:00} reads '{groupCode}', which must be a group of {rule.CategoryCode}");

                foreach (var optionCode in operand.OptionCodes)
                {
                    group.Options.ShouldContain(
                        option => option.Code == optionCode,
                        $"DR-{rule.Number:00} names '{groupCode}.{optionCode}', which must be an option of that group");
                }
            }
        }
    }

    [GeneratedRegex(
        @"Seeded groups \|.*?categories\s*—\s*(?<list>[0-9, ]+and \d+)\s*—\s*all in catalog version",
        RegexOptions.Singleline)]
    private static partial Regex GroupCountsRow();

    [GeneratedRegex(@"Seeded rules \| (?<total>\d+) rules, `DR-01` to `DR-(?<highest>\d+)`")]
    private static partial Regex RuleCountRow();

    /// <summary>Reads the group and rule counts straight out of section 1 of the document.</summary>
    private sealed class DesignOptionsDocument(string text)
    {
        public const string RelativePath = "docs/prd/design-options.md";

        public static DesignOptionsDocument Load() => new(File.ReadAllText(FullPath));

        public static string FullPath => Path.Combine(RepositoryRoot(), RelativePath);

        public (int Total, IReadOnlyList<int> PerCategory) SeededGroupCounts()
        {
            var match = GroupCountsRow().Match(text);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"Could not find the 'Seeded groups' row of {RelativePath} section 1.");
            }

            // "9, 13, 8, 10, 10 and 6" -> [9, 13, 8, 10, 10, 6].
            var list = match.Groups["list"].Value.Replace(" and ", ", ", StringComparison.Ordinal);
            var perCategory = list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(cell => int.Parse(cell, CultureInfo.InvariantCulture))
                .ToArray();

            var totalMatch = Regex.Match(text, @"Seeded groups \| (\d+) groups across the six orderable categories");
            if (!totalMatch.Success)
            {
                throw new InvalidOperationException(
                    $"Could not find the total in the 'Seeded groups' row of {RelativePath} section 1.");
            }

            return (int.Parse(totalMatch.Groups[1].Value, CultureInfo.InvariantCulture), perCategory);
        }

        public (int Total, int Highest) SeededRuleCount()
        {
            var match = RuleCountRow().Match(text);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"Could not find the 'Seeded rules' row of {RelativePath} section 1.");
            }

            return (
                int.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["highest"].Value, CultureInfo.InvariantCulture));
        }

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

            throw new InvalidOperationException(
                "Could not locate the repository root: no HyFib.Tailor360.slnx above " + AppContext.BaseDirectory);
        }
    }
}

/// <summary>Small assertion helpers kept local to this file.</summary>
internal static class SeededDesignCatalogueTestExtensions
{
    public static void ShouldBeUnique<T>(this IEnumerable<T> values, string message)
    {
        var list = values.ToList();
        list.Distinct().Count().ShouldBe(list.Count, message);
    }

    public static TValue ShouldContainKeyAndGetValue<TKey, TValue>(
        this IDictionary<TKey, TValue> dictionary, TKey key, string? message = null)
        where TKey : notnull
    {
        dictionary.ContainsKey(key).ShouldBeTrue(message ?? $"expected key '{key}'");
        return dictionary[key];
    }
}
