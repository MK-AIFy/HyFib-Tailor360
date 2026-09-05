namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Mints an opaque, high-entropy value and reduces one that comes back to the digest that is stored in
/// its place.
/// </summary>
/// <remarks>
/// It exists for the trusted-device cookie, which is a bearer credential with the same properties as a
/// session value: server-generated, structureless, and stored only as a digest. The port keeps the
/// generator out of the application layer so that the handler that remembers a device cannot decide
/// how much entropy is enough.
/// </remarks>
public interface IOpaqueTokenFactory
{
    /// <summary>Mints one value in the two forms the caller needs at once.</summary>
    OpaqueToken Issue();

    /// <summary>
    /// Reduces a submitted value to its stored digest, or <see langword="null"/> when the input cannot
    /// be one of ours.
    /// </summary>
    string? DigestOf(string? submitted);
}

/// <summary>A minted opaque token.</summary>
/// <param name="Value">The value that goes in the cookie. Sensitive: never logged, never stored.</param>
/// <param name="TokenHash">The digest stored in its place.</param>
public sealed record OpaqueToken(string Value, string TokenHash);
