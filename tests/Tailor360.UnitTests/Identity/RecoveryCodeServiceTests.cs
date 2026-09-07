using Shouldly;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Infrastructure.Security;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// Recovery codes: what is printed, and what a person can type and still be let in.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RecoveryCodeServiceTests
{
    private readonly RecoveryCodeService _service = new();

    [Fact]
    public void ASheetIsDistinctAndStoredOnlyAsDigests()
    {
        var sheet = _service.Issue(10);

        sheet.Count.ShouldBe(10);
        sheet.Select(code => code.CodeHash).Distinct(StringComparer.Ordinal).Count().ShouldBe(10);
        sheet.ShouldAllBe(code => HashedSecret.IsWellFormed(code.CodeHash));

        // The digest is not the code with different letters: nothing about the printed code survives
        // into the stored value in a form anyone could read back.
        sheet.ShouldAllBe(code => !code.CodeHash.Contains(code.Code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrintedCodesAvoidTheCharactersThatAreMisreadOffPaper()
    {
        // Zero and O, one and I and L, U and V. A code nobody can transcribe is a code nobody can use
        // on the day they have lost their phone.
        RecoveryCodeService.Alphabet.ShouldNotContain("0");
        RecoveryCodeService.Alphabet.ShouldNotContain("O");
        RecoveryCodeService.Alphabet.ShouldNotContain("1");
        RecoveryCodeService.Alphabet.ShouldNotContain("I");
        RecoveryCodeService.Alphabet.ShouldNotContain("L");
        RecoveryCodeService.Alphabet.ShouldNotContain("U");

        var sheet = _service.Issue(8);
        foreach (var code in sheet)
        {
            code.Code.Replace("-", string.Empty, StringComparison.Ordinal).Length
                .ShouldBe(RecoveryCodeService.CodeLength);
            code.Code.Where(char.IsLetterOrDigit)
                .ShouldAllBe(character => RecoveryCodeService.Alphabet.Contains(character, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheSameCodeTypedAnyReasonableWayReachesTheSameDigest()
    {
        // Somebody reading a code off a printed sheet types the case their keyboard offers and whatever
        // they make of the group separator. All of these are the same code.
        var issued = _service.Issue(1)[0];
        var bare = issued.Code.Replace("-", string.Empty, StringComparison.Ordinal);

        _service.DigestOf(issued.Code).ShouldBe(issued.CodeHash);
        _service.DigestOf(bare).ShouldBe(issued.CodeHash);
        _service.DigestOf(bare.ToLowerInvariant()).ShouldBe(issued.CodeHash);
        _service.DigestOf($"  {issued.Code}  ").ShouldBe(issued.CodeHash);
        _service.DigestOf(string.Join(' ', bare.Chunk(2).Select(part => new string(part))))
            .ShouldBe(issued.CodeHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SHORT")]
    [InlineData("ABCDEFGHJKM")]
    [InlineData("ABCDEFGH0K")]
    [InlineData("ABCDEFGH!K")]
    public void AnythingThatIsNotACodeDigestsToNothing(string? submitted)
        => _service.DigestOf(submitted).ShouldBeNull();

    [Fact]
    public void TwoSheetsNeverShareACode()
    {
        var first = _service.Issue(10).Select(code => code.CodeHash).ToHashSet(StringComparer.Ordinal);
        var second = _service.Issue(10).Select(code => code.CodeHash);

        second.ShouldAllBe(hash => !first.Contains(hash));
    }
}
