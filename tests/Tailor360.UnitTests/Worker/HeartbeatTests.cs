using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Background;
using Tailor360.Worker;
using Tailor360.Worker.Jobs;

namespace Tailor360.UnitTests.Worker;

/// <summary>
/// The worker's liveness signal, and the check that turns it into a restart.
/// </summary>
/// <remarks>
/// <para>
/// A worker process that is running but no longer beating is worse than one that has exited, because
/// nothing restarts it: the orchestrator sees a live process, the queue stops draining, and the first
/// person to notice is whoever was waiting for the work. The three states below are what separate those
/// cases, and until now nothing asserted them.
/// </para>
/// <para>
/// These are unit tests: no database, no host. The heartbeat's persistence is exercised by the
/// integration tier; what is asserted here is the decision the health check makes from the beat, and
/// the promise the service makes about surviving a database it cannot reach.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class HeartbeatTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 3, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Before the first beat the answer is degraded, not unhealthy. A worker that has just started has
    /// not failed; reporting it unhealthy would restart it during its own start-up, forever.
    /// </summary>
    [Fact]
    public async Task AWorkerThatHasNotBeatenYetIsDegradedRatherThanUnhealthy()
    {
        var check = new HeartbeatHealthCheck(
            new StubMonitor(null, TimeSpan.FromSeconds(45)), new FixedClock(Now));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AFreshHeartbeatIsHealthy()
    {
        var check = new HeartbeatHealthCheck(
            new StubMonitor(Now.AddSeconds(-10), TimeSpan.FromSeconds(45)), new FixedClock(Now));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    /// <summary>
    /// The boundary belongs to the healthy side: a beat exactly at the staleness limit has not yet
    /// exceeded it, and rounding the other way would restart a worker on the tick.
    /// </summary>
    [Fact]
    public async Task AHeartbeatExactlyAtTheLimitIsStillHealthy()
    {
        var staleAfter = TimeSpan.FromSeconds(45);
        var check = new HeartbeatHealthCheck(
            new StubMonitor(Now - staleAfter, staleAfter), new FixedClock(Now));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AStaleHeartbeatIsUnhealthy()
    {
        var staleAfter = TimeSpan.FromSeconds(45);
        var check = new HeartbeatHealthCheck(
            new StubMonitor(Now - staleAfter - TimeSpan.FromTicks(1), staleAfter), new FixedClock(Now));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    /// <summary>
    /// Three intervals, so one slow write does not raise a false alarm and a wedged instance is still
    /// caught within a minute of the default interval.
    /// </summary>
    [Fact]
    public void StalenessIsThreeHeartbeatIntervals()
    {
        var service = Service(new ThrowingScopeFactory(), TimeSpan.FromSeconds(20));

        service.StaleAfter.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AServiceThatHasNotRunReportsNoBeat()
        => Service(new ThrowingScopeFactory()).LastBeat.ShouldBeNull();

    /// <summary>
    /// The promise the service makes in its own comment: "the database being briefly unavailable must
    /// not stop the worker". A scope factory that throws stands in for a database that cannot be
    /// reached — the loop keeps running, and the in-memory beat still proves it.
    /// </summary>
    [Fact]
    public async Task ADatabaseItCannotReachDoesNotStopTheWorkerOrTheBeat()
    {
        var factory = new ThrowingScopeFactory();
        var service = Service(factory);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            // Polled rather than read once: StartAsync returns as soon as ExecuteAsync yields, so
            // reading immediately would assert on whether the first beat happened to finish before
            // the call returned — which is a scheduling detail, not the behaviour under test.
            await WaitFor(
                () => service.LastBeat is not null,
                "the first heartbeat to be recorded");

            service.LastBeat.ShouldBe(Now, "the in-memory beat is recorded before the write is attempted");
            factory.Attempts.ShouldBeGreaterThan(0, "the service did try to persist the beat");
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        // The write threw every time, and the loop is still the thing that stopped it.
        service.LastBeat.ShouldNotBeNull();
    }

    /// <summary>Waits for a condition, failing with a readable reason rather than a timeout.</summary>
    private static async Task WaitFor(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new Shouldly.ShouldAssertException($"Timed out waiting for {what}.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static HeartbeatService Service(IWorkerScopeFactory factory, TimeSpan? interval = null)
        => new(
            factory,
            new FixedClock(Now),
            Options.Create(new WorkerOptions
            {
                InstanceName = "unit-test-instance",
                HeartbeatInterval = interval ?? TimeSpan.FromSeconds(15),
            }),
            NullLogger<HeartbeatService>.Instance);

    private sealed class StubMonitor(DateTimeOffset? lastBeat, TimeSpan staleAfter) : IHeartbeatMonitor
    {
        public DateTimeOffset? LastBeat { get; } = lastBeat;

        public TimeSpan StaleAfter { get; } = staleAfter;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;

        public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);
    }

    /// <summary>A scope factory standing in for a database that cannot be reached.</summary>
    private sealed class ThrowingScopeFactory : IWorkerScopeFactory
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public IWorkerScope CreateSystemScope(
            Type jobType, Guid? branchId = null, string? correlationId = null)
        {
            Interlocked.Increment(ref _attempts);
            throw new InvalidOperationException("The database is unreachable.");
        }

        public Task<IWorkerScope> CreateScopeForAsync(
            Type jobType, WorkerJobRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The heartbeat runs on its own authority.");
    }
}
