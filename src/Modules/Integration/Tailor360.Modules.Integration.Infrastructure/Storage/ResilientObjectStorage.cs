using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Integration.Infrastructure.Storage;

/// <summary>
/// Wraps whichever <see cref="IObjectStorage"/> the module's factory selected with a bulkhead, a circuit
/// breaker and a per-call timeout — <c>docs/architecture/resilience-policies.md</c>'s object-storage row.
/// Registered as the concrete singleton other services depend on for its own state
/// (<see cref="Breaker"/>), with <c>IObjectStorage</c> itself resolving to this instance, the same double
/// registration <c>HeartbeatService</c> uses for <c>IHeartbeatMonitor</c>.
/// </summary>
/// <remarks>
/// <para>
/// **The bulkhead is checked before the breaker.** Concurrency is bounded regardless of whether the
/// dependency looks healthy — a burst of calls while the breaker is closed must still be shed past the
/// limit, never queued (<c>docs/nfr/capacity-and-performance.md</c> section 2.4's "6" and "2" figures are
/// concurrency ceilings, not queue depths). Because the bulkhead slot is acquired first, a breaker refusal
/// releases it immediately and never starts a phantom half-open trial.
/// </para>
/// <para>
/// **Every failure this class raises is <see cref="ObjectStorageUnavailableException"/>**, never the
/// underlying adapter's own exception type. The real exception — MinIO's, or a timeout — is logged once,
/// under one of the events below, and never appears in the message a caller sees, matching the redaction
/// convention <c>DatabaseHealthCheck</c> already follows: a connection failure can name the host.
/// </para>
/// </remarks>
public sealed class ResilientObjectStorage : IObjectStorage, IDisposable
{
    private readonly IObjectStorage _inner;
    private readonly SemaphoreSlim _bulkhead;
    private readonly TimeSpan _callTimeout;
    private readonly ILogger<ResilientObjectStorage> _logger;

    /// <summary>Builds the decorator around an already-selected adapter.</summary>
    /// <param name="inner">The real adapter — MinIO, or the in-memory collector.</param>
    /// <param name="options">The bulkhead, breaker and timeout settings.</param>
    /// <param name="clock">The clock the breaker's break duration is measured against.</param>
    /// <param name="logger">Logger.</param>
    public ResilientObjectStorage(IObjectStorage inner, IOptions<ObjectStorageOptions> options, IClock clock, ILogger<ResilientObjectStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = options.Value;
        _inner = inner;
        _logger = logger;
        _bulkhead = new SemaphoreSlim(settings.Bulkhead.MaxConcurrentCalls, settings.Bulkhead.MaxConcurrentCalls);
        _callTimeout = settings.CallTimeout;
        Breaker = new ObjectStorageCircuitBreaker(clock, settings.Breaker.FailureThreshold, settings.Breaker.BreakDuration);
    }

    /// <summary>The breaker's own state machine, read by <see cref="ObjectStorageHealthCheck"/>.</summary>
    public ObjectStorageCircuitBreaker Breaker { get; }

    /// <inheritdoc />
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            async token =>
            {
                await _inner.PutAsync(key, content, contentType, token);
                return true;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        => ExecuteAsync(token => _inner.OpenReadAsync(key, token), cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => ExecuteAsync(token => _inner.ExistsAsync(key, token), cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Also disposes the wrapped adapter: <c>MinioObjectStorage</c> is constructed inside this class's own
    /// registration factory rather than registered against <c>IObjectStorage</c> directly, so the
    /// container no longer sees it to dispose on its own — this is where that responsibility moved to.
    /// </remarks>
    public void Dispose()
    {
        _bulkhead.Dispose();
        (_inner as IDisposable)?.Dispose();
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (!await _bulkhead.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            IntegrationStorageLog.BulkheadRejected(_logger);
            throw new ObjectStorageUnavailableException("The object-storage bulkhead has no free concurrent call slot.");
        }

        try
        {
            if (!Breaker.TryEnter())
            {
                IntegrationStorageLog.CircuitOpen(_logger);
                throw new ObjectStorageUnavailableException("The object-storage circuit breaker is open.");
            }

            using var timeoutSource = new CancellationTokenSource(_callTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            try
            {
                var result = await operation(linked.Token);
                Breaker.RecordSuccess();
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller cancelled, not the dependency: this is not a breaker failure, and the caller
                // is told exactly what it asked to be told.
                throw;
            }
            catch (OperationCanceledException)
            {
                // Only the internal timeout source could still be signalled here.
                Breaker.RecordFailure();
                IntegrationStorageLog.CallTimedOut(_logger, _callTimeout);
                throw new ObjectStorageUnavailableException("The object-storage call did not complete within its timeout.");
            }
            catch (Exception exception)
            {
                Breaker.RecordFailure();
                IntegrationStorageLog.CallFailed(_logger, exception);
                throw new ObjectStorageUnavailableException("The object-storage call failed.");
            }
        }
        finally
        {
            _bulkhead.Release();
        }
    }
}

/// <summary>Source-generated log messages for the object-storage resilience layer.</summary>
internal static partial class IntegrationStorageLog
{
    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Warning,
        Message = "An object-storage call was rejected: the bulkhead has no free concurrent call slot.")]
    public static partial void BulkheadRejected(ILogger logger);

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Warning,
        Message = "An object-storage call was rejected: the circuit breaker is open.")]
    public static partial void CircuitOpen(ILogger logger);

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Error,
        Message = "An object-storage call did not complete within its {Timeout} timeout.")]
    public static partial void CallTimedOut(ILogger logger, TimeSpan timeout);

    [LoggerMessage(
        EventId = 5104,
        Level = LogLevel.Error,
        Message = "An object-storage call failed.")]
    public static partial void CallFailed(ILogger logger, Exception exception);
}
