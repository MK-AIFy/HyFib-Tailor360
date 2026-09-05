using Microsoft.Extensions.Logging;

namespace Tailor360.Platform.Security.Authentication;

/// <summary>
/// The fail-closed ticket store used when no module has supplied a real one. It recognises no session,
/// so every cookie presented to such a host is refused and cleared.
/// </summary>
/// <remarks>
/// A host that composes the security services without the module that owns the session table is
/// misconfigured. Refusing every session is the safe way for that to present: the alternative — no
/// registration at all — is an exception on the first authenticated request, which is a 500 where a 401
/// belongs and is far harder to read in a deployment log.
/// </remarks>
/// <param name="logger">Logger.</param>
public sealed class UnavailableSessionTicketStore(ILogger<UnavailableSessionTicketStore> logger)
    : ISessionTicketStore
{
    /// <inheritdoc />
    public Task<SessionResolution> ResolveAsync(
        string presentedToken,
        CancellationToken cancellationToken = default)
    {
        logger.LogError(
            "A session cookie was presented but no session ticket store is registered. The host is "
            + "composed without the module that owns sessions, so every session is refused.");

        return Task.FromResult(SessionResolution.Unknown);
    }
}
