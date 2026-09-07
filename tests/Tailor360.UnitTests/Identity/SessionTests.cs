using Shouldly;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Sessions;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The session ticket's own rules. Everything the browser holds is one random value, so every property
/// that bounds a session's life or ends it has to hold in this type — there is nothing in the cookie to
/// check it against.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SessionTests
{
    private static Session Start(DateTimeOffset? at = null)
        => Session.Start(
            IdentityTestData.Id("session"),
            IdentityTestData.Id("user"),
            IdentityTestData.Organisation,
            null,
            IdentityTestData.Digest("ticket"),
            "Chrome on Windows",
            at ?? IdentityTestData.Now,
            SessionLifetime.Default).Value;

    [Fact]
    public void ARawTicketValueCannotBeStored()
        => Session.Start(
            IdentityTestData.Id("session"),
            IdentityTestData.Id("user"),
            IdentityTestData.Organisation,
            null,
            "0P9SDaeuHzp3Z8Z0h3yqcw",
            "Chrome on Windows",
            IdentityTestData.Now,
            SessionLifetime.Default).Error.Code.ShouldBe("identity.token-not-hashed");

    [Fact]
    public void AnInactivityTimeoutLongerThanTheAbsoluteLifetimeIsRefused()
        => SessionLifetime.Create(TimeSpan.FromHours(13), TimeSpan.FromHours(12))
            .IsFailure.ShouldBeTrue();

    [Fact]
    public void ASessionEndsWhenItHasSatUnusedForTheInactivityTimeout()
    {
        var session = Start();

        session.IsActive(IdentityTestData.Now.AddMinutes(29)).ShouldBeTrue();
        session.IsActive(IdentityTestData.Now.AddMinutes(31)).ShouldBeFalse();
    }

    [Fact]
    public void UsingASessionSlidesTheInactivityDeadlineForward()
    {
        var session = Start();
        var later = IdentityTestData.Now.AddMinutes(20);

        session.Touch(later, TimeSpan.FromMinutes(30)).IsSuccess.ShouldBeTrue();

        session.IsActive(later.AddMinutes(29)).ShouldBeTrue();
        session.LastSeenAt.ShouldBe(later);
    }

    [Fact]
    public void SlidingNeverPushesASessionPastItsAbsoluteExpiry()
    {
        // Without this, a tab left open on a counter tablet — polling every few minutes, as the shell
        // does — would hold a session open for ever.
        var session = Start();
        var at = IdentityTestData.Now;

        while (session.IsActive(at.AddMinutes(20)))
        {
            at = at.AddMinutes(20);
            session.Touch(at, TimeSpan.FromMinutes(30)).IsSuccess.ShouldBeTrue();
        }

        session.IdleExpiresAt.ShouldBe(session.AbsoluteExpiresAt);
        session.IsActive(IdentityTestData.Now.AddHours(12)).ShouldBeFalse();
    }

    [Fact]
    public void AnExpiredSessionCannotBeRevivedByUsingIt()
        => Start().Touch(IdentityTestData.Now.AddHours(13), TimeSpan.FromMinutes(30)).Error
            .ShouldBe(IdentityErrors.SessionNotActive);

    [Fact]
    public void RevocationIsFinal()
    {
        // "Sign out everywhere" is worth nothing if a request already in flight can bring a session
        // back.
        var session = Start();
        session.Revoke(IdentityTestData.Now, SessionEndReason.SignedOutEverywhere);

        session.IsActive(IdentityTestData.Now).ShouldBeFalse();
        session.Touch(IdentityTestData.Now, TimeSpan.FromMinutes(30)).IsFailure.ShouldBeTrue();
        session.RecordStrongAuthentication(IdentityTestData.Now).IsFailure.ShouldBeTrue();
        session.RotateTo(
            IdentityTestData.Id("next"),
            IdentityTestData.Digest("ticket-2"),
            IdentityTestData.Now,
            TimeSpan.FromMinutes(30)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void RevokingTwiceKeepsTheFirstReason()
    {
        var session = Start();
        session.Revoke(IdentityTestData.Now, SessionEndReason.PasswordChanged);
        session.Revoke(IdentityTestData.Now.AddMinutes(1), SessionEndReason.SignedOutEverywhere);

        session.EndReason.ShouldBe(SessionEndReason.PasswordChanged);
        session.RevokedAt.ShouldBe(IdentityTestData.Now);
    }

    [Fact]
    public void RotationEndsTheOldTicketAndIssuesANewOne()
    {
        // This is the defence against session fixation: the value a caller held before signing in is
        // not the value they hold afterwards.
        var session = Start();

        var replacement = session.RotateTo(
            IdentityTestData.Id("next"),
            IdentityTestData.Digest("ticket-2"),
            IdentityTestData.Now.AddMinutes(1),
            TimeSpan.FromMinutes(30)).Value;

        replacement.Id.ShouldNotBe(session.Id);
        replacement.TokenHash.ShouldNotBe(session.TokenHash);
        session.EndReason.ShouldBe(SessionEndReason.Rotated);
        session.SupersededBySessionId.ShouldBe(replacement.Id);
        session.IsActive(IdentityTestData.Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void RotationCarriesTheOriginalAbsoluteExpiryIntoTheReplacement()
    {
        // Rotating at every challenge and step-up would otherwise hand out a fresh twelve hours each
        // time, and the absolute cap would quietly mean nothing.
        var session = Start();
        var current = session;
        var at = IdentityTestData.Now;
        var rotations = 0;

        while (current.IsActive(at.AddMinutes(25)))
        {
            at = at.AddMinutes(25);
            rotations++;

            current = current.RotateTo(
                IdentityTestData.Id($"session-{rotations}"),
                IdentityTestData.Digest($"ticket-{rotations}"),
                at,
                TimeSpan.FromMinutes(30)).Value;

            current.AbsoluteExpiresAt.ShouldBe(session.AbsoluteExpiresAt);
        }

        // A whole shift of rotations, and the session still ends twelve hours after it began.
        rotations.ShouldBeGreaterThan(20);
        current.IsActive(IdentityTestData.Now.AddHours(12)).ShouldBeFalse();
    }

    [Fact]
    public void RotationKeepsTheStrongAuthenticationTheHolderAlreadyProved()
    {
        var session = Start();
        session.RecordStrongAuthentication(IdentityTestData.Now);

        var replacement = session.RotateTo(
            IdentityTestData.Id("next"),
            IdentityTestData.Digest("ticket-2"),
            IdentityTestData.Now.AddMinutes(1),
            TimeSpan.FromMinutes(30)).Value;

        replacement.MfaSatisfied.ShouldBeTrue();
        replacement.LastStrongAuthAt.ShouldBe(IdentityTestData.Now);
    }

    [Fact]
    public void StepUpFreshnessExpiresWithTheWindow()
    {
        var session = Start();
        session.RecordStrongAuthentication(IdentityTestData.Now);

        session.IsStepUpFresh(IdentityTestData.Now.AddMinutes(4), TimeSpan.FromMinutes(5))
            .ShouldBeTrue();
        session.IsStepUpFresh(IdentityTestData.Now.AddMinutes(6), TimeSpan.FromMinutes(5))
            .ShouldBeFalse();
    }

    [Fact]
    public void ASessionThatNeverProvedAStrongFactorIsNeverFresh()
        => Start().IsStepUpFresh(IdentityTestData.Now, TimeSpan.FromMinutes(5)).ShouldBeFalse();

    [Fact]
    public void AnOverLongDeviceLabelIsTruncatedRatherThanRefused()
    {
        // The label is derived from a user agent, which is attacker-influenced but not security
        // relevant. Refusing a sign-in over it would be the wrong trade.
        var session = Session.Start(
            IdentityTestData.Id("session"),
            IdentityTestData.Id("user"),
            IdentityTestData.Organisation,
            null,
            IdentityTestData.Digest("ticket"),
            new string('x', 500),
            IdentityTestData.Now,
            SessionLifetime.Default).Value;

        session.DeviceLabel.Length.ShouldBe(Session.MaximumDeviceLabelLength);
    }
}
