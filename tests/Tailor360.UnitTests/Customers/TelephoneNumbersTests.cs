using Shouldly;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Naming;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// Reading a telephone number the way a counter writes one.
/// </summary>
/// <remarks>
/// Every number here is synthetic. The Indian mobile range used for documentation and test data is
/// 98430-xxxxx style; none of these is dialable and none belongs to anybody.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TelephoneNumbersTests
{
    [Theory]
    [InlineData("+91 90000 21174")]
    [InlineData("+919000021174")]
    [InlineData("0091 90000 21174")]
    [InlineData("90000 21174")]
    [InlineData("9000021174")]
    [InlineData("090000 21174")]
    [InlineData("(90000) 21174")]
    [InlineData("90000-21174")]
    [InlineData("90000.21174")]
    [InlineData("  9000021174  ")]
    public void EveryWayOneNumberIsWrittenReadsAsTheSameNumber(string written)
    {
        var read = TelephoneNumbers.TryRead(written, "phone");

        read.IsSuccess.ShouldBeTrue(read.IsFailure ? read.Error.Code : null);
        read.Value.E164.ShouldBe("+919000021174");
        read.Value.LastSix.ShouldBe("021174");
    }

    [Fact]
    public void ANumberThatAlreadyCarriesAnotherCountryCodeIsKept()
    {
        // Not this module's business to decide whether a foreign national number is well formed.
        // Refusing one because it does not look Indian is worse than storing it.
        var read = TelephoneNumbers.TryRead("+44 20 7946 0958", "phone");

        read.IsSuccess.ShouldBeTrue();
        read.Value.E164.ShouldBe("+442079460958");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingNumberIsAMissingValueAndNotAMalformedOne(string? written)
    {
        // Two different problems, so two different codes: a screen shows "this is required" in one
        // case and "check the digits" in the other.
        var read = TelephoneNumbers.TryRead(written, "phone");

        read.IsFailure.ShouldBeTrue();
        read.Error.Code.ShouldBe("customers.value-required");
        read.Error.Target.ShouldBe("phone");
    }

    /// <summary>
    /// A character that is not part of a written number is refused, not deleted.
    /// </summary>
    /// <remarks>
    /// Deleting it is the obvious implementation and the dangerous one: each of these would otherwise
    /// canonicalise to a real, dialable number that nobody typed, and the customer would then be
    /// messaged at it. The capital O is the one a counter actually types.
    /// </remarks>
    [Theory]
    [InlineData("90000O21174")]
    [InlineData("9000021174 ext 45")]
    [InlineData("call me on 9000021174")]
    [InlineData("9000021174x45")]
    [InlineData("90000 21174 (mobile)")]
    [InlineData("9000+021174")]
    public void ACharacterThatIsNotPartOfANumberIsRefusedRatherThanDeleted(string written)
    {
        var read = TelephoneNumbers.TryRead(written, "phone");

        read.IsFailure.ShouldBeTrue(
            read.IsSuccess ? $"it was read as {read.Value.E164}, which nobody typed" : null);
        read.Error.Code.ShouldBe("customers.phone-not-understood");
    }

    /// <summary>The separators a person really writes a number with are still ignored.</summary>
    [Theory]
    [InlineData("+91 90000 21174")]
    [InlineData("+91-90000-21174")]
    [InlineData("(+91) 90000/21174")]
    // A non-breaking space and an en dash, which is what a paste out of a word processor brings.
    [InlineData("+91\u00a090000\u201321174")]
    public void TheSeparatorsANumberIsWrittenWithAreStillIgnored(string written)
    {
        var read = TelephoneNumbers.TryRead(written, "phone");

        read.IsSuccess.ShouldBeTrue(read.IsFailure ? read.Error.Code : null);
        read.Value.E164.ShouldBe("+919000021174");
    }

    [Theory]
    [InlineData("90000")]
    [InlineData("12345")]
    [InlineData("no digits at all")]
    [InlineData("+9190000211749000021174123")]
    public void ANumberThatCouldNotBeDialledIsRefused(string written)
    {
        var read = TelephoneNumbers.TryRead(written, "alternatePhone");

        read.IsFailure.ShouldBeTrue();
        read.Error.Code.ShouldBe("customers.phone-not-understood");
        read.Error.Target.ShouldBe("alternatePhone");
    }

    [Fact]
    public void TheSearchTailIsTheLastSixDigits()
    {
        // Reception types the tail of the number, not the whole of it. Six digits is what the search
        // screen asks for, and storing them is what lets that search use an index.
        var read = TelephoneNumbers.TryRead("+91 90000 66315", "phone");

        read.Value.LastSix.ShouldBe("066315");
        read.Value.LastSix.Length.ShouldBe(TelephoneNumber.SearchTailLength);
    }

    [Fact]
    public void ReadingIsIdempotent()
    {
        // The canonical form is written to a column and read back through the same function when a
        // correction arrives. A form this could not re-read would make a stored number unmatchable.
        var once = TelephoneNumbers.TryRead("90000 21174", "phone").Value;

        TelephoneNumbers.TryRead(once.E164, "phone").Value.ShouldBe(once);
    }

    [Fact]
    public void TheErrorMessageNamesNoNumber()
    {
        // A telephone number is personal data and never appears in a problem detail, a log line or an
        // audit summary (docs/nfr/data-classification.md section 5.2).
        var read = TelephoneNumbers.TryRead("98430", "phone");

        read.Error.Message.ShouldNotContain("98430");
        CustomersErrors.PhoneNotUnderstood("phone").Message.ShouldNotContain("98430");
    }
}
