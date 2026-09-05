namespace Tailor360.Platform.Security.Authentication;

/// <summary>
/// Resolves a presented session cookie into the facts the request pipeline needs. Implemented by the
/// module that owns the session table; this project deliberately knows nothing about how or where a
/// session is stored, which is what keeps <c>Platform.Security</c> free of a module reference.
/// </summary>
/// <remarks>
/// The implementation is called on <b>every</b> request that presents a cookie. Two things follow.
/// It must be cheap — one indexed lookup on the digest of the presented value — and it must be the
/// place revocation is honoured, because a revocation that is only checked at sign-in is not a
/// revocation at all. Sliding the inactivity deadline also belongs here, since it is the same row.
/// </remarks>
public interface ISessionTicketStore
{
    /// <summary>
    /// Looks up the session a cookie value names, verifies that it may still be used, and slides its
    /// inactivity deadline.
    /// </summary>
    /// <param name="presentedToken">
    /// The raw cookie value as the browser sent it. Implementations compare its digest, never the value,
    /// and must never log it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SessionResolution> ResolveAsync(string presentedToken, CancellationToken cancellationToken = default);
}
