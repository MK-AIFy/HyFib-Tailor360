namespace Tailor360.Modules.Customers.Contracts.Preferences;

/// <summary>
/// What another module may know about how a customer wants to be reached.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A preference is not a consent, and both are checked.</strong> Consent says whether the shop
/// may send about a purpose at all; this says how, in which language, and when not to. Notifications
/// (#47) evaluates <see cref="Tailor360.Modules.Customers.Contracts.Consent.IConsentQuery"/> and this
/// query before every send, and a refusal on either is recorded as a suppression with its reason
/// (<c>docs/prd/glossary.md</c>). Allowing a
/// channel here consents to nothing, and consenting does not oblige the shop to use a channel the
/// customer switched off.
/// </para>
/// <para>
/// <strong>An empty channel set is an answer, not a gap.</strong> It is how a customer says "do not
/// message me" without withdrawing consent to the purposes themselves, so
/// <see cref="CommunicationPreference.HasBeenRecorded"/> is what separates it from a customer nobody
/// has asked. Both mean nothing goes out; only the second is a reason to ask.
/// </para>
/// <para>
/// <strong>This query does not tell you whether the customer exists</strong>, for the reason
/// <see cref="Tailor360.Modules.Customers.Contracts.Consent.IConsentQuery"/> does not: it answers what
/// may be sent, and for a customer nobody has heard of the answer is nothing.
/// </para>
/// </remarks>
public interface ICommunicationPreferenceQuery
{
    /// <summary>How this customer may be reached.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The preference, which is never null.</returns>
    Task<CommunicationPreference> GetAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}

/// <summary>How a customer may be reached, as another module sees it.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="HasBeenRecorded">
/// False when nobody has recorded a preference for this customer. The channel set is then empty for
/// the same reason it is empty for a customer who chose no channel — nothing may be sent either way —
/// but only this one is a prompt to ask her.
/// </param>
/// <param name="AllowedChannels">
/// The channels the customer accepts, in a stable order. May be empty, and an empty set is a valid
/// answer rather than an unset one.
/// </param>
/// <param name="Language">
/// The language to write to the customer in. Taken from the preference when one has been recorded and
/// from the customer's own record when none has, so it is always a language somebody chose rather than
/// a default invented here.
/// </param>
/// <param name="QuietHours">
/// The window the customer would rather not hear from the shop in, or null when they named none. A
/// customer who expressed no preference has none; this is not initialised to a window nobody chose.
/// </param>
public sealed record CommunicationPreference(
    Guid CustomerId,
    bool HasBeenRecorded,
    IReadOnlyList<MessageChannel> AllowedChannels,
    string Language,
    QuietWindow? QuietHours)
{
    /// <summary>Whether a channel may carry a message to this customer.</summary>
    /// <param name="channel">The channel the caller is considering.</param>
    /// <returns>True when the customer accepts it.</returns>
    public bool Allows(MessageChannel channel) => AllowedChannels.Contains(channel);

    /// <summary>The answer for a customer whose preference nobody has recorded.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="language">The language from the customer's own record.</param>
    /// <returns>The preference.</returns>
    public static CommunicationPreference NotRecorded(Guid customerId, string language)
        => new(customerId, false, [], language, null);
}

/// <summary>
/// A way the shop can reach a customer, as another module names it.
/// </summary>
/// <remarks>
/// The published counterpart of the module's own <c>CommunicationChannel</c>. The two carry the same
/// members and are kept in step by a test rather than by a shared type, because a <c>Contracts</c>
/// project may reference <c>Platform.Abstractions</c> and nothing else — which is also what stops a
/// consumer taking a dependency on the module's domain enumeration and being broken by a member the
/// module adds for its own reasons.
/// </remarks>
public enum MessageChannel
{
    /// <summary>A text message.</summary>
    Sms,

    /// <summary>A WhatsApp message.</summary>
    WhatsApp,

    /// <summary>An email.</summary>
    Email,
}

/// <summary>
/// The window in which a customer would rather not hear from the shop.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Wall-clock times in the branch's timezone, not instants.</strong> "Not before eight in the
/// morning" means eight in the morning where the customer is, on whichever day the message is ready,
/// so the pair is carried as two <see cref="TimeOnly"/> values and evaluated against the branch
/// timezone at send time (BR-7).
/// </para>
/// <para>
/// <strong>The window usually runs backwards over midnight</strong> — 21:30 to 08:00 is the ordinary
/// case — so <see cref="Covers"/> handles a start later than its end. That arithmetic is written twice,
/// here and in the module's own <c>QuietHours</c>, because the architecture forbids the shared type
/// that would let it be written once; a unit test asserts the two agree at every minute of the day, so
/// the duplication cannot quietly drift apart.
/// </para>
/// </remarks>
/// <param name="Start">When the quiet window opens, in the branch's local time.</param>
/// <param name="End">When it closes, in the branch's local time.</param>
public sealed record QuietWindow(TimeOnly Start, TimeOnly End)
{
    /// <summary>Whether a local time falls inside the window.</summary>
    /// <param name="local">The time in the branch's timezone.</param>
    /// <returns>True when the customer would rather not hear from the shop then.</returns>
    public bool Covers(TimeOnly local)
        => Start < End
            ? local >= Start && local < End
            : local >= Start || local < End;
}
