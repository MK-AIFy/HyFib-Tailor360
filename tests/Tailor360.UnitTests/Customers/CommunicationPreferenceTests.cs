using Shouldly;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Preferences;

namespace Tailor360.UnitTests.Customers;

/// <summary>How a customer wants to be reached, and the window they would rather not be.</summary>
[Trait("Category", "Unit")]
public sealed class CommunicationPreferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 7, 11, 5, 0, TimeSpan.FromHours(5.5));

    /* Channels ---------------------------------------------------------------------------------- */

    /// <summary>
    /// Allowing nothing is an answer, not an absence. It is how a customer says "do not message me"
    /// without withdrawing consent to the purposes themselves — she may still want her measurements
    /// kept and still be told things at the counter.
    /// </summary>
    [Fact]
    public void AllowingNoChannelIsAValidPreference()
    {
        var preferences = Preferences([]);

        preferences.AllowedChannels.ShouldBeEmpty();
        preferences.Allows(CommunicationChannel.Sms).ShouldBeFalse();
        preferences.Allows(CommunicationChannel.Email).ShouldBeFalse();
    }

    /// <summary>
    /// Two requests saying the same thing produce the same row and the same audit entry, whatever
    /// order the channels arrived in and however many times each was repeated.
    /// </summary>
    [Fact]
    public void ChannelsAreDeduplicatedAndOrderedStably()
    {
        var one = Preferences([
            CommunicationChannel.Email,
            CommunicationChannel.Sms,
            CommunicationChannel.Email,
        ]);

        var other = Preferences([CommunicationChannel.Sms, CommunicationChannel.Email]);

        one.AllowedChannels.ShouldBe([CommunicationChannel.Sms, CommunicationChannel.Email]);
        one.AllowedChannels.ShouldBe(other.AllowedChannels);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void AChannelTheShopCannotSendOnIsRefused(int raw)
    {
        var recorded = CommunicationPreferences.Record(
            Guid.NewGuid(), Guid.NewGuid(), [(CommunicationChannel)raw], null, null, Now, by: null);

        recorded.IsFailure.ShouldBeTrue();
        recorded.Error.Code.ShouldBe("customers.communication-channel-not-understood");
    }

    /// <summary>
    /// The whole set, not a list of additions, so the audit entry reads as a state rather than as a
    /// difference.
    /// </summary>
    [Fact]
    public void ReplacingThePreferenceReplacesTheWholeChannelSet()
    {
        var preferences = Preferences([CommunicationChannel.Sms, CommunicationChannel.Email]);

        preferences.Replace([CommunicationChannel.WhatsApp], "ta-IN", null, Now.AddDays(1), by: null)
            .IsSuccess.ShouldBeTrue();

        preferences.AllowedChannels.ShouldBe([CommunicationChannel.WhatsApp]);
        preferences.Language.ShouldBe("ta-IN");
        preferences.UpdatedAt.ShouldBe(Now.AddDays(1));
    }

    /* Language ---------------------------------------------------------------------------------- */

    [Fact]
    public void ThePreferenceFallsBackToTheDefaultLanguage()
    {
        Preferences([]).Language.ShouldBe(CustomerDetails.DefaultLanguage);
    }

    [Theory]
    [InlineData("hi-IN")]
    [InlineData("klingon")]
    public void ALanguageTheProductDoesNotServeIsRefused(string language)
    {
        var recorded = CommunicationPreferences.Record(
            Guid.NewGuid(), Guid.NewGuid(), [], language, null, Now, by: null);

        recorded.IsFailure.ShouldBeTrue();
        recorded.Error.Code.ShouldBe("customers.language-not-supported");
    }

    /* Quiet hours ------------------------------------------------------------------------------- */

    [Fact]
    public void NoQuietHoursIsNoWindowRatherThanAnInventedOne()
    {
        var window = QuietHours.TryRead(null, null);

        window.IsSuccess.ShouldBeTrue();
        window.Value.ShouldBeNull();
    }

    /// <summary>
    /// Half a window is not a preference anybody could act on, and guessing the missing end would put a
    /// time the customer never gave into a rule that stops messages reaching them.
    /// </summary>
    [Theory]
    [InlineData(21, null)]
    [InlineData(null, 8)]
    public void OneEndOfAWindowWithoutTheOtherIsRefused(int? start, int? end)
    {
        var window = QuietHours.TryRead(
            start is null ? null : new TimeOnly(start.Value, 0),
            end is null ? null : new TimeOnly(end.Value, 0));

        window.IsFailure.ShouldBeTrue();
        window.Error.Code.ShouldBe("customers.quiet-hours-incomplete");
    }

    [Fact]
    public void AWindowThatStartsAndEndsAtTheSameTimeIsRefused()
    {
        var window = QuietHours.TryRead(new TimeOnly(21, 30), new TimeOnly(21, 30));

        window.IsFailure.ShouldBeTrue();
        window.Error.Code.ShouldBe("customers.quiet-hours-empty");
    }

    /// <summary>
    /// The ordinary case runs backwards over midnight, so it is the case the type is built for rather
    /// than the exception it refuses.
    /// </summary>
    [Theory]
    [InlineData(22, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(7, 59, true)]
    [InlineData(8, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(21, 29, false)]
    [InlineData(21, 30, true)]
    public void AWindowOverMidnightCoversTheEveningAndTheEarlyMorning(int hour, int minute, bool covered)
    {
        var window = QuietHours.TryRead(new TimeOnly(21, 30), new TimeOnly(8, 0)).Value.ShouldNotBeNull();

        window.Covers(new TimeOnly(hour, minute)).ShouldBe(covered);
    }

    [Theory]
    [InlineData(12, 0, false)]
    [InlineData(13, 0, true)]
    [InlineData(14, 59, true)]
    [InlineData(15, 0, false)]
    public void AWindowInsideOneDayCoversOnlyThatStretch(int hour, int minute, bool covered)
    {
        var window = QuietHours.TryRead(new TimeOnly(13, 0), new TimeOnly(15, 0)).Value.ShouldNotBeNull();

        window.Covers(new TimeOnly(hour, minute)).ShouldBe(covered);
    }

    [Fact]
    public void AWindowIsKeptAsTheLocalTimesItWasGivenIn()
    {
        // Wall-clock, not an instant: "not before eight" means eight where the customer is, on
        // whichever day the message is ready (BR-7). Converting to UTC here would move the window
        // whenever the branch's offset did.
        var preferences = CommunicationPreferences.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [CommunicationChannel.Sms],
            null,
            QuietHours.TryRead(new TimeOnly(21, 30), new TimeOnly(8, 0)).Value,
            Now,
            by: null).Value;

        preferences.QuietHours.ShouldNotBeNull();
        preferences.QuietHours.Start.ShouldBe(new TimeOnly(21, 30));
        preferences.QuietHours.End.ShouldBe(new TimeOnly(8, 0));
    }

    private static CommunicationPreferences Preferences(CommunicationChannel[] channels)
        => CommunicationPreferences.Record(
            Guid.NewGuid(), Guid.NewGuid(), channels, null, null, Now, by: null).Value;
}
