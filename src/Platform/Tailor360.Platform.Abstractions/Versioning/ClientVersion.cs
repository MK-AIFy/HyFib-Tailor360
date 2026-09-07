using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tailor360.Platform.Abstractions.Versioning;

/// <summary>
/// A client build version, as it arrives in the <c>X-Client-Version</c> header: three numbers and an
/// optional pre-release label, ordered the way semantic versioning orders them.
/// </summary>
/// <remarks>
/// <para>
/// Only as much of the specification as the handshake needs is implemented, and the omissions are
/// deliberate rather than incidental. Build metadata after <c>+</c> is parsed and then ignored, which is
/// what semantic versioning requires: two builds of the same source differ there and are the same
/// version. Pre-release identifiers are compared as a whole string rather than dot-segment by
/// dot-segment, because this client's pre-releases are generated (<c>0.4.0-alpha.7</c>) and a
/// comparison that ordered <c>alpha.10</c> before <c>alpha.7</c> would only ever be wrong during a
/// release rehearsal — where the fix is to compare the release versions, not to carry a fuller
/// implementation into production.
/// </para>
/// <para>
/// A version that does not parse is not a version. The middleware treats it as absent rather than as
/// too old: the refusal exists so that a genuinely stale client is told to update, and answering
/// <c>426</c> to a value nobody can interpret would turn a typo in a script into an outage that reads
/// like a compatibility problem.
/// </para>
/// </remarks>
public readonly record struct ClientVersion : IComparable<ClientVersion>
{
    private ClientVersion(int major, int minor, int patch, string preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    /// <summary>The major version.</summary>
    public int Major { get; }

    /// <summary>The minor version.</summary>
    public int Minor { get; }

    /// <summary>The patch version.</summary>
    public int Patch { get; }

    /// <summary>The pre-release label without its leading hyphen, or empty for a release build.</summary>
    public string PreRelease { get; }

    /// <summary>The longest a header value may be before it is refused unparsed.</summary>
    /// <remarks>
    /// A bound, because this runs before authentication on every request and the value is attacker
    /// supplied. No real version comes close to it.
    /// </remarks>
    public const int MaxLength = 64;

    /// <summary>Parses a header value.</summary>
    /// <param name="candidate">The raw <c>X-Client-Version</c> value.</param>
    /// <param name="version">The parsed version, when it parsed.</param>
    /// <returns>True when the value is a version this comparison understands.</returns>
    public static bool TryParse(string? candidate, out ClientVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxLength)
        {
            return false;
        }

        var text = candidate.Trim();

        // Build metadata is discarded before anything else: it never participates in ordering, and
        // leaving it in would make an otherwise identical version compare as different.
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            text = text[..plus];
        }

        var preRelease = string.Empty;
        var hyphen = text.IndexOf('-', StringComparison.Ordinal);
        if (hyphen >= 0)
        {
            preRelease = text[(hyphen + 1)..];
            text = text[..hyphen];

            if (preRelease.Length == 0)
            {
                return false;
            }
        }

        var parts = text.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new ClientVersion(major, minor, patch, preRelease);
        return true;
    }

    /// <summary>Orders two versions.</summary>
    public int CompareTo(ClientVersion other)
    {
        if (Major != other.Major)
        {
            return Major.CompareTo(other.Major);
        }

        if (Minor != other.Minor)
        {
            return Minor.CompareTo(other.Minor);
        }

        if (Patch != other.Patch)
        {
            return Patch.CompareTo(other.Patch);
        }

        // A pre-release precedes the release it leads to: 1.2.0-alpha is older than 1.2.0.
        return (PreRelease.Length, other.PreRelease.Length) switch
        {
            (0, 0) => 0,
            (0, _) => 1,
            (_, 0) => -1,
            _ => string.CompareOrdinal(PreRelease, other.PreRelease),
        };
    }

    /// <summary>True when the left version orders before the right.</summary>
    public static bool operator <(ClientVersion left, ClientVersion right) => left.CompareTo(right) < 0;

    /// <summary>True when the left version orders after the right.</summary>
    public static bool operator >(ClientVersion left, ClientVersion right) => left.CompareTo(right) > 0;

    /// <summary>True when the left version orders before the right, or equals it.</summary>
    public static bool operator <=(ClientVersion left, ClientVersion right) => left.CompareTo(right) <= 0;

    /// <summary>True when the left version orders after the right, or equals it.</summary>
    public static bool operator >=(ClientVersion left, ClientVersion right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString()
    {
        var release = string.Create(
            CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        return PreRelease.Length == 0 ? release : release + "-" + PreRelease;
    }

    private static bool TryParseNumber(string part, [NotNullWhen(true)] out int value)
    {
        value = 0;

        // `int.TryParse` would accept a leading sign and surrounding whitespace, neither of which is a
        // version. Digits only, and a leading zero is refused as the specification requires.
        if (part.Length == 0 || part.Length > 9 || !part.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (part.Length > 1 && part[0] == '0')
        {
            return false;
        }

        value = int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture);
        return true;
    }
}
