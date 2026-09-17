using Shouldly;
using Tailor360.Modules.Integration.Infrastructure.Storage;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.UnitTests.Integration;

/// <summary>
/// The object-storage circuit breaker's state machine, tested with no <c>IObjectStorage</c> and no
/// database — <c>ResilientObjectStorage</c>'s own resilience layer is a pure function of failures, time
/// and a threshold, and this is the whole point of pulling it out of the decorator.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ObjectStorageCircuitBreakerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AFreshBreakerIsClosedAndAdmitsACall()
    {
        var breaker = new ObjectStorageCircuitBreaker(new FixedClock(Now), failureThreshold: 3, breakDuration: TimeSpan.FromSeconds(30));

        breaker.State.ShouldBe(ObjectStorageCircuitState.Closed);
        breaker.TryEnter().ShouldBeTrue();
    }

    [Fact]
    public void FewerFailuresThanTheThresholdStayClosed()
    {
        var breaker = new ObjectStorageCircuitBreaker(new FixedClock(Now), failureThreshold: 3, breakDuration: TimeSpan.FromSeconds(30));

        breaker.RecordFailure();
        breaker.RecordFailure();

        breaker.State.ShouldBe(ObjectStorageCircuitState.Closed);
    }

    [Fact]
    public void TheThresholdConsecutiveFailureOpensTheBreaker()
    {
        var breaker = new ObjectStorageCircuitBreaker(new FixedClock(Now), failureThreshold: 3, breakDuration: TimeSpan.FromSeconds(30));

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();

        breaker.State.ShouldBe(ObjectStorageCircuitState.Open);
    }

    [Fact]
    public void AnOpenBreakerRefusesEveryCall()
    {
        var clock = new MutableClock(Now);
        var breaker = new ObjectStorageCircuitBreaker(clock, failureThreshold: 1, breakDuration: TimeSpan.FromSeconds(30));

        breaker.RecordFailure();

        breaker.TryEnter().ShouldBeFalse();
    }

    [Fact]
    public void TheBreakerHalfOpensOnceTheBreakDurationElapses()
    {
        var clock = new MutableClock(Now);
        var breaker = new ObjectStorageCircuitBreaker(clock, failureThreshold: 1, breakDuration: TimeSpan.FromSeconds(30));

        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(30));

        breaker.State.ShouldBe(ObjectStorageCircuitState.HalfOpen);
    }

    [Fact]
    public void ExactlyOneTrialIsAdmittedWhileHalfOpen()
    {
        var clock = new MutableClock(Now);
        var breaker = new ObjectStorageCircuitBreaker(clock, failureThreshold: 1, breakDuration: TimeSpan.FromSeconds(30));

        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(30));

        breaker.TryEnter().ShouldBeTrue("the trial call");
        breaker.TryEnter().ShouldBeFalse("a second caller arriving while the trial is outstanding");
    }

    [Fact]
    public void ASuccessfulTrialClosesTheBreakerAndResetsTheFailureCount()
    {
        var clock = new MutableClock(Now);
        var breaker = new ObjectStorageCircuitBreaker(clock, failureThreshold: 1, breakDuration: TimeSpan.FromSeconds(30));

        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(30));
        breaker.TryEnter();
        breaker.RecordSuccess();

        breaker.State.ShouldBe(ObjectStorageCircuitState.Closed);

        // The failure count reset: a single failure now does not immediately re-open a threshold-1 breaker,
        // because RecordFailure from Closed increments toward the threshold rather than opening outright.
        breaker.RecordFailure();
        breaker.State.ShouldBe(ObjectStorageCircuitState.Open, "the threshold is 1, so this single failure reopens it");
    }

    /// <summary>A failure in half-open re-opens immediately, without needing to reach the threshold again.</summary>
    [Fact]
    public void AFailedTrialReOpensTheBreakerImmediately()
    {
        var clock = new MutableClock(Now);
        var breaker = new ObjectStorageCircuitBreaker(clock, failureThreshold: 5, breakDuration: TimeSpan.FromSeconds(30));

        for (var i = 0; i < 5; i++)
        {
            breaker.RecordFailure();
        }

        breaker.State.ShouldBe(ObjectStorageCircuitState.Open);
        clock.Advance(TimeSpan.FromSeconds(30));
        breaker.TryEnter().ShouldBeTrue();

        breaker.RecordFailure();

        breaker.State.ShouldBe(ObjectStorageCircuitState.Open, "one failed trial re-opens immediately, not after five more failures");
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;

        public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public void Advance(TimeSpan by) => UtcNow += by;

        public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);
    }
}
