namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Answers whether a proposed password already appears in a public list of exposed passwords.
/// </summary>
/// <remarks>
/// This is the one password rule that cannot be computed from the password alone, and it is worth more
/// than any composition rule: an attacker's first few million guesses come from exactly these lists, so
/// rejecting a password that is on one removes the whole of that attack.
/// <para>
/// The port exists so the check can be added without the rest of the system changing shape, and the
/// shipped implementation is <see cref="NullBreachedPasswordChecker"/>, which allows everything. A real
/// implementation reads a <em>local</em> list using the k-anonymity arrangement — look up the first five
/// characters of the SHA-1 digest, compare the remainder in memory — so that neither the password nor
/// its full digest ever leaves the process. An implementation that calls a remote service on the
/// sign-in path is not acceptable here: it would put a third party in the critical path of setting a
/// password and would leak a digest prefix for every staff member.
/// </para>
/// <para>
/// The implementation must not log the candidate, its digest, or anything derived from either.
/// </para>
/// </remarks>
public interface IBreachedPasswordChecker
{
    /// <summary>
    /// True when the candidate is known to be exposed. A checker that cannot reach its data answers
    /// <see langword="false"/> rather than failing: the other password rules still apply, and a missing
    /// list must not stop someone changing a password they believe is compromised.
    /// </summary>
    /// <param name="candidate">The proposed password. Never logged, never stored, never transmitted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<bool> IsBreachedAsync(string candidate, CancellationToken cancellationToken = default);
}
