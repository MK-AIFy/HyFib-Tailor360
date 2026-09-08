using System.Reflection;
using Shouldly;
using Tailor360.Modules.Customers.Domain.Consent;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The rules a consent purpose, its wording and a consent record hold.
/// </summary>
/// <remarks>
/// Two of these are structural rather than behavioural — that a published wording and a consent record
/// publish nothing that changes them. They are asserted by reflection because the property they hold is
/// the <em>absence</em> of a method, and the day somebody adds one is the day the record stops being
/// evidence. A test that only exercised the methods that exist could never notice.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ConsentTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 7, 11, 5, 0, TimeSpan.FromHours(5.5));

    private static readonly Guid Organisation = CustomersTestData.Organisation;

    /* Purposes ---------------------------------------------------------------------------------- */

    [Fact]
    public void ADefinedPurposeStartsWithNoWordingAndIsNotRetired()
    {
        var purpose = Purpose();

        purpose.Key.ShouldBe(ConsentPurposeKeys.MarketingMessages);
        purpose.CurrentWordingVersion.ShouldBe(0);
        purpose.IsRetired.ShouldBeFalse();
        purpose.Wordings.ShouldBeEmpty();
    }

    /// <summary>
    /// The key is written into every consent record and read by other modules, so it is held to a shape
    /// that survives a URL, a log line and a configuration file unaltered.
    /// </summary>
    [Theory]
    [InlineData("Marketing_Messages")]
    [InlineData("marketing messages")]
    [InlineData("marketing-messages")]
    [InlineData("marketing.messages")]
    [InlineData("marketing/messages")]
    public void APurposeKeyOutsideTheAllowedShapeIsRefused(string key)
    {
        var defined = ConsentPurpose.Define(
            Guid.NewGuid(), Organisation, key, "Marketing messages", null, Now);

        defined.IsFailure.ShouldBeTrue();
        defined.Error.Code.ShouldBe("customers.consent-purpose-key-not-allowed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void APurposeWithNoKeyIsRefused(string? key)
    {
        var defined = ConsentPurpose.Define(
            Guid.NewGuid(), Organisation, key, "Marketing messages", null, Now);

        defined.IsFailure.ShouldBeTrue();
        defined.Error.Code.ShouldBe("customers.value-required");
        defined.Error.Target.ShouldBe("key");
    }

    [Fact]
    public void APurposeWithNoNameIsRefused()
    {
        var defined = ConsentPurpose.Define(
            Guid.NewGuid(), Organisation, ConsentPurposeKeys.MarketingMessages, "  ", null, Now);

        defined.IsFailure.ShouldBeTrue();
        defined.Error.Target.ShouldBe("name");
    }

    [Fact]
    public void ValuesLongerThanTheColumnsThatHoldThemAreRefused()
    {
        ConsentPurpose.Define(
                Guid.NewGuid(),
                Organisation,
                new string('a', ConsentPurposeKeys.MaximumLength + 1),
                "Marketing messages",
                null,
                Now)
            .Error.Code.ShouldBe("customers.value-too-long");

        ConsentPurpose.Define(
                Guid.NewGuid(),
                Organisation,
                ConsentPurposeKeys.MarketingMessages,
                new string('a', ConsentPurpose.MaximumNameLength + 1),
                null,
                Now)
            .Error.Target.ShouldBe("name");

        ConsentPurpose.Define(
                Guid.NewGuid(),
                Organisation,
                ConsentPurposeKeys.MarketingMessages,
                "Marketing messages",
                new string('a', ConsentPurpose.MaximumDescriptionLength + 1),
                Now)
            .Error.Target.ShouldBe("description");
    }

    /* Wording ----------------------------------------------------------------------------------- */

    [Fact]
    public void WordingVersionsCountUpFromOne()
    {
        var purpose = Purpose();

        purpose.PublishWording(Guid.NewGuid(), "We may send you offers.", Now).Value.Version.ShouldBe(1);
        purpose.PublishWording(Guid.NewGuid(), "We may send you offers and news.", Now).Value.Version.ShouldBe(2);

        purpose.CurrentWordingVersion.ShouldBe(2);
        purpose.Wordings.Count.ShouldBe(2);
    }

    /// <summary>
    /// The version a record names keeps saying what it always said, whatever is published later.
    /// </summary>
    [Fact]
    public void PublishingANewWordingLeavesTheOldOneExactlyAsItWas()
    {
        var purpose = Purpose();
        var first = purpose.PublishWording(Guid.NewGuid(), "We may send you offers.", Now).Value;

        purpose.PublishWording(Guid.NewGuid(), "We may send you offers and news.", Now.AddYears(1));

        first.Version.ShouldBe(1);
        first.Text.ShouldBe("We may send you offers.");
        first.PublishedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AWordingWithNoWordsIsRefused(string? text)
    {
        var published = Purpose().PublishWording(Guid.NewGuid(), text, Now);

        published.IsFailure.ShouldBeTrue();
        published.Error.Target.ShouldBe("text");
    }

    [Fact]
    public void AWordingLongerThanTheColumnIsRefused()
    {
        var published = Purpose().PublishWording(
            Guid.NewGuid(), new string('a', ConsentWording.MaximumTextLength + 1), Now);

        published.IsFailure.ShouldBeTrue();
        published.Error.Code.ShouldBe("customers.value-too-long");
    }

    [Fact]
    public void AWordingVersionBelowTheFirstIsRefused()
    {
        var published = ConsentWording.Publish(Guid.NewGuid(), Guid.NewGuid(), 0, "Words.", Now);

        published.IsFailure.ShouldBeTrue();
        published.Error.Target.ShouldBe("version");
    }

    /* Retirement -------------------------------------------------------------------------------- */

    [Fact]
    public void ARetiredPurposeIsNotAskedAboutAndItsWordingDoesNotChange()
    {
        var purpose = Purpose();
        purpose.PublishWording(Guid.NewGuid(), "We may send you offers.", Now);

        purpose.Retire(Now, by: null).IsSuccess.ShouldBeTrue();
        purpose.IsRetired.ShouldBeTrue();

        // What customers already said stands: retiring removes nothing.
        purpose.Wordings.Count.ShouldBe(1);

        var published = purpose.PublishWording(Guid.NewGuid(), "Something else.", Now);
        published.IsFailure.ShouldBeTrue();
        published.Error.Code.ShouldBe("customers.consent-purpose-retired");
    }

    [Fact]
    public void RetiringAPurposeTwiceIsRefused()
    {
        var purpose = Purpose();
        purpose.Retire(Now, by: null);

        purpose.Retire(Now, by: null).Error.Code.ShouldBe("customers.consent-purpose-retired");
    }

    /* Records ----------------------------------------------------------------------------------- */

    [Theory]
    [InlineData(ConsentDecision.Granted)]
    [InlineData(ConsentDecision.Declined)]
    [InlineData(ConsentDecision.Withdrawn)]
    public void EveryDecisionIsRecordedTheSameWay(ConsentDecision decision)
    {
        var recorded = Record(decision);

        recorded.IsSuccess.ShouldBeTrue();
        recorded.Value.Decision.ShouldBe(decision);
        recorded.Value.WordingVersion.ShouldBe(1);
        recorded.Value.Source.ShouldBe("counter, verbal");
        recorded.Value.RecordedAt.ShouldBe(Now);
    }

    /// <summary>
    /// A record with no wording version behind it proves nothing: it says somebody agreed, and not to
    /// what.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARecordWithNoWordingVersionIsRefused(int version)
    {
        var recorded = ConsentRecord.Record(
            Guid.NewGuid(),
            Organisation,
            Guid.NewGuid(),
            ConsentPurposeKeys.MarketingMessages,
            version,
            ConsentDecision.Granted,
            "counter, verbal",
            branchId: null,
            Now,
            by: null);

        recorded.IsFailure.ShouldBeTrue();
        recorded.Error.Target.ShouldBe("wordingVersion");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ARecordWithNoSourceIsRefused(string? source)
    {
        var recorded = ConsentRecord.Record(
            Guid.NewGuid(),
            Organisation,
            Guid.NewGuid(),
            ConsentPurposeKeys.MarketingMessages,
            1,
            ConsentDecision.Granted,
            source,
            branchId: null,
            Now,
            by: null);

        recorded.IsFailure.ShouldBeTrue();
        recorded.Error.Target.ShouldBe("source");
    }

    [Fact]
    public void ARecordWithNoPurposeOrAnOverLongSourceIsRefused()
    {
        ConsentRecord.Record(
                Guid.NewGuid(), Organisation, Guid.NewGuid(), "  ", 1, ConsentDecision.Granted,
                "counter, verbal", null, Now, null)
            .Error.Target.ShouldBe("purposeKey");

        ConsentRecord.Record(
                Guid.NewGuid(), Organisation, Guid.NewGuid(), ConsentPurposeKeys.MarketingMessages, 1,
                ConsentDecision.Granted, new string('a', ConsentRecord.MaximumSourceLength + 1), null,
                Now, null)
            .Error.Code.ShouldBe("customers.value-too-long");
    }

    /* The structural guards --------------------------------------------------------------------- */

    /// <summary>
    /// A consent record publishes nothing that changes it.
    /// </summary>
    /// <remarks>
    /// <c>docs/nfr/data-classification.md</c> section 5.3: nobody may edit a historical consent record —
    /// a change is a new record. The database enforces that too, but a mutator here would mean the
    /// application had a way to try, and the day one is added is the day the record stops being
    /// evidence of what was actually said.
    /// </remarks>
    [Fact]
    public void AConsentRecordPublishesNothingThatChangesIt()
    {
        NothingMutable(typeof(ConsentRecord));
    }

    /// <summary>The same for a published wording, for the same reason one version out.</summary>
    [Fact]
    public void APublishedWordingPublishesNothingThatChangesIt()
    {
        NothingMutable(typeof(ConsentWording));
    }

    /// <summary>Every seeded key is one <see cref="ConsentPurpose.Define"/> will actually accept.</summary>
    [Fact]
    public void TheSeededPurposeKeysAreKeysTheDomainAccepts()
    {
        ConsentPurposeKeys.Seeded.Count.ShouldBe(5);
        ConsentPurposeKeys.Seeded.ShouldBeUnique();

        foreach (var key in ConsentPurposeKeys.Seeded)
        {
            ConsentPurpose.Define(Guid.NewGuid(), Organisation, key, "Seeded", null, Now)
                .IsSuccess.ShouldBeTrue(key);
        }
    }

    private static void NothingMutable(Type type)
    {
        var setters = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToArray();

        setters.ShouldBeEmpty($"{type.Name} publishes a settable property");

        // Anything but the object members and the property getters would be a way to change it.
        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToArray();

        methods.ShouldBeEmpty($"{type.Name} publishes an instance method that could change it");
    }

    private static ConsentPurpose Purpose()
        => ConsentPurpose.Define(
            Guid.NewGuid(),
            Organisation,
            ConsentPurposeKeys.MarketingMessages,
            "Marketing messages",
            "Anything that is not about an order in hand.",
            Now).Value;

    private static Tailor360.Platform.Abstractions.Results.Result<ConsentRecord> Record(
        ConsentDecision decision)
        => ConsentRecord.Record(
            Guid.NewGuid(),
            Organisation,
            Guid.NewGuid(),
            ConsentPurposeKeys.MarketingMessages,
            1,
            decision,
            "counter, verbal",
            branchId: null,
            Now,
            by: null);
}
