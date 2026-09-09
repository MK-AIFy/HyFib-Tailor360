using Shouldly;
using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// The shape of a machine key, and the four words it may not be.
/// </summary>
/// <remarks>
/// A code is what a price list, a report, an event payload, a seed file and an export refer to
/// (<c>docs/prd/category-hierarchy.md</c> section 4), which is why it is constrained at the point of
/// entry rather than tidied later: a published code can never be corrected.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class CatalogCodeTests
{
    [Theory]
    [InlineData("BLOUSE")]
    [InlineData("BLOUSE_PATTERN")]
    [InlineData("BLOUSE_AARI")]
    [InlineData("KIDS")]
    [InlineData("A1")]
    [InlineData("SERVICE_2026")]
    public void AcceptsUpperSnakeCase(string code)
        => CatalogCode.IsWellFormed(code).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("B")]
    [InlineData("blouse")]
    [InlineData("Blouse")]
    [InlineData("BLOUSE PATTERN")]
    [InlineData("BLOUSE-PATTERN")]
    [InlineData("_BLOUSE")]
    [InlineData("1BLOUSE")]
    [InlineData("ரவிக்கை")]
    public void RefusesAnythingElse(string? code)
        => CatalogCode.IsWellFormed(code).ShouldBeFalse();

    [Fact]
    public void RefusesACodeLongerThanTheColumn()
    {
        CatalogCode.IsWellFormed(new string('A', CatalogCode.MaximumLength)).ShouldBeTrue();
        CatalogCode.IsWellFormed(new string('A', CatalogCode.MaximumLength + 1)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("NONE")]
    [InlineData("DEFAULT")]
    [InlineData("ALL")]
    [InlineData("UNKNOWN")]
    public void RefusesTheWordsFiltersAndExportsAlreadyUse(string reserved)
    {
        // A category legitimately named "All" would collide with the export column meaning "every
        // category", and the collision would show up as a wrong total in a report rather than an error.
        CatalogCode.IsWellFormed(reserved).ShouldBeFalse();
        CatalogCode.Reserved.ShouldContain(reserved);
    }

    [Theory]
    [InlineData("BLOUSE_AARI", "BLOUSE", true)]
    [InlineData("BLOUSE_PATTERN", "BLOUSE", true)]
    [InlineData("AARI", "BLOUSE", false)]
    [InlineData("BLOUSE", "BLOUSE", false)]
    [InlineData("BLOUSEX", "BLOUSE", false)]
    public void ReadsLineageFromTheParentPrefix(string code, string parentCode, bool follows)
        => CatalogCode.FollowsParentPrefix(code, parentCode).ShouldBe(follows);
}
