using Shouldly;
using Tailor360.Modules.Customers.Domain.Naming;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The name-folding vectors published in <c>docs/customers/name-normalisation.md</c>.
/// </summary>
/// <remarks>
/// Every pair below is two spellings of one name that a counter would type differently, taken from
/// the transliteration variants the plan names — ksh/x, tch/ch, zh/l, v/w, doubled consonants,
/// final -i/-y/-ee and the optional h. The test is the document: if a rule changes, both change.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class CustomerNameNormaliserTests
{
    [Theory]
    [InlineData("Lakshmi", "Laxmi")]
    [InlineData("Lakshmi", "Lakshmy")]
    [InlineData("Lakshmi", "Lakshmee")]
    [InlineData("Shanthi", "Santhi")]
    [InlineData("Shanthi", "Shanti")]
    [InlineData("Muthu", "Muttu")]
    [InlineData("Vijaya", "Wijaya")]
    [InlineData("Selvam", "Sellvam")]
    [InlineData("Kavitha", "Kavita")]
    [InlineData("Revathi", "Revati")]
    [InlineData("Bhuvaneswari", "Bhuvaneshwari")]
    [InlineData("Deepa", "Dipa")]
    [InlineData("Tamizh", "Tamil")]
    [InlineData("Chitra", "Citra")]
    [InlineData("Poornima", "Purnima")]
    [InlineData("Satish", "Sathish")]
    [InlineData("Krishna", "Krisna")]
    [InlineData("Ashwin", "Ashvin")]
    [InlineData("Kaala", "Kala")]
    public void TwoSpellingsOfOneNameFoldToOneKey(string first, string second)
        => CustomerNameNormaliser.Normalise(first)
            .ShouldBe(CustomerNameNormaliser.Normalise(second));

    [Theory]
    [InlineData("Kavitha Raman", "kavita raman")]
    [InlineData("  Kavitha   Raman  ", "kavita raman")]
    [InlineData("Kavitha R.", "kavita r")]
    [InlineData("D'Souza", "d souza")]
    [InlineData("Anitha-Selvam", "anita selvam")]
    // The accent is what this vector is about, so it is written as an escape: it says exactly which
    // code point is meant without a reader having to trust the file's encoding.
    [InlineData("R\u00e9vathi", "revati")]
    public void TheKeyIsLowerCaseSingleSpacedAndUnaccented(string written, string expected)
        => CustomerNameNormaliser.Normalise(written).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0b95\u0bb5\u0bbf\u0ba4\u0bbe")]
    public void AnInputWithNoLatinLetterFoldsToNothing(string? written)
    {
        // A Tamil-script name is not transliterated here. Inventing a Latin spelling nobody chose
        // would put a guess in a search index; the native-script column is stored and searched on its
        // own instead.
        CustomerNameNormaliser.Normalise(written).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Kavitha", "Revathi")]
    [InlineData("Anitha", "Amitha")]
    [InlineData("Cheran", "Seran")]
    [InlineData("Ramesh", "Rajesh")]
    public void TwoDifferentNamesDoNotFoldTogether(string first, string second)
        => CustomerNameNormaliser.Normalise(first)
            .ShouldNotBe(CustomerNameNormaliser.Normalise(second));

    [Fact]
    public void FoldingIsIdempotent()
    {
        // The key is written to a column and compared against freshly folded input, so folding a key
        // again has to leave it alone. A rule that broke this would make a record unfindable by the
        // name it was saved under.
        var once = CustomerNameNormaliser.Normalise("Bhuvaneshwari Karthik");

        CustomerNameNormaliser.Normalise(once).ShouldBe(once);
    }

    [Fact]
    public void ALongNameIsTruncatedToTheColumnItIsStoredIn()
    {
        var key = CustomerNameNormaliser.Normalise(new string('a', CustomerNameNormaliser.MaximumLength * 2));

        key.Length.ShouldBeLessThanOrEqualTo(CustomerNameNormaliser.MaximumLength);
    }

    [Fact]
    public void EverySubstitutionCarriesTheSpellingPairItExistsFor()
    {
        // The `Because` text is published in the document, so an empty one would ship a rule nobody
        // can review.
        foreach (var substitution in CustomerNameNormaliser.Substitutions)
        {
            substitution.From.ShouldNotBeNullOrWhiteSpace();
            substitution.Because.Length.ShouldBeGreaterThan(10);
        }
    }

    [Fact]
    public void TheAccentFoldingTablesAreTheSameLength()
    {
        // Two parallel strings, so being one character out of step would fold half the alphabet to
        // the wrong letter and still compile. This is the only thing holding them together.
        LatinFolding.TablesAgree.ShouldBeTrue();
    }

    [Theory]
    [InlineData("\u1e47", "n")]
    [InlineData("\u1e6d", "t")]
    [InlineData("\u015b", "s")]
    [InlineData("\u00df", "ss")]
    [InlineData("\u00e6", "ae")]
    public void TheFoldingTableCoversTheLettersATransliteratedNameArrivesWith(string accented, string plain)
    {
        // Dot-below and macron letters are how a name copied from an identity document arrives, and
        // the sharp s and the ligatures are how a European name does. None of them can be handled by
        // Unicode normalisation here: the solution builds with InvariantGlobalization, under which
        // string.Normalize returns its input unchanged rather than failing.
        LatinFolding.Fold(accented).ShouldBe(plain);
    }
}
