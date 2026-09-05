using Shouldly;
using Tailor360.Modules.Identity.Application.Abuse;
using Tailor360.Modules.Identity.Application.Options;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The throttle that runs in front of the password hash. It bounds this address against this account,
/// which is the pair neither the endpoint's rate-limit policy nor the account's lockout column bounds.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CredentialThrottleTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnAttemptIsAllowedUntilTheAccountsLimitIsReached()
    {
        var (throttle, _) = Build(perAccount: 3, perClient: 100);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            throttle.Check(CredentialAction.SignIn, "meena", "203.0.113.1").IsAllowed.ShouldBeTrue();
            throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");
        }

        var refused = throttle.Check(CredentialAction.SignIn, "meena", "203.0.113.1");

        refused.IsAllowed.ShouldBeFalse();
        refused.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void TheAccountsCounterIsSharedAcrossAddresses()
    {
        var (throttle, _) = Build(perAccount: 3, perClient: 100);

        // An attacker who spreads across addresses defeats a per-address limit and not this one. That
        // is the whole reason the account dimension exists alongside the address dimension.
        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");
        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.2");
        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.3");

        throttle.Check(CredentialAction.SignIn, "meena", "203.0.113.4").IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public void TheAddressCounterIsSharedAcrossAccounts()
    {
        var (throttle, _) = Build(perAccount: 100, perClient: 3);

        // One password sprayed across many accounts is the attack no per-account counter would notice.
        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");
        throttle.RecordFailure(CredentialAction.SignIn, "arun", "203.0.113.1");
        throttle.RecordFailure(CredentialAction.SignIn, "priya", "203.0.113.1");

        throttle.Check(CredentialAction.SignIn, "kavya", "203.0.113.1").IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public void TheAccountKeyIsCaseAndWhitespaceInsensitive()
    {
        var (throttle, _) = Build(perAccount: 2, perClient: 100);

        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");
        throttle.RecordFailure(CredentialAction.SignIn, "  MEENA  ", "203.0.113.1");

        // Otherwise the counter is defeated by pressing the shift key.
        throttle.Check(CredentialAction.SignIn, "Meena", "203.0.113.1").IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public void EachEndpointIsCountedSeparately()
    {
        var (throttle, _) = Build(perAccount: 2, perClient: 100);

        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");
        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");

        throttle.Check(CredentialAction.SignIn, "meena", "203.0.113.1").IsAllowed.ShouldBeFalse();

        // Being locked out of sign-in must not lock the same person out of answering a challenge on a
        // session they already hold, and the reverse.
        throttle.Check(CredentialAction.MultiFactorChallenge, "meena", "203.0.113.1").IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public void TheWindowLapses()
    {
        var (throttle, clock) = Build(perAccount: 2, perClient: 100);

        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");
        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");
        throttle.Check(CredentialAction.SignIn, "meena", "203.0.113.1").IsAllowed.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(5));

        throttle.Check(CredentialAction.SignIn, "meena", "203.0.113.1").IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public void SigningInSuccessfullyClearsTheAccountCounterAndLeavesTheAddressCounterAlone()
    {
        var (throttle, _) = Build(perAccount: 3, perClient: 4);

        throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");
        throttle.RecordFailure(CredentialAction.SignIn, "arun", "203.0.113.1");
        throttle.RecordFailure(CredentialAction.SignIn, "priya", "203.0.113.1");

        throttle.RecordSuccess(CredentialAction.SignIn, "meena", "203.0.113.1");

        // The account is clear again, so one wrong password costs its owner nothing later.
        throttle.Check(CredentialAction.SignIn, "meena", "203.0.113.1").IsAllowed.ShouldBeTrue();

        // The address is not. Guessing one password out of a thousand must not buy an attacker a clean
        // slate for the next thousand.
        throttle.RecordFailure(CredentialAction.SignIn, "kavya", "203.0.113.1");
        throttle.Check(CredentialAction.SignIn, "kavya", "203.0.113.1").IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public void ARequestWithNoAddressIsStillCountedAgainstItsAccount()
    {
        var (throttle, _) = Build(perAccount: 2, perClient: 2);

        // A deployment whose proxy configuration cannot resolve an address must not lose the account
        // dimension as well.
        throttle.RecordFailure(CredentialAction.SignIn, "meena", clientKey: null);
        throttle.RecordFailure(CredentialAction.SignIn, "meena", clientKey: null);

        throttle.Check(CredentialAction.SignIn, "meena", clientKey: null).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public void AHumanCheckIsAskedForOnceAnAddressLooksAutomatedAndNotBefore()
    {
        var (throttle, _) = Build(perAccount: 100, perClient: 100, captchaAfter: 3);

        throttle.Check(CredentialAction.SignIn, "meena", "203.0.113.1").IsCaptchaRequired.ShouldBeFalse();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            throttle.RecordFailure(CredentialAction.SignIn, "meena", "203.0.113.1");
        }

        var decision = throttle.Check(CredentialAction.SignIn, "meena", "203.0.113.1");

        decision.IsCaptchaRequired.ShouldBeTrue();

        // It is a signal, not a refusal: the attempt is still evaluated when no verifier is configured.
        decision.IsAllowed.ShouldBeTrue();
    }

    private static (CredentialThrottle Throttle, MovableClock Clock) Build(
        int perAccount,
        int perClient,
        int captchaAfter = 0)
    {
        var options = new CredentialThrottleOptions();

        foreach (var rule in new[] { options.SignIn, options.MultiFactorChallenge, options.Recovery })
        {
            rule.PerAccount = perAccount;
            rule.PerClient = perClient;
            rule.Window = TimeSpan.FromMinutes(5);
            rule.CaptchaAfter = captchaAfter;
        }

        var clock = new MovableClock(Start);
        return (new CredentialThrottle(clock, TestOptions.For(options)), clock);
    }
}
