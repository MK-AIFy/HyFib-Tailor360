using System.Collections.Concurrent;
using Tailor360.Modules.Identity.Infrastructure.Sessions;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Infrastructure.Passkeys;

/// <summary>
/// Holds a started WebAuthn ceremony until its answer comes back, or until it expires.
/// </summary>
/// <remarks>
/// <para>
/// The challenge is kept server-side rather than handed to the client to give back. A challenge the
/// client stores is a challenge the client chooses, and the whole point of a challenge is that the
/// server picked it.
/// </para>
/// <para>
/// <b>Two things bind a ceremony to the browser that started it.</b> The handle is 256 bits of server
/// entropy, so it cannot be guessed; and when the request carried a session, the session identifier is
/// stored beside it and must match on return. That is what stops the substitution attack a registration
/// ceremony is otherwise open to — an attacker starts a ceremony under their own session, gets a victim
/// to complete it, and ends up with their authenticator enrolled on the victim's account.
/// </para>
/// <para>
/// A ceremony is consumed on the first answer, right or wrong, so a captured response cannot be
/// replayed against the same challenge. Entries are held in memory and are lost on restart, which
/// costs a person one retry of a ceremony that lives for three minutes; anything durable would mean
/// storing a challenge in a table for the sake of that retry.
/// </para>
/// </remarks>
/// <param name="clock">The clock. Expiry is measured with it, never with the ambient time.</param>
public sealed class PasskeyCeremonyStore(IClock clock)
{
    /// <summary>How many entries may be held before a start prunes the expired ones.</summary>
    private const int PruneThreshold = 512;

    private readonly ConcurrentDictionary<string, Entry> _ceremonies = new(StringComparer.Ordinal);

    /// <summary>Stores one started ceremony and returns its handle.</summary>
    /// <param name="optionsJson">The options object issued to the browser.</param>
    /// <param name="boundSessionId">The session that started it, when there was one.</param>
    /// <param name="lifetime">How long the challenge stays answerable.</param>
    public (string CeremonyId, DateTimeOffset ExpiresAt) Start(
        string optionsJson,
        Guid? boundSessionId,
        TimeSpan lifetime)
    {
        var now = clock.UtcNow;
        var expiresAt = now + lifetime;
        var ceremonyId = SessionTokenFactory.CreateToken();

        _ceremonies[ceremonyId] = new Entry(optionsJson, boundSessionId, expiresAt);
        Prune(now);

        return (ceremonyId, expiresAt);
    }

    /// <summary>
    /// Takes a ceremony back out, once. Returns null when the handle is unknown, already spent, past
    /// its expiry, or belongs to a different session from the one answering.
    /// </summary>
    public string? Consume(string? ceremonyId, Guid? boundSessionId)
    {
        if (string.IsNullOrWhiteSpace(ceremonyId) || !_ceremonies.TryRemove(ceremonyId, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= clock.UtcNow)
        {
            return null;
        }

        return entry.BoundSessionId == boundSessionId ? entry.OptionsJson : null;
    }

    private void Prune(DateTimeOffset now)
    {
        if (_ceremonies.Count < PruneThreshold)
        {
            return;
        }

        foreach (var (key, entry) in _ceremonies)
        {
            if (entry.ExpiresAt <= now)
            {
                _ceremonies.TryRemove(key, out _);
            }
        }
    }

    private sealed record Entry(string OptionsJson, Guid? BoundSessionId, DateTimeOffset ExpiresAt);
}
