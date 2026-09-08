using Tailor360.Modules.Customers.Domain.Preferences;
using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Customers.Application.Preferences;

/// <summary>
/// Writes the communication-preference trail.
/// </summary>
/// <remarks>
/// Same ordering rule as the rest of the module: save the change first, then record it, because the
/// trail may lag reality and must never lead it. What reaches the trail is the shape of the answer —
/// which channels, which language, whether a quiet window was named — and not the window itself. A
/// reader of the trail needs to know she asked not to be messaged at night; the hours are on the
/// record for whoever may read it.
/// </remarks>
internal static class PreferenceAudit
{
    /// <summary>The entity type preference entries are recorded against.</summary>
    public const string EntityType = "customers.customer";

    /// <summary>Records one change and commits the entry.</summary>
    /// <param name="audit">The platform's audit writer.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="before">The preference before, or null when there was none.</param>
    /// <param name="after">The preference now.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the entry is committed.</returns>
    public static async Task RecordAsync(
        IAuditWriter audit,
        Guid customerId,
        PreferenceTrailEntry? before,
        PreferenceTrailEntry after,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(after);

        var summary = before is null
            ? "Communication preference recorded for the first time."
            : "Communication preference replaced.";

        await audit.WriteAsync(
            new AuditEntry(
                PreferenceHandler.ChangedAction,
                EntityType,
                customerId,
                summary,
                Reason: null,
                Before: before,
                After: after),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}

/// <summary>A communication preference as the trail describes it.</summary>
/// <param name="AllowedChannels">The channel names, in a stable order. An empty list is an answer.</param>
/// <param name="Language">The language, which is a preference and not an identifier.</param>
/// <param name="HasQuietHours">Whether a quiet window was named. Not the window.</param>
internal sealed record PreferenceTrailEntry(
    IReadOnlyList<string> AllowedChannels,
    string Language,
    bool HasQuietHours)
{
    /// <summary>Takes a trail entry from a preference.</summary>
    /// <param name="preferences">The preference.</param>
    /// <returns>The entry.</returns>
    public static PreferenceTrailEntry Of(CommunicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return new PreferenceTrailEntry(
            [.. preferences.AllowedChannels.Select(channel => channel.ToString())],
            preferences.Language,
            preferences.QuietHours is not null);
    }
}
