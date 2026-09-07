using Shouldly;
using Tailor360.Modules.Customers.Domain.Deduplication;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// What makes one customer record look like another, and how strongly.
/// </summary>
/// <remarks>
/// Nothing here decides anything. Scoring produces a band and its reasons; a person reads them and
/// chooses. These tests are about whether the reasons are the right ones.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class DuplicateScoringTests
{
    private static DuplicateSubject Subject(
        string normalisedName = "kavita raman",
        string phone = "+919843021174",
        string? alternatePhone = null,
        string? nativeName = null,
        string? locality = "RS Puram",
        string? postcode = "641002")
        => new(normalisedName, nativeName, phone, alternatePhone, locality, postcode);

    [Fact]
    public void ASharedTelephoneNumberIsTheStrongestSignalTheShopHas()
    {
        var match = DuplicateScoring.Compare(
            Subject(normalisedName: "revati murugan", locality: "Saibaba Colony", postcode: "641011"),
            Subject());

        match.Confidence.ShouldBe(DuplicateConfidence.High);
        match.Reasons.ShouldContain(DuplicateReason.SharedTelephoneNumber);
    }

    [Fact]
    public void ANumberInEitherPositionCounts()
    {
        // A customer who gave her own number last year and her husband's this year is one customer.
        var match = DuplicateScoring.Compare(
            Subject(normalisedName: "anita selvam", phone: "+919111111111", alternatePhone: "+919843021174"),
            Subject());

        match.Confidence.ShouldBe(DuplicateConfidence.High);
    }

    [Fact]
    public void AMatchingNameAndAMatchingPlaceIsWorthReading()
    {
        var match = DuplicateScoring.Compare(Subject(phone: "+919111111111"), Subject());

        match.Confidence.ShouldBe(DuplicateConfidence.Medium);
        match.Reasons.ShouldContain(DuplicateReason.SameFoldedName);
        match.Reasons.ShouldContain(DuplicateReason.SameLocality);
    }

    [Fact]
    public void AMatchingNameAloneIsOnlyWorthAGlance()
    {
        var match = DuplicateScoring.Compare(
            Subject(phone: "+919111111111", locality: "Peelamedu", postcode: "641004"),
            Subject());

        match.Confidence.ShouldBe(DuplicateConfidence.Low);
    }

    [Fact]
    public void TheSameWordsInAnotherOrderStillMatch()
    {
        // Which name goes first depends on which form somebody filled in.
        var match = DuplicateScoring.Compare(
            Subject(normalisedName: "raman kavita", phone: "+919111111111"),
            Subject());

        match.Reasons.ShouldContain(DuplicateReason.SameWordsInAnotherOrder);
        match.Confidence.ShouldBe(DuplicateConfidence.Medium);
    }

    [Fact]
    public void ASharedAddressWithNoSharedNameIsNotADuplicate()
    {
        // A household, a hostel or a block of flats. Offering it as a duplicate would teach the
        // counter to dismiss the warning, which is worse than not showing one.
        var match = DuplicateScoring.Compare(
            Subject(normalisedName: "murugesan pandian", phone: "+919111111111"),
            Subject());

        match.Confidence.ShouldBe(DuplicateConfidence.None);
        match.Reasons.ShouldContain(DuplicateReason.SameLocality);
    }

    [Fact]
    public void TwoUnrelatedRecordsResembleEachOtherInNothing()
    {
        var match = DuplicateScoring.Compare(
            Subject(
                normalisedName: "murugesan pandian",
                phone: "+919111111111",
                locality: "Gandhipuram",
                postcode: "641012"),
            Subject());

        match.Confidence.ShouldBe(DuplicateConfidence.None);
        match.Reasons.ShouldBeEmpty();
    }

    [Fact]
    public void TwoRecordsWithNoNameKeyDoNotMatchOnTheEmptyString()
    {
        // A name written only in Tamil script folds to nothing. Two of those must not look like each
        // other just because neither has a Latin key.
        var match = DuplicateScoring.Compare(
            Subject(normalisedName: string.Empty, phone: "+919111111111", locality: null, postcode: null),
            Subject(normalisedName: string.Empty, locality: null, postcode: null));

        match.Confidence.ShouldBe(DuplicateConfidence.None);
    }

    [Fact]
    public void AMatchingNativeNameCountsAsANameMatch()
    {
        var tamil = "கவிதா";
        var match = DuplicateScoring.Compare(
            Subject(normalisedName: string.Empty, phone: "+919111111111", nativeName: tamil),
            Subject(normalisedName: string.Empty, nativeName: tamil));

        match.Reasons.ShouldContain(DuplicateReason.SameNativeName);
        match.Confidence.ShouldBe(DuplicateConfidence.Medium);
    }
}
