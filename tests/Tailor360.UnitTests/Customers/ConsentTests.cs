using System.Reflection;
using Shouldly;
using Tailor360.Modules.Customers.Application.Consent;
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

    /// <summary>
    /// The version sequence belongs to the purpose, and nothing outside it can publish a wording.
    /// </summary>
    /// <remarks>
    /// A publicly reachable factory would let a handler or a seeder publish for a retired purpose,
    /// repeat a version or skip one, and every consent record naming the resulting version would point
    /// at wording nobody could reconstruct. This asserts the type publishes no factory at all — which
    /// is also why the test above has to go through <see cref="ConsentPurpose.PublishWording"/>.
    /// </remarks>
    [Fact]
    public void NothingOutsideThePurposeCanPublishAWording()
    {
        typeof(ConsentWording)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .ShouldBeEmpty("ConsentWording publishes a factory the aggregate does not control");
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

    /// <summary>
    /// An outcome that is none of the three is refused, because it is uninterpretable rather than
    /// weaker: the query that reads the latest record to decide whether a message may be sent would
    /// have nothing to say about it. A value outside the enumeration arrives from a cast, which is
    /// exactly what a deserialiser does with a number it did not recognise.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(99)]
    public void ADecisionThatIsNoneOfTheOutcomesIsRefused(int raw)
    {
        var recorded = ConsentRecord.Record(
            Guid.NewGuid(),
            Organisation,
            Guid.NewGuid(),
            ConsentPurposeKeys.MarketingMessages,
            1,
            (ConsentDecision)raw,
            "counter, verbal",
            branchId: null,
            Now,
            by: null);

        recorded.IsFailure.ShouldBeTrue();
        recorded.Error.Code.ShouldBe("customers.consent-decision-not-understood");
        recorded.Error.Target.ShouldBe("decision");
    }

    /// <summary>
    /// A record is held to the same key rule as the purpose it names. One that names a key no purpose
    /// could ever have cannot be resolved back to what the customer was actually asked.
    /// </summary>
    [Theory]
    [InlineData("Marketing_Messages")]
    [InlineData("marketing messages")]
    [InlineData("marketing-messages")]
    public void ARecordNamingAKeyNoPurposeCouldHaveIsRefused(string key)
    {
        var recorded = ConsentRecord.Record(
            Guid.NewGuid(),
            Organisation,
            Guid.NewGuid(),
            key,
            1,
            ConsentDecision.Granted,
            "counter, verbal",
            branchId: null,
            Now,
            by: null);

        recorded.IsFailure.ShouldBeTrue();
        recorded.Error.Code.ShouldBe("customers.consent-purpose-key-not-allowed");
        recorded.Error.Target.ShouldBe("purposeKey");
    }

    [Fact]
    public void ARecordNamingAKeyLongerThanTheColumnIsRefused()
    {
        var recorded = ConsentRecord.Record(
            Guid.NewGuid(),
            Organisation,
            Guid.NewGuid(),
            new string('a', ConsentPurposeKeys.MaximumLength + 1),
            1,
            ConsentDecision.Granted,
            "counter, verbal",
            branchId: null,
            Now,
            by: null);

        recorded.IsFailure.ShouldBeTrue();
        recorded.Error.Code.ShouldBe("customers.value-too-long");
        recorded.Error.Target.ShouldBe("purposeKey");
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

    /// <summary>
    /// Renaming corrects the register and touches nothing a customer said.
    /// </summary>
    /// <remarks>
    /// A consent record names the key, and the key does not change — which is what lets a shop reword
    /// the label on a counter screen without anybody's stored answer coming to mean something
    /// different.
    /// </remarks>
    [Fact]
    public void RenamingAPurposeChangesTheLabelAndNotTheKey()
    {
        var purpose = Purpose();
        purpose.PublishWording(Guid.NewGuid(), "We may send you offers.", Now);

        purpose.Rename("Offers and news", "Anything not about an order in hand.", Now.AddDays(1), by: null)
            .ShouldBeTrue();

        purpose.Name.ShouldBe("Offers and news");
        purpose.Description.ShouldBe("Anything not about an order in hand.");
        purpose.Key.ShouldBe(ConsentPurposeKeys.MarketingMessages);
        purpose.CurrentWordingVersion.ShouldBe(1);
        purpose.UpdatedAt.ShouldBe(Now.AddDays(1));
    }

    /// <summary>
    /// The seeder runs on every deployment, so a rename that changes nothing must report nothing.
    /// </summary>
    [Fact]
    public void RenamingAPurposeToWhatItAlreadySaysChangesNothing()
    {
        var purpose = Purpose();

        purpose.Rename(
                "Marketing messages",
                "Anything that is not about an order in hand.",
                Now.AddDays(1),
                by: null)
            .ShouldBeFalse();

        purpose.UpdatedAt.ShouldBe(Now);
    }

    /// <summary>
    /// Every purpose the seeder writes is one the domain would accept if it were typed in by hand.
    /// </summary>
    /// <remarks>
    /// The seeder throws rather than writing half a register if this ever stops being true, so this is
    /// the test that keeps an install from being the thing that discovers it.
    /// </remarks>
    [Fact]
    public void EverySeededPurposeIsOneTheDomainAccepts()
    {
        SeededConsentPurposes.All.Count.ShouldBe(ConsentPurposeKeys.Seeded.Count);
        SeededConsentPurposes.All.Select(purpose => purpose.Key)
            .ShouldBe(ConsentPurposeKeys.Seeded, ignoreOrder: true);

        foreach (var seeded in SeededConsentPurposes.All)
        {
            var defined = ConsentPurpose.Define(
                Guid.NewGuid(), Organisation, seeded.Key, seeded.Name, seeded.Description, Now);

            defined.IsSuccess.ShouldBeTrue(
                defined.IsFailure ? $"{seeded.Key}: {defined.Error.Code}" : seeded.Key);
        }
    }

    /// <summary>
    /// The seeder publishes no wording, so nothing it writes can be consented to until an Owner
    /// publishes words that were actually reviewed. DC-01 enforced rather than mentioned.
    /// </summary>
    [Fact]
    public void ASeededPurposeCannotBeConsentedToUntilItHasWording()
    {
        var seeded = SeededConsentPurposes.All[0];
        var purpose = ConsentPurpose.Define(
            Guid.NewGuid(), Organisation, seeded.Key, seeded.Name, seeded.Description, Now).Value;

        purpose.CurrentWordingVersion.ShouldBe(0);

        // A record must name a version, and version zero is not one.
        ConsentRecord.Record(
                Guid.NewGuid(),
                Organisation,
                Guid.NewGuid(),
                purpose.Key,
                purpose.CurrentWordingVersion,
                ConsentDecision.Granted,
                "counter, verbal",
                branchId: null,
                Now,
                by: null)
            .IsFailure.ShouldBeTrue();
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
