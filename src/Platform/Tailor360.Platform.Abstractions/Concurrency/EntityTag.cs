using System.Globalization;

namespace Tailor360.Platform.Abstractions.Concurrency;

/// <summary>
/// An editable aggregate's concurrency token as it travels over HTTP: the row's version rendered as a
/// strong entity tag, which the client receives as <c>ETag</c> and sends back as <c>If-Match</c>.
/// </summary>
/// <remarks>
/// <para>
/// The version is PostgreSQL's <c>xmin</c> (<c>docs/architecture/conventions.md</c> section 4.1), so
/// nothing has to be maintained by hand and a row changed by an operator statement or a migration
/// changes it too. This type exists so that exactly one piece of code decides how that number is
/// written, quoted and compared: an aggregate whose tag was formatted one way and parsed another would
/// reject every update, and one compared loosely would accept a stale one.
/// </para>
/// <para>
/// Comparison is <b>strong</b> and ordinal, as RFC 9110 requires of <c>If-Match</c>. A weak validator
/// (<c>W/"…"</c>) never matches, because a weak tag says "semantically equivalent", and "near enough"
/// is not a basis on which to overwrite somebody else's edit.
/// </para>
/// </remarks>
/// <param name="Version">The row version, unquoted — the value the 409 body reports as <c>currentVersion</c>.</param>
public readonly record struct EntityTag(string Version)
{
    /// <summary>The wildcard <c>*</c>, which matches any existing representation.</summary>
    public static EntityTag Any { get; } = new("*");

    /// <summary>True when this tag is the wildcard rather than a specific version.</summary>
    public bool IsAny => string.Equals(Version, "*", StringComparison.Ordinal);

    /// <summary>The header form: the version in double quotes, for example <c>"48213"</c>.</summary>
    public string Value => IsAny ? "*" : $"\"{Version}\"";

    /// <summary>Builds a tag from a row's <c>xmin</c> concurrency token.</summary>
    /// <param name="rowVersion">The value Entity Framework holds in the <c>xmin</c> shadow property.</param>
    public static EntityTag From(uint rowVersion)
        => new(rowVersion.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Reads a header value. Returns false for anything that is not a strong entity tag, including a
    /// weak validator, an unquoted token and a list of several tags.
    /// </summary>
    /// <param name="headerValue">The raw <c>If-Match</c> or <c>ETag</c> header value.</param>
    /// <param name="tag">The parsed tag when the value was well formed.</param>
    public static bool TryParse(string? headerValue, out EntityTag tag)
    {
        tag = default;

        if (headerValue is null)
        {
            return false;
        }

        var trimmed = headerValue.Trim();

        if (trimmed.Length == 0 || trimmed.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(trimmed, "*", StringComparison.Ordinal))
        {
            tag = Any;
            return true;
        }

        // A weak validator is not rejected as malformed and then explained; it simply is not a strong
        // tag, and If-Match is defined only over strong ones.
        if (trimmed.StartsWith("W/", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Length < 3 || trimmed[0] != '"' || trimmed[^1] != '"')
        {
            return false;
        }

        var inner = trimmed[1..^1];
        if (inner.Length == 0 || inner.Contains('"', StringComparison.Ordinal))
        {
            return false;
        }

        tag = new EntityTag(inner);
        return true;
    }

    /// <summary>True when this tag names the same version as <paramref name="other"/>, or is the wildcard.</summary>
    /// <param name="other">The tag the stored row currently carries.</param>
    public bool Matches(EntityTag other)
        => IsAny || string.Equals(Version, other.Version, StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;
}
