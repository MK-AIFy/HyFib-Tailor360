using Shouldly;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Infrastructure.Sessions;

namespace Tailor360.UnitTests.Platform.Sessions;

/// <summary>
/// The value that goes in the session cookie and the digest that is stored instead of it. It is a pure
/// function with no database behind it, so it belongs in this tier.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SessionTokenFactoryTests
{
    [Fact]
    public void ATokenCarriesTwoHundredAndFiftySixBitsOfEntropy()
    {
        SessionTokenFactory.TokenByteLength.ShouldBe(32);

        // Base64url of 32 bytes, unpadded: 43 characters. Asserting the encoded length is what would
        // catch a future change that shortened the value without anyone noticing.
        SessionTokenFactory.CreateToken().Length.ShouldBe(43);
    }

    [Fact]
    public void ATokenIsSafeInACookieWithoutEscaping()
    {
        var token = SessionTokenFactory.CreateToken();

        token.ShouldAllBe(character =>
            char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_');
    }

    [Fact]
    public void EveryTokenDiffers()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 500; i++)
        {
            tokens.Add(SessionTokenFactory.CreateToken()).ShouldBeTrue();
        }
    }

    [Fact]
    public void TheDigestIsTheShapeTheStoreAndTheDatabaseBothRequire()
    {
        var digest = SessionTokenFactory.Digest(SessionTokenFactory.CreateToken());

        // The database restates this shape as a check constraint, so a token that reached the table in
        // a legible form would be refused there too.
        HashedSecret.IsWellFormed(digest).ShouldBeTrue();
    }

    [Fact]
    public void TheDigestIsStableAndDiffersPerToken()
    {
        var token = SessionTokenFactory.CreateToken();

        SessionTokenFactory.Digest(token).ShouldBe(SessionTokenFactory.Digest(token));
        SessionTokenFactory.Digest(token).ShouldNotBe(
            SessionTokenFactory.Digest(SessionTokenFactory.CreateToken()));
    }

    [Fact]
    public void TheDigestIsNotTheToken()
    {
        var token = SessionTokenFactory.CreateToken();

        // Stating the obvious on purpose: the stored value must not be the presented value, or a stolen
        // table would be a stolen set of live sessions.
        SessionTokenFactory.Digest(token).ShouldNotBe(token);
    }
}
