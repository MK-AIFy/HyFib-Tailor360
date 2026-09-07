namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Produces a sheet of recovery codes and reduces a submitted code to the digest the account stores.
/// </summary>
/// <remarks>
/// Recovery codes are the way back in when the phone with the authenticator is lost, broken or in
/// another town, so they are shown once, at enrolment, and then exist only as digests. Two details
/// carry most of the value: a submitted code is normalised before it is hashed, because a person
/// reading a code off paper types the spaces and the case they see; and the codes are drawn from an
/// alphabet without the characters that are read wrongly off paper.
/// <para>
/// Implementations must not log a generated or submitted code.
/// </para>
/// </remarks>
public interface IRecoveryCodeService
{
    /// <summary>Generates a sheet of distinct codes with their digests.</summary>
    /// <param name="count">How many codes to issue.</param>
    IReadOnlyList<GeneratedRecoveryCode> Issue(int count);

    /// <summary>
    /// Reduces a submitted code to the stored digest, tolerating the spacing and case a person types.
    /// Returns <see langword="null"/> when the input cannot be a code at all.
    /// </summary>
    /// <param name="submitted">The code as typed. Never logged.</param>
    string? DigestOf(string? submitted);
}

/// <summary>One generated recovery code, in the two forms the caller needs at once.</summary>
/// <param name="Code">
/// The code as the holder sees it, shown once and never stored. Sensitive: never log it.
/// </param>
/// <param name="CodeHash">The digest the account stores in its place.</param>
public sealed record GeneratedRecoveryCode(string Code, string CodeHash);
