using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Modules.Integration.Infrastructure.Storage;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.UnitTests.Integration;

/// <summary>
/// <see cref="ObjectStorageHealthCheck"/> reads the breaker's state rather than probing storage itself.
/// All three states are exercised, not just Open and Closed: a half-open breaker has not yet confirmed
/// the dependency recovered, and reporting Healthy on that guess would be the exact premature all-clear
/// this endpoint exists to avoid.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ObjectStorageHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AClosedBreakerIsHealthy()
    {
        var check = new ObjectStorageHealthCheck(Storage(new FixedClock(Now), failureThreshold: 100));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AnOpenBreakerIsDegraded()
    {
        var storage = Storage(new FixedClock(Now), failureThreshold: 1);
        await Should.ThrowAsync<ObjectStorageUnavailableException>(() => storage.ExistsAsync("probe", TestContext.Current.CancellationToken));
        var check = new ObjectStorageHealthCheck(storage);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Degraded);
    }

    /// <summary>
    /// The regression this test exists for: a breaker that has silently advanced past its break duration
    /// (no trial call has run yet) must not read as Healthy just because it is no longer Open.
    /// </summary>
    [Fact]
    public async Task AHalfOpenBreakerIsDegradedNotHealthy()
    {
        var clock = new MutableClock(Now);
        var storage = Storage(clock, failureThreshold: 1);
        await Should.ThrowAsync<ObjectStorageUnavailableException>(() => storage.ExistsAsync("probe", TestContext.Current.CancellationToken));
        clock.Advance(TimeSpan.FromSeconds(1));
        storage.Breaker.State.ShouldBe(ObjectStorageCircuitState.HalfOpen, "the break duration below is 500ms; a second later it must have elapsed");

        var check = new ObjectStorageHealthCheck(storage);
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task TheDescriptionNamesNoEndpointNoBucketAndNoException()
    {
        var storage = Storage(new FixedClock(Now), failureThreshold: 1);
        await Should.ThrowAsync<ObjectStorageUnavailableException>(() => storage.ExistsAsync("probe", TestContext.Current.CancellationToken));
        var check = new ObjectStorageHealthCheck(storage);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Description.ShouldNotBeNullOrWhiteSpace();
        result.Description.ShouldNotContain("minio", Case.Insensitive);
        result.Description.ShouldNotContain("http");
    }

    private static ResilientObjectStorage Storage(IClock clock, int failureThreshold)
        => new(
            new ThrowingObjectStorage(),
            Options.Create(new ObjectStorageOptions
            {
                Breaker = new ObjectStorageBreakerOptions { FailureThreshold = failureThreshold, BreakDuration = TimeSpan.FromMilliseconds(500) },
                CallTimeout = TimeSpan.FromSeconds(5),
            }),
            clock,
            NullLogger<ResilientObjectStorage>.Instance);

    private sealed class ThrowingObjectStorage : IObjectStorage
    {
        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Host=minio.internal;Port=9000 unreachable.");

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Host=minio.internal;Port=9000 unreachable.");

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Host=minio.internal;Port=9000 unreachable.");
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
