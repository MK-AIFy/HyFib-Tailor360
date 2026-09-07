using Shouldly;
using Tailor360.Platform.Security.Audit;

namespace Tailor360.UnitTests.Security;

/// <summary>
/// The rule that decides whether a refused request gets its own audit entry.
/// </summary>
/// <remarks>
/// Two things have to be true at once and they pull against each other: one person's client retrying a
/// forbidden command forty times must not write forty rows, and no refusal may ever be dropped because
/// enough of them have already been written. The first is why there is a key; the second is why there is
/// no rate limit, and why running out of room to remember turns coalescing off rather than turning
/// recording off.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class AuthorisationDenialCoalescerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 11, 30, 20, TimeSpan.Zero);

    private const string Actor = "9c1f4c1c9f0e4b6ab0d0d9c9b1a2c3d4";
    private const string Endpoint = "POST /api/v1/orders/{orderId}/confirm";

    [Fact]
    public void RecordsTheFirstRefusalOfAMinute()
        => new AuthorisationDenialCoalescer().ShouldRecord(Actor, Endpoint, Now).ShouldBeTrue();

    [Fact]
    public void DoesNotRecordTheSameRefusalTwiceInOneMinute()
    {
        var coalescer = new AuthorisationDenialCoalescer();

        coalescer.ShouldRecord(Actor, Endpoint, Now).ShouldBeTrue();

        for (var repeat = 0; repeat < 40; repeat++)
        {
            coalescer.ShouldRecord(Actor, Endpoint, Now.AddSeconds(repeat % 30)).ShouldBeFalse();
        }
    }

    [Fact]
    public void RecordsTheSameRefusalAgainInTheNextMinute()
    {
        var coalescer = new AuthorisationDenialCoalescer();

        coalescer.ShouldRecord(Actor, Endpoint, Now).ShouldBeTrue();
        coalescer.ShouldRecord(Actor, Endpoint, Now.AddMinutes(1)).ShouldBeTrue();
    }

    /// <summary>
    /// The property the whole design is for. Forty people refused at one endpoint in one minute is forty
    /// entries, not one and not ten: a denial dropped because it was the eleventh that minute is the one
    /// the investigation needed.
    /// </summary>
    [Fact]
    public void RecordsEveryDistinctActorInTheSameMinute()
    {
        var coalescer = new AuthorisationDenialCoalescer();

        for (var actor = 0; actor < 40; actor++)
        {
            coalescer.ShouldRecord($"actor-{actor}", Endpoint, Now).ShouldBeTrue();
        }
    }

    [Fact]
    public void RecordsEveryDistinctEndpointForOneActorInTheSameMinute()
    {
        var coalescer = new AuthorisationDenialCoalescer();

        for (var endpoint = 0; endpoint < 40; endpoint++)
        {
            coalescer.ShouldRecord(Actor, $"POST /api/v1/thing-{endpoint}", Now).ShouldBeTrue();
        }
    }

    /// <summary>
    /// Overflow makes the trail noisier, never shorter. A flood is exactly when the trail matters, and
    /// the alternative — deciding which denials do not count — cannot be made safely at that moment.
    /// </summary>
    [Fact]
    public void RecordsEverythingOnceItHasRunOutOfRoomToRemember()
    {
        var coalescer = new AuthorisationDenialCoalescer(capacity: 4);

        for (var actor = 0; actor < 4; actor++)
        {
            coalescer.ShouldRecord($"actor-{actor}", Endpoint, Now).ShouldBeTrue();
        }

        coalescer.Remembered.ShouldBe(4);

        // Past the bound, and the answer to a repeat is now yes rather than no.
        coalescer.ShouldRecord("actor-0", Endpoint, Now).ShouldBeTrue();
        coalescer.ShouldRecord("actor-0", Endpoint, Now).ShouldBeTrue();
        coalescer.ShouldRecord("actor-99", Endpoint, Now).ShouldBeTrue();
    }

    /// <summary>
    /// What has been coalesced is forgotten as its minute passes, so a busy day does not accumulate a
    /// key per refusal for the life of the process.
    /// </summary>
    [Fact]
    public void ForgetsWhatItRememberedAboutMinutesThatHavePassed()
    {
        var coalescer = new AuthorisationDenialCoalescer();

        for (var actor = 0; actor < 500; actor++)
        {
            coalescer.ShouldRecord($"actor-{actor}", Endpoint, Now).ShouldBeTrue();
        }

        coalescer.Remembered.ShouldBe(500);

        coalescer.ShouldRecord(Actor, Endpoint, Now.AddMinutes(5)).ShouldBeTrue();
        coalescer.Remembered.ShouldBe(1);
    }

    /// <summary>
    /// A minute either side is kept, so a request whose clock reading falls just over a boundary is not
    /// recorded twice for one event.
    /// </summary>
    [Fact]
    public void KeepsTheMinuteEitherSideOfTheCurrentOne()
    {
        var coalescer = new AuthorisationDenialCoalescer();

        coalescer.ShouldRecord(Actor, Endpoint, Now).ShouldBeTrue();
        coalescer.ShouldRecord("somebody-else", Endpoint, Now.AddMinutes(1)).ShouldBeTrue();

        coalescer.Remembered.ShouldBe(2);
        coalescer.ShouldRecord(Actor, Endpoint, Now).ShouldBeFalse();
    }

    [Fact]
    public void RefusesToBeBuiltWithNoRoomAtAll()
        => Should.Throw<ArgumentOutOfRangeException>(() => new AuthorisationDenialCoalescer(capacity: 0));

    [Fact]
    public void RefusesARefusalItCannotKey()
    {
        var coalescer = new AuthorisationDenialCoalescer();

        Should.Throw<ArgumentException>(() => coalescer.ShouldRecord(" ", Endpoint, Now));
        Should.Throw<ArgumentException>(() => coalescer.ShouldRecord(Actor, " ", Now));
    }
}
