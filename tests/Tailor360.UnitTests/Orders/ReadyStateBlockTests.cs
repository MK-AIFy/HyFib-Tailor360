using Shouldly;
using Tailor360.Modules.Orders.Contracts.Orders;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The one piece of executable code in the Orders <c>Contracts</c> project, and the whole of what keeps an
/// evidence or media identifier off a published read contract.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReadyStateBlock.Of"/> is the factory <c>IOrderSnapshotQuery</c>'s implementation must go through,
/// and it has no caller inside this repository yet — Orders' Infrastructure layer arrives with a later issue. An
/// unexercised guard is a claim rather than a guarantee, so every refusal is pinned here: a reference that is a
/// GUID in any spelling, one carrying anything but a code's characters, and one longer than the column.
/// </para>
/// <para>
/// The rule it enforces is <c>docs/nfr/data-classification.md</c> section 5.6, which classes QC and delivery
/// evidence images as sensitive personal data, and security rule 9 — media is never given a URL and never
/// travels as an identifier on another module's surface. A block names the <em>kind</em> of evidence a garment
/// still owes; it never names the evidence.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ReadyStateBlockTests
{
    /* What a block may carry ---------------------------------------------------------------------- */

    [Theory]
    [InlineData("finishing")]
    [InlineData("QC-000114")]
    [InlineData("J-CBE01-2627-000512-02")]
    [InlineData("fabric_short")]
    [InlineData("RECON-000007")]
    public void ACodeOrADisplayNumberIsCarried(string reference)
        => ReadyStateBlock.Of(ReadyBlockReason.WorkflowComplete, reference).Reference.ShouldBe(reference);

    [Fact]
    public void ABlockNeedNotNameAnything()
        => ReadyStateBlock.Of(ReadyBlockReason.QcPassed, null).Reference.ShouldBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AReferenceOfNothingButSpaceIsNoReferenceAtAll(string reference)
        => ReadyStateBlock.Of(ReadyBlockReason.QcPassed, reference).Reference.ShouldBeNull();

    [Fact]
    public void SurroundingSpaceIsTrimmedRatherThanRefused()
        => ReadyStateBlock.Of(ReadyBlockReason.NoOpenHold, "  fabric_short  ").Reference.ShouldBe("fabric_short");

    /* What it refuses ----------------------------------------------------------------------------- */

    [Theory]
    [InlineData("019bd660-4e55-7b76-9087-f60718293041")]
    [InlineData("019BD6604E557B769087F60718293041")]
    [InlineData("{019bd660-4e55-7b76-9087-f60718293041}")]
    [InlineData("(019bd660-4e55-7b76-9087-f60718293041)")]
    public void AnIdentityIsRefusedInEverySpellingAGuidCanBeWritten(string identity)
    {
        // A media object id and an evidence key are both UUIDv7, and the bare-hex spelling is 32 characters —
        // inside the 40-character bound — so a length rule alone would have let one through.
        var refused = Should.Throw<ArgumentException>(
            () => ReadyStateBlock.Of(ReadyBlockReason.DocumentationComplete, identity));

        refused.ParamName.ShouldBe("reference");
    }

    [Theory]
    [InlineData("the customer asked for it")]
    [InlineData("media/2026/09/019bd660.jpg")]
    [InlineData("qc-evidence.jpg")]
    [InlineData("C:\\evidence\\019bd660.png")]
    [InlineData("https://example.invalid/evidence")]
    [InlineData("-leading-hyphen")]
    [InlineData("_leading-underscore")]
    public void FreeTextAndAnObjectKeyAreBothRefused(string reference)
        => Should.Throw<ArgumentException>(
                () => ReadyStateBlock.Of(ReadyBlockReason.DocumentationComplete, reference))
            .ParamName.ShouldBe("reference");

    [Fact]
    public void AReferenceLongerThanTheColumnIsRefusedRatherThanTruncated()
    {
        var tooLong = new string('a', ReadyStateBlock.MaximumReferenceLength + 1);

        Should.Throw<ArgumentException>(
                () => ReadyStateBlock.Of(ReadyBlockReason.WorkflowComplete, tooLong))
            .ParamName.ShouldBe("reference");
    }

    [Fact]
    public void AReferenceExactlyTheLengthOfTheColumnIsCarried()
    {
        var atTheBound = new string('a', ReadyStateBlock.MaximumReferenceLength);

        ReadyStateBlock.Of(ReadyBlockReason.WorkflowComplete, atTheBound).Reference.ShouldBe(atTheBound);
    }

    [Fact]
    public void APredicateOutsideTheSixIsRefused()
    {
        // Section 9.1 fixes six predicates. An undefined value would reach the delivery queue as a reason code
        // no screen can render and no subscriber can branch on.
        Should.Throw<ArgumentOutOfRangeException>(
                () => ReadyStateBlock.Of((ReadyBlockReason)99, "finishing"))
            .ParamName.ShouldBe("reason");
    }

    [Fact]
    public void EveryPredicateOfSectionNinePointOneIsAccepted()
    {
        foreach (var reason in Enum.GetValues<ReadyBlockReason>())
        {
            ReadyStateBlock.Of(reason, "finishing").Reason.ShouldBe(reason);
        }
    }
}
