using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Preferences;

/// <summary>
/// The window in which a customer would rather not hear from the shop.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A wall-clock window in the branch's timezone, not an instant.</strong> "Not before eight in
/// the morning" means eight in the morning where the customer is, on whichever day the message is
/// ready — so the pair is stored as two <see cref="TimeOnly"/> values and evaluated against the branch
/// timezone at send time (BR-7), never converted to UTC here.
/// </para>
/// <para>
/// <strong>The window may run backwards over midnight, and usually does.</strong> 21:30 to 08:00 is the
/// ordinary case, so <see cref="Covers"/> handles a start later than its end rather than treating it as
/// a mistake. A window whose ends are equal covers nothing: somebody who wants never to be messaged
/// says so by allowing no channel, not by naming a zero-length hour.
/// </para>
/// <para>
/// <strong>This is the customer's own preference and nothing else.</strong> The branch's quiet-hours
/// policy, the escalation delays and the fallback rules are separate configuration owned by
/// Notifications (#47) and set per branch
/// (<c>docs/prd/workflows/branch-scenarios.md</c>); this module does not hold a default for them and
/// does not invent one. A customer who has expressed no preference has none, which is why the type is
/// nullable on the preference rather than initialised to a window nobody chose.
/// </para>
/// </remarks>
/// <param name="Start">When the quiet window opens, in the branch's local time.</param>
/// <param name="End">When it closes, in the branch's local time.</param>
public sealed record QuietHours(TimeOnly Start, TimeOnly End)
{
    /// <summary>Reads a pair of local times as a window, or says why they are not one.</summary>
    /// <param name="start">The opening time, or null.</param>
    /// <param name="end">The closing time, or null.</param>
    /// <returns>
    /// The window; null when neither was given; or the reason the pair was refused, which is that one
    /// end was given without the other.
    /// </returns>
    public static Result<QuietHours?> TryRead(TimeOnly? start, TimeOnly? end)
    {
        if (start is null && end is null)
        {
            return Result.Success<QuietHours?>(null);
        }

        // Half a window is not a preference anybody could act on, and guessing the missing end would
        // put a time the customer never gave into a rule that stops messages reaching them.
        if (start is null || end is null)
        {
            return Result.Failure<QuietHours?>(
                CustomersErrors.QuietHoursIncomplete(start is null ? "quietHoursStart" : "quietHoursEnd"));
        }

        return start.Value == end.Value
            ? Result.Failure<QuietHours?>(CustomersErrors.QuietHoursEmpty("quietHoursStart"))
            : Result.Success<QuietHours?>(new QuietHours(start.Value, end.Value));
    }

    /// <summary>Whether a local time falls inside the window.</summary>
    /// <param name="local">The time in the branch's timezone.</param>
    /// <returns>True when the customer would rather not hear from the shop then.</returns>
    public bool Covers(TimeOnly local)
        => Start < End
            ? local >= Start && local < End
            : local >= Start || local < End;
}
