using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Worker.Jobs;

namespace Tailor360.UnitTests.Worker;

/// <summary>
/// The outbox dispatcher's own liveness check, over a stub monitor — no database, no host, and no real
/// <see cref="Tailor360.Platform.Persistence.Outbox.OutboxDispatcher"/>. This is deliberately Unhealthy
/// rather than Degraded on staleness, unlike the Ready-tagged heartbeat check: a Live-tagged check answers
/// the watchdog, which treats anything short of Healthy as a failure, so a Degraded reading here would be
/// silently indistinguishable from Healthy to the one caller that reads it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OutboxDispatcherLivenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ADispatcherThatHasNeverIteratedIsUnhealthy()
    {
        var check = new OutboxDispatcherLivenessHealthCheck(
            new StubMonitor(null, TimeSpan.FromSeconds(30)), new FixedClock(Now));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task ARecentIterationIsHealthy()
    {
        var check = new OutboxDispatcherLivenessHealthCheck(
            new StubMonitor(Now.AddSeconds(-5), TimeSpan.FromSeconds(30)), new FixedClock(Now));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AnIterationExactlyAtTheStalenessLimitIsStillHealthy()
    {
        var staleAfter = TimeSpan.FromSeconds(30);
        var check = new OutboxDispatcherLivenessHealthCheck(
            new StubMonitor(Now - staleAfter, staleAfter), new FixedClock(Now));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AStaleIterationIsUnhealthy()
    {
        var staleAfter = TimeSpan.FromSeconds(30);
        var check = new OutboxDispatcherLivenessHealthCheck(
            new StubMonitor(Now - staleAfter - TimeSpan.FromTicks(1), staleAfter), new FixedClock(Now));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    private sealed class StubMonitor(DateTimeOffset? lastIterationStartedAt, TimeSpan staleAfter) : IOutboxDispatcherActivityMonitor
    {
        public DateTimeOffset? LastIterationStartedAt { get; } = lastIterationStartedAt;

        public TimeSpan StaleAfter { get; } = staleAfter;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;

        public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);
    }
}
