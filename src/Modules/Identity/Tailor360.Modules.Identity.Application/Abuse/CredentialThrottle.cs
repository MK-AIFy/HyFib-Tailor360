using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Abuse;

/// <summary>
/// The shipped throttle: fixed windows held in memory, one per (endpoint, key) pair.
/// </summary>
/// <remarks>
/// <para>
/// A fixed window rather than a sliding one because the difference does not matter here. The worst a
/// fixed window allows is twice the limit across a window boundary, and the limits are set an order of
/// magnitude below what would let an online guessing attack succeed, so doubling them briefly changes
/// nothing an attacker can use.
/// </para>
/// <para>
/// The map is pruned opportunistically rather than by a timer, so an idle instance holds no background
/// work and a busy one clears its own dead entries. The prune is bounded per call, so a burst that
/// created a hundred thousand keys does not turn one unlucky request into a long pause.
/// </para>
/// </remarks>
/// <param name="clock">The clock. Windows are measured with it, never with the ambient time.</param>
/// <param name="options">The configured limits.</param>
public sealed class CredentialThrottle(IClock clock, IOptions<CredentialThrottleOptions> options)
    : ICredentialThrottle
{
    /// <summary>How many dead entries one call may clear.</summary>
    private const int PruneBudget = 64;

    /// <summary>How many entries the map may hold before a call prunes.</summary>
    private const int PruneThreshold = 4096;

    private readonly CredentialThrottleOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ThrottleDecision Check(CredentialAction action, string? accountKey, string? clientKey)
    {
        var rule = _options.For(action);
        var now = clock.UtcNow;

        var account = Peek(Key(action, "a", accountKey), rule.Window, now);
        var client = Peek(Key(action, "c", clientKey), rule.Window, now);

        var captcha = rule.CaptchaAfter > 0 && client.Count >= rule.CaptchaAfter;

        if (accountKey is not null && account.Count >= rule.PerAccount)
        {
            return new ThrottleDecision(false, Remaining(account, rule.Window, now), captcha);
        }

        if (clientKey is not null && client.Count >= rule.PerClient)
        {
            return new ThrottleDecision(false, Remaining(client, rule.Window, now), captcha);
        }

        return new ThrottleDecision(true, TimeSpan.Zero, captcha);
    }

    /// <inheritdoc />
    public void RecordFailure(CredentialAction action, string? accountKey, string? clientKey)
    {
        var rule = _options.For(action);
        var now = clock.UtcNow;

        Increment(Key(action, "a", accountKey), rule.Window, now);
        Increment(Key(action, "c", clientKey), rule.Window, now);

        Prune(now);
    }

    /// <inheritdoc />
    public void RecordSuccess(CredentialAction action, string? accountKey, string? clientKey)
    {
        // The address counter is deliberately untouched. Guessing one password out of a thousand must
        // not buy an attacker a clean slate for the next thousand.
        _ = clientKey;

        if (Key(action, "a", accountKey) is { } key)
        {
            _windows.TryRemove(key, out _);
        }

        Prune(clock.UtcNow);
    }

    private static string? Key(CredentialAction action, string scope, string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{(int)action}:{scope}:{value.Trim().ToUpperInvariant()}");

    private Window Peek(string? key, TimeSpan window, DateTimeOffset now)
    {
        if (key is null || !_windows.TryGetValue(key, out var existing) || existing.HasLapsed(window, now))
        {
            return new Window(now, 0);
        }

        return existing;
    }

    private void Increment(string? key, TimeSpan window, DateTimeOffset now)
    {
        if (key is null)
        {
            return;
        }

        _windows.AddOrUpdate(
            key,
            _ => new Window(now, 1),
            (_, existing) => existing.HasLapsed(window, now)
                ? new Window(now, 1)
                : existing with { Count = existing.Count + 1 });
    }

    private static TimeSpan Remaining(Window window, TimeSpan length, DateTimeOffset now)
    {
        var remaining = window.StartedAt + length - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
    }

    private void Prune(DateTimeOffset now)
    {
        if (_windows.Count < PruneThreshold)
        {
            return;
        }

        // The longest window any rule uses, so an entry is only removed once no rule could still be
        // counting it. Reading the widest rule rather than the entry's own is what lets one map hold
        // every endpoint's counters.
        var longest = Longest();
        var cleared = 0;

        foreach (var (key, window) in _windows)
        {
            if (cleared >= PruneBudget)
            {
                return;
            }

            if (window.HasLapsed(longest, now))
            {
                _windows.TryRemove(key, out _);
                cleared++;
            }
        }
    }

    private TimeSpan Longest()
    {
        var longest = _options.SignIn.Window;

        if (_options.MultiFactorChallenge.Window > longest)
        {
            longest = _options.MultiFactorChallenge.Window;
        }

        return _options.Recovery.Window > longest ? _options.Recovery.Window : longest;
    }

    private readonly record struct Window(DateTimeOffset StartedAt, int Count)
    {
        public bool HasLapsed(TimeSpan length, DateTimeOffset now) => now - StartedAt >= length;
    }
}
