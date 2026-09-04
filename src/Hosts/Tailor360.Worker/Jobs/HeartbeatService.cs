using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Records that this worker instance is alive, both in memory for its own health check and in
/// <c>platform.worker_heartbeats</c> so that an operator can tell a stopped worker from a busy one.
/// The row is per instance rather than global, because a single shared row would be kept fresh by
/// whichever instance still worked and would hide the one that had stopped.
/// </summary>
/// <param name="scopeFactory">Creates a scope per beat, so a failed write cannot poison a long-lived context.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Worker options.</param>
/// <param name="logger">Logger.</param>
public sealed class HeartbeatService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<WorkerOptions> options,
    ILogger<HeartbeatService> logger)
    : BackgroundService, IHeartbeatMonitor
{
    private long _lastBeatTicks;

    /// <inheritdoc />
    public DateTimeOffset? LastBeat
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastBeatTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public TimeSpan StaleAfter => options.Value.HeartbeatInterval * 3;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.HeartbeatInterval;
        WorkerLog.HeartbeatStarting(logger, options.Value.InstanceName, interval);

        await BeatAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await BeatAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown was requested; a stopping worker is expected to stop beating.
        }
    }

    private async Task BeatAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        Interlocked.Exchange(ref _lastBeatTicks, now.UtcTicks);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO platform.worker_heartbeats (instance_name, last_beat_at, version)
                 VALUES ({options.Value.InstanceName}, {now}, {Version})
                 ON CONFLICT (instance_name) DO UPDATE
                     SET last_beat_at = EXCLUDED.last_beat_at, version = EXCLUDED.version
                 """,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The database being briefly unavailable must not stop the worker: the in-memory beat still
            // proves the loop runs, and the readiness probe reports the database separately.
            WorkerLog.HeartbeatNotPersisted(logger, exception);
        }
    }

    private static string Version =>
        typeof(HeartbeatService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}

/// <summary>Source-generated log messages for the worker, so that logging costs nothing when disabled.</summary>
internal static partial class WorkerLog
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Worker heartbeat starting for instance {InstanceName} every {Interval}")]
    public static partial void HeartbeatStarting(ILogger logger, string instanceName, TimeSpan interval);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "The worker heartbeat could not be persisted; the instance keeps running.")]
    public static partial void HeartbeatNotPersisted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Information,
        Message = "Outbox dispatcher starting as {Owner}.")]
    public static partial void OutboxDispatcherStarting(ILogger logger, string owner);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Error,
        Message = "An outbox dispatch cycle failed; the loop continues.")]
    public static partial void OutboxCycleFailed(ILogger logger, Exception exception);
}
