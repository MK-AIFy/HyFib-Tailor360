using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Modules.Integration.Infrastructure.Storage;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.UnitTests.Integration;

/// <summary>
/// <see cref="ResilientObjectStorage"/>'s own behaviour around a fake <c>IObjectStorage</c> — no network,
/// no MinIO, no database. What is asserted here is the shedding rule ("refuses rather than queues"), the
/// exception it surfaces to a caller, and that the breaker only ever sees the outcome of a call that
/// actually happened.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ResilientObjectStorageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ACallPastTheBulkheadLimitIsRejectedRatherThanQueued()
    {
        var inner = new BlockingObjectStorage();
        var storage = Build(inner, maxConcurrentCalls: 1);

        var holding = storage.ExistsAsync("media/one", TestContext.Current.CancellationToken);
        await inner.WaitUntilCallStartedAsync();

        // A second caller arrives while the first is still in flight and the bulkhead has no free slot.
        await Should.ThrowAsync<ObjectStorageUnavailableException>(
            () => storage.ExistsAsync("media/two", TestContext.Current.CancellationToken));

        inner.Release();
        await holding;
    }

    [Fact]
    public async Task ARejectedCallIsNeverReportedAsASuccessfulWrite()
    {
        var inner = new BlockingObjectStorage();
        var storage = Build(inner, maxConcurrentCalls: 1);

        var holding = storage.PutAsync("media/one", new MemoryStream([1]), "image/png", TestContext.Current.CancellationToken);
        await inner.WaitUntilCallStartedAsync();

        var rejected = await Should.ThrowAsync<ObjectStorageUnavailableException>(
            () => storage.PutAsync("media/two", new MemoryStream([2]), "image/png", TestContext.Current.CancellationToken));

        rejected.ShouldNotBeNull();
        inner.Puts.ShouldBe(1, "the rejected call never reached the underlying adapter at all");

        inner.Release();
        await holding;
    }

    [Fact]
    public async Task AFailingCallSurfacesTheDistinguishableExceptionNotTheAdaptersOwn()
    {
        var inner = new ThrowingObjectStorage();
        var storage = Build(inner, maxConcurrentCalls: 2);

        await Should.ThrowAsync<ObjectStorageUnavailableException>(
            () => storage.ExistsAsync("media/one", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASuccessfulCallReleasesTheBulkheadSlotForTheNextCaller()
    {
        var inner = new InMemoryObjectStorage();
        var storage = Build(inner, maxConcurrentCalls: 1);

        await storage.ExistsAsync("media/one", TestContext.Current.CancellationToken);
        // The slot was released after the first call completed; a second call must not be rejected.
        await Should.NotThrowAsync(() => storage.ExistsAsync("media/two", TestContext.Current.CancellationToken));
    }

    private static ResilientObjectStorage Build(IObjectStorage inner, int maxConcurrentCalls)
        => new(
            inner,
            Options.Create(new ObjectStorageOptions
            {
                Bulkhead = new ObjectStorageBulkheadOptions { MaxConcurrentCalls = maxConcurrentCalls },
                Breaker = new ObjectStorageBreakerOptions { FailureThreshold = 100, BreakDuration = TimeSpan.FromMinutes(1) },
                CallTimeout = TimeSpan.FromSeconds(5),
            }),
            new FixedClock(Now),
            NullLogger<ResilientObjectStorage>.Instance);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;

        public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);
    }

    /// <summary>Blocks its one call until <see cref="Release"/> is signalled, so a test can hold a bulkhead slot open.</summary>
    private sealed class BlockingObjectStorage : IObjectStorage
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _puts;

        public int Puts => Volatile.Read(ref _puts);

        public void Release() => _release.TrySetResult();

        public Task WaitUntilCallStartedAsync() => _started.Task;

        public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _puts);
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return null;
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return true;
        }
    }

    private sealed class ThrowingObjectStorage : IObjectStorage
    {
        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The dependency is unreachable.");

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The dependency is unreachable.");

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The dependency is unreachable.");
    }
}
