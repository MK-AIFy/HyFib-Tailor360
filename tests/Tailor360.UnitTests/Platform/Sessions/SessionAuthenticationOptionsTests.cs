using Shouldly;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.UnitTests.Platform.Sessions;

/// <summary>
/// The session lifetime configuration. It is validated on start, so the point of these tests is that
/// the validation actually catches a combination that would silently lengthen a session.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SessionAuthenticationOptionsTests
{
    [Fact]
    public void TheDefaultsAreTheOnesTheIssueSets()
    {
        var options = new SessionAuthenticationOptions();

        options.IdleTimeout.ShouldBe(TimeSpan.FromMinutes(30));
        options.AbsoluteLifetime.ShouldBe(TimeSpan.FromHours(12));
        options.ExpiryWarningLead.ShouldBe(TimeSpan.FromMinutes(2));
        options.IsUsable.ShouldBeTrue();
    }

    [Fact]
    public void AnInactivityTimeoutLongerThanTheAbsoluteLifetimeIsRejected()
    {
        // The absolute lifetime is what bounds the value of a stolen ticket. An inactivity timeout that
        // outlived it would never fire, and the cap would quietly mean nothing.
        var options = new SessionAuthenticationOptions
        {
            IdleTimeout = TimeSpan.FromHours(6),
            AbsoluteLifetime = TimeSpan.FromHours(2),
        };

        options.IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void AWarningThatArrivesAfterTheDeadlineIsRejected()
    {
        var options = new SessionAuthenticationOptions
        {
            IdleTimeout = TimeSpan.FromMinutes(5),
            ExpiryWarningLead = TimeSpan.FromMinutes(10),
        };

        options.IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void AWriteBackIntervalLongerThanTheTimeoutItMaintainsIsRejected()
    {
        // Writing the deadline back less often than the deadline itself expires would end a session
        // someone was actively using.
        var options = new SessionAuthenticationOptions
        {
            IdleTimeout = TimeSpan.FromMinutes(5),
            SlidingWriteInterval = TimeSpan.FromMinutes(5),
        };

        options.IsUsable.ShouldBeFalse();
    }
}
