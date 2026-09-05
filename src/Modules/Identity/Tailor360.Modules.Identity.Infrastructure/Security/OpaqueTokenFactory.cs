using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Infrastructure.Sessions;

namespace Tailor360.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Mints the remembered-device cookie value, using the same generator and the same digest as the
/// session cookie.
/// </summary>
/// <remarks>
/// Sharing <see cref="SessionTokenFactory"/> is deliberate. The two values have identical properties —
/// 256 bits of server entropy, no structure, stored only as a lower-case hexadecimal SHA-256 — and the
/// database check constraint on the device table enforces that shape. A second implementation would be
/// a second place for those properties to drift.
/// </remarks>
public sealed class OpaqueTokenFactory : IOpaqueTokenFactory
{
    /// <inheritdoc />
    public OpaqueToken Issue()
    {
        var value = SessionTokenFactory.CreateToken();
        return new OpaqueToken(value, SessionTokenFactory.Digest(value));
    }

    /// <inheritdoc />
    public string? DigestOf(string? submitted)
        => string.IsNullOrWhiteSpace(submitted) ? null : SessionTokenFactory.Digest(submitted);
}
