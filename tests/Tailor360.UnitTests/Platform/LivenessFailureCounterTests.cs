using Shouldly;
using Tailor360.Platform.Observability.Health;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The liveness watchdog's counting rule: three consecutive failures, with no success between them,
/// reach the threshold — and one success anywhere in the run resets it, so a single slow check does not
/// accumulate toward a restart alongside two unrelated ones on a different day.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LivenessFailureCounterTests
{
    [Fact]
    public void TheFirstAndSecondFailureDoNotReachTheThreshold()
    {
        var counter = new LivenessFailureCounter(consecutiveFailuresBeforeExit: 3);

        counter.RecordFailure().ShouldBeFalse();
        counter.RecordFailure().ShouldBeFalse();
        counter.ConsecutiveFailures.ShouldBe(2);
    }

    [Fact]
    public void TheThirdConsecutiveFailureReachesTheThreshold()
    {
        var counter = new LivenessFailureCounter(consecutiveFailuresBeforeExit: 3);

        counter.RecordFailure();
        counter.RecordFailure();

        counter.RecordFailure().ShouldBeTrue();
    }

    /// <summary>Not after two, and not by summing failures that were never consecutive.</summary>
    [Fact]
    public void ASuccessBetweenTwoFailuresResetsTheRun()
    {
        var counter = new LivenessFailureCounter(consecutiveFailuresBeforeExit: 3);

        counter.RecordFailure();
        counter.RecordFailure();
        counter.RecordSuccess();
        counter.RecordFailure();

        counter.ConsecutiveFailures.ShouldBe(1);
    }

    [Fact]
    public void ANewCounterHasNoFailures()
        => new LivenessFailureCounter(consecutiveFailuresBeforeExit: 3).ConsecutiveFailures.ShouldBe(0);
}
