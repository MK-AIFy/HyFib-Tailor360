namespace Tailor360.Modules.Customers.Api.Payloads;

/// <summary>Reads an enumeration member from the name a request carried.</summary>
/// <remarks>
/// <para>
/// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> is not usable on its own for a wire format,
/// because it accepts far more than the member names: <c>"99"</c> parses happily into a member that does
/// not exist, <c>"-1"</c> likewise, and <c>"Millimetre, Count"</c> parses into a combination that is not a
/// member either. Every one of those then travels through the domain and into storage as an unusable value
/// whose published field has, for instance, no display units at all.
/// </para>
/// <para>
/// Numbers are refused rather than resolved for a second reason: a caller that sends <c>3</c> today would
/// mean something else entirely the day somebody inserts a member above it, and nothing would notice.
/// The names are the contract, so the text is matched against the declared names and nothing else.
/// </para>
/// </remarks>
internal static class EnumText
{
    /// <summary>Reads a member by name, case-insensitively.</summary>
    /// <typeparam name="TEnum">The enumeration.</typeparam>
    /// <param name="text">What the request carried.</param>
    /// <param name="value">The member, when the text named one.</param>
    /// <returns>False when the text is absent, is a number, or names no member.</returns>
    public static bool TryRead<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                value = Enum.Parse<TEnum>(name);
                return true;
            }
        }

        return false;
    }
}
