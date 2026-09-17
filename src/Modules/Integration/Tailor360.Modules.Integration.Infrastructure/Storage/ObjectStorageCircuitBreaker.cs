using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Integration.Infrastructure.Storage;

/// <summary>The circuit breaker's own three states, independent of whatever <see cref="IObjectStorage"/> it guards.</summary>
public enum ObjectStorageCircuitState
{
    /// <summary>Calls pass through. A run of consecutive failures reaching the threshold opens the breaker.</summary>
    Closed,

    /// <summary>Every call is refused immediately, without reaching the dependency, until the break duration elapses.</summary>
    Open,

    /// <summary>The break has elapsed; exactly one trial call is admitted to decide whether to close or re-open.</summary>
    HalfOpen,
}

/// <summary>
/// A minimal circuit breaker: opens after a run of consecutive failures, stays open for a fixed duration,
/// then admits exactly one trial call. The trial's own outcome decides the next state — success closes the
/// breaker and resets the failure count, failure re-opens it immediately without waiting for the threshold
/// again. This is the whole state machine <see cref="ResilientObjectStorage"/> wraps around
/// <c>IObjectStorage</c>; it has no dependency on that interface itself; so it is testable without a real
/// or fake adapter.
/// </summary>
/// <param name="clock">The clock the break duration is measured against.</param>
/// <param name="failureThreshold">Consecutive failures before the breaker opens. At least 1.</param>
/// <param name="breakDuration">How long the breaker stays open before half-opening. Positive.</param>
public sealed class ObjectStorageCircuitBreaker(IClock clock, int failureThreshold, TimeSpan breakDuration)
{
    private readonly Lock _gate = new();
    private ObjectStorageCircuitState _state = ObjectStorageCircuitState.Closed;
    private int _consecutiveFailures;
    private DateTimeOffset _openedAt;
    private bool _halfOpenTrialInFlight;

    /// <summary>The breaker's current state, resolving an elapsed break into <see cref="ObjectStorageCircuitState.HalfOpen"/> as it is read.</summary>
    public ObjectStorageCircuitState State
    {
        get
        {
            lock (_gate)
            {
                return Observe();
            }
        }
    }

    /// <summary>
    /// Asks to make one call. Returns <see langword="true"/> when the caller may proceed — the breaker is
    /// closed, or it is half-open and no trial call is already outstanding. A caller that receives
    /// <see langword="true"/> in the half-open state **must** report the outcome through
    /// <see cref="RecordSuccess"/> or <see cref="RecordFailure"/>, because it is the one trial the breaker
    /// is waiting on.
    /// </summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            var state = Observe();
            if (state == ObjectStorageCircuitState.Closed)
            {
                return true;
            }

            if (state == ObjectStorageCircuitState.HalfOpen && !_halfOpenTrialInFlight)
            {
                _halfOpenTrialInFlight = true;
                return true;
            }

            return false;
        }
    }

    /// <summary>Reports that the admitted call succeeded. Closes the breaker and clears the failure count, from any state.</summary>
    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _halfOpenTrialInFlight = false;
            _state = ObjectStorageCircuitState.Closed;
        }
    }

    /// <summary>Reports that the admitted call failed. A half-open trial's failure re-opens immediately; otherwise the failure count advances toward the threshold.</summary>
    public void RecordFailure()
    {
        lock (_gate)
        {
            var state = Observe();
            _halfOpenTrialInFlight = false;

            if (state == ObjectStorageCircuitState.HalfOpen)
            {
                Open();
                return;
            }

            _consecutiveFailures++;
            if (_consecutiveFailures >= failureThreshold)
            {
                Open();
            }
        }
    }

    private void Open()
    {
        _state = ObjectStorageCircuitState.Open;
        _openedAt = clock.UtcNow;
        _consecutiveFailures = 0;
    }

    /// <summary>Must be called while holding <see cref="_gate"/>. Advances Open to HalfOpen once the break has elapsed.</summary>
    private ObjectStorageCircuitState Observe()
    {
        if (_state == ObjectStorageCircuitState.Open && clock.UtcNow - _openedAt >= breakDuration)
        {
            _state = ObjectStorageCircuitState.HalfOpen;
        }

        return _state;
    }
}
