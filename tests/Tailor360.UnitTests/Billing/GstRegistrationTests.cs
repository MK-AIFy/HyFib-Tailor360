using Shouldly;
using Tailor360.Modules.Billing.Domain.Registrations;

namespace Tailor360.UnitTests.Billing;

/// <summary>The GST registration (#145): the GSTIN's shape and check character, dates, and overlap.</summary>
[Trait("Category", "Unit")]
public sealed class GstRegistrationTests
{
    [Theory]
    [InlineData("33AAACH7409R1Z8", true)]
    [InlineData("33AABCT1332L1ZL", true)]
    [InlineData("33AAACH7409R1ZW", false)] // the check character is wrong
    [InlineData("33AAACH7409R1YV", false)] // the fourteenth character is always Z
    [InlineData("33aaach7409r1z8", false)] // lower case is not a GSTIN; the request upper-cases before asking
    [InlineData("33AAACH7409R1Z", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void KnowsAWellFormedGstin(string? candidate, bool expected)
        => Gstin.IsWellFormed(candidate).ShouldBe(expected);

    [Fact]
    public void TheCheckCharacterFollowsThePublishedAlgorithm()
    {
        // Every prefix has exactly one check character, and changing any one character changes it
        // (a transposition of two adjacent characters is caught because the weights alternate).
        Gstin.CheckCharacter("33AAACH7409R1Z").ShouldBe('8');
        Gstin.CheckCharacter("33AAACH7409R1Z".Replace('7', '1')).ShouldNotBe('8');
        Gstin.CheckCharacter("33AAAHC7409R1Z").ShouldNotBe('8');
        // A made-up prefix with the check character the algorithm gives it is well formed; a
        // transposition inside it is not, because the weights alternate.
        Gstin.IsWellFormed("29ABCDE1234F1Z" + Gstin.CheckCharacter("29ABCDE1234F1Z")).ShouldBeTrue();
        Gstin.IsWellFormed("29ABDCE1234F1Z" + Gstin.CheckCharacter("29ABCDE1234F1Z")).ShouldBeFalse();
    }

    [Fact]
    public void RecordsARegistrationAndReadsItsDetailsBack()
    {
        var created = GstRegistration.Create(
            BillingTestData.Id("reg"), BillingTestData.Organisation,
            BillingTestData.Registration(gstin: "33aaach7409r1z8".ToUpperInvariant()), BillingTestData.Now, null);

        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        var registration = created.Value;
        registration.Gstin.ShouldBe(BillingTestData.WellFormedGstin);
        registration.StateCode.ShouldBe("33");
        registration.IsInForceOn(new DateOnly(2026, 4, 1)).ShouldBeTrue();
        registration.IsInForceOn(new DateOnly(2026, 3, 31)).ShouldBeFalse();
        registration.Details.ShouldBe(BillingTestData.Registration());
    }

    [Fact]
    public void RefusesAMalformedGstinAStateMismatchAndDatesOutOfOrder()
    {
        Create(BillingTestData.Registration(gstin: "33AAACH7409R1ZW")).Error.Code.ShouldBe("billing.gstin-not-well-formed");
        Create(BillingTestData.Registration(stateCode: "29")).Error.Code.ShouldBe("billing.gstin-state-mismatch");
        Create(BillingTestData.Registration(stateCode: "3")).Error.Code.ShouldBe("billing.state-code-not-well-formed");
        Create(BillingTestData.Registration(stateCode: "00", gstin: "00AAACH7409R1Z" + Gstin.CheckCharacter("00AAACH7409R1Z")))
            .Error.Code.ShouldBe("billing.state-code-not-well-formed");
        Create(BillingTestData.Registration(from: new DateOnly(2026, 4, 1), to: new DateOnly(2026, 3, 31)))
            .Error.Code.ShouldBe("billing.dates-not-ordered");
        // An omitted first day binds as the first day of year one, which is not a day anything is in force from.
        Create(BillingTestData.RegistrationWithoutAFirstDay()).Error.Target.ShouldBe("effectiveFrom");
        // The thirteenth character, the entity number, is never zero.
        Create(BillingTestData.Registration(gstin: "33AAACH7409R0Z" + Gstin.CheckCharacter("33AAACH7409R0Z")))
            .Error.Code.ShouldBe("billing.gstin-not-well-formed");
        Create(BillingTestData.Registration() with { LegalName = "" }).Error.Target.ShouldBe("legalName");
        Create(BillingTestData.Registration(branch: Guid.Empty)).Error.Target.ShouldBe("branchId");
    }

    [Fact]
    public void AnAmendmentKeepsTheBranchAndTheOverlapReadsBothOpenAndClosedRanges()
    {
        var registration = Create(BillingTestData.Registration(to: new DateOnly(2026, 12, 31))).Value;

        registration.Amend(BillingTestData.Registration(branch: BillingTestData.SecondBranch), BillingTestData.Now, null)
            .Error.Code.ShouldBe("billing.branch-immutable");
        registration.Amend(BillingTestData.Registration(to: null), BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        registration.EffectiveTo.ShouldBeNull();

        // Open-ended from April: overlaps anything that starts on or after April, and anything that
        // ends on or after April; a range that ended in March does not.
        registration.Overlaps(new DateOnly(2027, 1, 1), null).ShouldBeTrue();
        registration.Overlaps(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)).ShouldBeFalse();
        registration.Overlaps(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1)).ShouldBeTrue();

        registration.Amend(BillingTestData.Registration(to: new DateOnly(2026, 12, 31)), BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        registration.Overlaps(new DateOnly(2027, 1, 1), null).ShouldBeFalse();
        registration.Overlaps(new DateOnly(2026, 12, 31), null).ShouldBeTrue();
    }

    private static Tailor360.Platform.Abstractions.Results.Result<GstRegistration> Create(GstRegistrationDetails details)
        => GstRegistration.Create(BillingTestData.Id("reg"), BillingTestData.Organisation, details, BillingTestData.Now, null);
}
