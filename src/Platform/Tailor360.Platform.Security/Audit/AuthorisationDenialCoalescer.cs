using System.Collections.Concurrent;

namespace Tailor360.Platform.Security.Audit;

/// <summary>
/// Decides whether one refused request is worth its own audit entry, given what has already been
/// recorded this minute.
/// </summary>
/// <remarks>
/// <para>
/// A refused request is recorded once per actor, per endpoint, per minute. A person whose client
/// retries a forbidden command forty times in ten seconds did one thing worth recording, and forty
/// entries would bury the one denial somebody was looking for in the thirty-nine that say the same.
/// Coalescing is therefore about identity, not volume: it collapses <em>repetitions of the same event</em>
/// and nothing else.
/// </para>
/// <para>
/// <b>Nothing is ever sampled.</b> A distinct actor, a distinct endpoint or a distinct minute is always
/// a new entry, however many have already been written — the eleventh actor to be refused in a minute is
/// exactly the one the investigation needs. That is why the window below has no rate limit in it, only a
/// memory bound; and why reaching that bound turns coalescing <em>off</em> rather than turning recording
/// off. Overflow makes the trail noisier, never shorter, because a spike in denials is the moment the
/// trail matters most.
/// </para>
/// </remarks>
/// <param name="capacity">
/// How many distinct keys are remembered before coalescing is abandoned. It bounds the memory an
/// unauthenticated flood can make this hold; there is no bound on what is recorded.
/// </param>
public sealed class AuthorisationDenialCoalescer(int capacity = AuthorisationDenialCoalescer.DefaultCapacity)
{
    /// <summary>How many distinct actor-endpoint-minute keys are remembered by default.</summary>
    public const int DefaultCapacity = 20_000;

    private readonly ConcurrentDictionary<DenialKey, byte> _seen = new();
    private readonly int _capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");

    /// <summary>How many distinct keys are currently remembered. For tests and diagnostics.</summary>
    public int Remembered => _seen.Count;

    /// <summary>
    /// True when this refusal is the first of its actor, endpoint and minute — or when the window is
    /// full, in which case every refusal is recorded rather than any being dropped.
    /// </summary>
    /// <param name="actor">The actor's stable identifier, or the anonymous principal.</param>
    /// <param name="endpoint">The endpoint signature, for example <c>POST /api/v1/orders</c>.</param>
    /// <param name="now">The instant of the refusal.</param>
    public bool ShouldRecord(string actor, string endpoint, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var minute = now.ToUnixTimeSeconds() / 60;
        Forget(minute);

        // Full: record everything. The alternative is to decide which denials do not matter, in the one
        // situation where that decision cannot be made safely.
        return _seen.Count >= _capacity || _seen.TryAdd(new DenialKey(actor, endpoint, minute), 0);
    }

    /// <summary>
    /// Drops keys from minutes that have passed. Keeping one minute either side of the current one
    /// costs nothing and means a request whose clock reading straddles a boundary is not double-counted.
    /// </summary>
    private void Forget(long minute)
    {
        if (_seen.IsEmpty)
        {
            return;
        }

        foreach (var key in _seen.Keys)
        {
            if (Math.Abs(minute - key.Minute) > 1)
            {
                _seen.TryRemove(key, out _);
            }
        }
    }

    private readonly record struct DenialKey(string Actor, string Endpoint, long Minute);
}
