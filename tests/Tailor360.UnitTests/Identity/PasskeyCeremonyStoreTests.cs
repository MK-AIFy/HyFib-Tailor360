using Shouldly;
using Tailor360.Modules.Identity.Infrastructure.Passkeys;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// What binds a WebAuthn ceremony to the browser that started it. The challenge itself is the library's
/// business; whether the right browser is allowed to answer it is this store's.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PasskeyCeremonyStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);
    private static readonly Guid Session = Guid.Parse("0199c000-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherSession = Guid.Parse("0199c000-0000-7000-8000-0000000000f2");

    [Fact]
    public void AStartedCeremonyIsReturnedToTheSessionThatStartedIt()
    {
        var store = new PasskeyCeremonyStore(new MovableClock(Start));

        var (ceremonyId, expiresAt) = store.Start("{\"challenge\":\"synthetic\"}", Session, Lifetime);

        expiresAt.ShouldBe(Start + Lifetime);
        store.Consume(ceremonyId, Session).ShouldBe("{\"challenge\":\"synthetic\"}");
    }

    [Fact]
    public void ACeremonyIssuedToOneSessionCannotBeCompletedByAnother()
    {
        var store = new PasskeyCeremonyStore(new MovableClock(Start));

        var (ceremonyId, _) = store.Start("{}", Session, Lifetime);

        // This is the substitution attack a registration ceremony is otherwise open to: an attacker
        // starts one under their own session, has the victim's authenticator sign it, and ends up
        // enrolled on the victim's account.
        store.Consume(ceremonyId, OtherSession).ShouldBeNull();
    }

    [Fact]
    public void ACeremonyStartedAnonymouslyCannotBeCompletedUnderASession()
    {
        var store = new PasskeyCeremonyStore(new MovableClock(Start));

        var (ceremonyId, _) = store.Start("{}", boundSessionId: null, Lifetime);

        store.Consume(ceremonyId, Session).ShouldBeNull();
    }

    [Fact]
    public void ACeremonyIsSpentOnItsFirstAnswerRightOrWrong()
    {
        var store = new PasskeyCeremonyStore(new MovableClock(Start));

        var (ceremonyId, _) = store.Start("{}", Session, Lifetime);

        store.Consume(ceremonyId, Session).ShouldNotBeNull();

        // A captured response must not be replayable against the same challenge.
        store.Consume(ceremonyId, Session).ShouldBeNull();
    }

    [Fact]
    public void AWrongSessionStillSpendsTheCeremony()
    {
        var store = new PasskeyCeremonyStore(new MovableClock(Start));

        var (ceremonyId, _) = store.Start("{}", Session, Lifetime);

        store.Consume(ceremonyId, OtherSession).ShouldBeNull();

        // Otherwise a caller could probe with the wrong session until they found the right one, with
        // the challenge still live at the end of it.
        store.Consume(ceremonyId, Session).ShouldBeNull();
    }

    [Fact]
    public void ACeremonyExpires()
    {
        var clock = new MovableClock(Start);
        var store = new PasskeyCeremonyStore(clock);

        var (ceremonyId, _) = store.Start("{}", Session, Lifetime);

        clock.Advance(Lifetime);

        store.Consume(ceremonyId, Session).ShouldBeNull();
    }

    [Fact]
    public void AnUnknownHandleIsRefusedAndSaysNothingAboutWhy()
    {
        var store = new PasskeyCeremonyStore(new MovableClock(Start));

        store.Consume("not-a-handle", Session).ShouldBeNull();
        store.Consume(null, Session).ShouldBeNull();
        store.Consume("   ", Session).ShouldBeNull();
    }

    [Fact]
    public void EveryHandleIsDistinct()
    {
        var store = new PasskeyCeremonyStore(new MovableClock(Start));

        var handles = Enumerable.Range(0, 50)
            .Select(_ => store.Start("{}", Session, Lifetime).CeremonyId)
            .ToArray();

        handles.Distinct(StringComparer.Ordinal).Count().ShouldBe(handles.Length);

        // 256 bits, base64url. A handle short enough to guess would make the session binding the only
        // thing standing between an attacker and a challenge, and an anonymous assertion has no session.
        handles.ShouldAllBe(handle => handle.Length >= 40);
    }
}
