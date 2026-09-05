namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Mints the value that travels in a recovery link, and reduces one that comes back to the digest the
/// token row stores.
/// </summary>
/// <remarks>
/// The value is a high-entropy random string rather than anything derived from the account, so a link
/// reveals nothing about whose it is even before it is used; and it is URL-safe, so it survives an
/// email client that rewrites links.
/// <para>
/// Implementations must not log a minted or submitted value: it is a working credential for as long as
/// the token lives.
/// </para>
/// </remarks>
public interface IRecoveryTokenService
{
    /// <summary>Mints one token in the two forms the caller needs at once.</summary>
    IssuedRecoveryToken Issue();

    /// <summary>
    /// Reduces a submitted value to its stored digest, or <see langword="null"/> when the input cannot
    /// be a token at all.
    /// </summary>
    string? DigestOf(string? submitted);
}

/// <summary>A minted recovery token.</summary>
/// <param name="Value">The value that goes in the link. Sensitive: never log it, never store it.</param>
/// <param name="TokenHash">The digest stored in its place.</param>
public sealed record IssuedRecoveryToken(string Value, string TokenHash);
