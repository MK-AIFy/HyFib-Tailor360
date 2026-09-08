using Tailor360.Modules.Customers.Application.Preferences;

namespace Tailor360.Modules.Customers.Api.Payloads;

/// <summary>
/// How a customer wants to be reached, on the wire.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HasBeenRecorded"/> is the field that stops the screen guessing. An empty channel list
/// means nothing may be sent either way, but only a customer nobody has asked is a customer to ask;
/// one who chose no channel has already answered.
/// </para>
/// <para>
/// Quiet hours are wall-clock times in the branch's timezone and never instants — "not before eight in
/// the morning" means eight in the morning where she is, on whichever day the message is ready (BR-7).
/// </para>
/// </remarks>
/// <param name="CustomerId">The customer.</param>
/// <param name="HasBeenRecorded">False when nobody has recorded a preference for her.</param>
/// <param name="AllowedChannels">
/// The channels she accepts — <c>Sms</c>, <c>WhatsApp</c>, <c>Email</c> — in a stable order. May be
/// empty, and an empty list is an answer rather than a gap.
/// </param>
/// <param name="Language">
/// The language she is written to in. Taken from her own record when no preference has been recorded,
/// so it is always a language somebody chose.
/// </param>
/// <param name="QuietHoursStart">When the quiet window opens in the branch's local time, or null.</param>
/// <param name="QuietHoursEnd">When it closes, or null.</param>
/// <param name="UpdatedAt">When it was last changed, or null when it has never been recorded.</param>
/// <param name="Version">
/// The concurrency token a change must be made against, or null when nothing has been recorded — the
/// first write is the one change that needs no <c>If-Match</c>, because there is no version of a row
/// that does not exist.
/// </param>
public sealed record CommunicationPreferencePayload(
    Guid CustomerId,
    bool HasBeenRecorded,
    IReadOnlyList<string> AllowedChannels,
    string Language,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    DateTimeOffset? UpdatedAt,
    string? Version)
{
    /// <summary>Projects a preference onto the wire.</summary>
    /// <param name="preferences">The preference.</param>
    /// <returns>The payload.</returns>
    public static CommunicationPreferencePayload From(AdministeredPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return new CommunicationPreferencePayload(
            preferences.CustomerId,
            preferences.HasBeenRecorded,
            [.. preferences.AllowedChannels.Select(channel => channel.ToString())],
            preferences.Language,
            preferences.QuietHours?.Start,
            preferences.QuietHours?.End,
            preferences.UpdatedAt,
            // The unquoted row version, as every other payload in the module publishes it. The quotes
            // belong to the header, and a client that sent this back inside a second pair of them
            // would be refused as malformed.
            preferences.Version?.Version);
    }
}

/// <summary>The whole preference, as the customer now states it.</summary>
/// <remarks>
/// Every field is replaced, not merged. Sending an empty <see cref="AllowedChannels"/> is how she says
/// "do not message me"; omitting the quiet hours is how she says she named none. A partial update
/// would make "she accepts SMS" and "she has not mentioned SMS" the same request.
/// </remarks>
/// <param name="AllowedChannels">
/// The channels she accepts — <c>Sms</c>, <c>WhatsApp</c>, <c>Email</c>. Duplicates collapse and the
/// order does not matter; an empty list is valid.
/// </param>
/// <param name="Language">The language to write to her in, or null for the default.</param>
/// <param name="QuietHoursStart">
/// When the quiet window opens, in the branch's local time. Both ends or neither, and the two must
/// differ — a window that starts and ends together covers nothing, and "never message me" is an empty
/// channel list.
/// </param>
/// <param name="QuietHoursEnd">When it closes. May be earlier than the start, which is the usual case.</param>
public sealed record ReplacePreferencesRequest(
    IReadOnlyList<string>? AllowedChannels,
    string? Language,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd);
