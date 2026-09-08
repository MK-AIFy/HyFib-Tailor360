using Tailor360.Platform.Security.FieldVisibility;

namespace Tailor360.Web.Timeline;

/// <summary>One page of a customer's merged history, on the wire.</summary>
/// <remarks>
/// <para>
/// The entry shape is the approved response view <c>customers.timeline</c> in
/// <c>docs/security/field-visibility.md</c>, and a contract test holds
/// <see cref="CustomerTimelineEntryPayload"/> equal to it — same fields, same order — so a property
/// added here without an approved row fails the build rather than reaching a response.
/// </para>
/// <para>
/// One field is gated: <see cref="CustomerTimelineEntryPayload.Reason"/> is free text a member of staff
/// typed about a named person, which is customer notes and needs <c>customers.read_notes</c>. It is
/// withheld twice over — the contributing source does not put it in the entry, and this projection
/// would drop it if it had — because the two withholdings answer different questions: the source knows
/// whether the caller may read notes, and the view is where somebody approved that they may not.
/// </para>
/// </remarks>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="NextCursor">Where the next page starts, or null at the end.</param>
/// <param name="UnavailableSources">
/// The modules that could not answer. Empty on an ordinary page; a screen shows it as "part of this
/// history could not be loaded" rather than letting a gap look like an absence.
/// </param>
public sealed record CustomerTimelinePayload(
    IReadOnlyList<CustomerTimelineEntryPayload> Entries,
    string? NextCursor,
    IReadOnlyList<string> UnavailableSources)
{
    /// <summary>
    /// The problem code for an identifier that names no customer.
    /// </summary>
    /// <remarks>
    /// The same code the Customers module answers with. It is repeated here because the module's own
    /// constant is on a <c>Domain</c> type the host may not reference (ARCH-006), and a contract test
    /// holds the two equal so a client is never handed two codes for one condition.
    /// </remarks>
    public const string CustomerNotFound = "customers.customer-not-found";

    /// <summary>Projects a composed page through the caller's mask for the approved view.</summary>
    /// <param name="page">The composed page.</param>
    /// <param name="mask">The caller's mask for <c>customers.timeline</c>.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="ArgumentException">The mask is for some other view, or it reaches no field.</exception>
    public static CustomerTimelinePayload From(CustomerTimelinePage page, FieldMask mask)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(mask);

        if (!string.Equals(mask.View.Key, CustomersResponseViews.Timeline, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A customer timeline is projected through '{CustomersResponseViews.Timeline}', not "
                + $"'{mask.View.Key}'.",
                nameof(mask));
        }

        if (mask.IsEmpty)
        {
            throw new ArgumentException(
                $"The caller reaches no field of '{CustomersResponseViews.Timeline}', so there is no "
                + "timeline to send them. The route demands the view's own permission, so reaching "
                + "here with an empty mask is a fault in the endpoint rather than an answer.",
                nameof(mask));
        }

        return new CustomerTimelinePayload(
            [.. page.Entries.Select(entry => CustomerTimelineEntryPayload.From(entry, mask))],
            page.NextCursor,
            page.UnavailableSources);
    }
}

/// <summary>One thing that happened to a customer.</summary>
/// <param name="EntryId">
/// The entry. Unique within its source, and half of the position the cursor resumes from.
/// </param>
/// <param name="OccurredAt">When it happened, by the server's clock, in UTC.</param>
/// <param name="Source">The module that contributed it, for example <c>customers</c>.</param>
/// <param name="Kind">
/// The stable dotted kind, for example <c>customers.consent.recorded</c>, which a screen turns into an
/// icon and a label of its own.
/// </param>
/// <param name="Title">What happened, in the shop's words.</param>
/// <param name="Detail">The longer description the recording module wrote.</param>
/// <param name="Reason">
/// The reason the actor gave, or null when there was none — or when this caller may not read it.
/// <paramref name="ReasonPermission"/> is what tells those two apart.
/// </param>
/// <param name="ReasonPermission">
/// The permission that shows <paramref name="Reason"/>, set whenever a reason was given. Null means no
/// reason was given; a value with a null reason means one was given and withheld.
/// </param>
/// <param name="ReferenceType">The kind of thing the entry links to, or null.</param>
/// <param name="ReferenceId">What it links to, or null.</param>
/// <param name="ExpandPermission">
/// What a caller must hold to open the reference, or null when there is nothing to open.
/// </param>
/// <param name="BranchId">The branch the entry belongs to, where it belongs to one.</param>
/// <param name="ActorDisplayName">
/// Who did it, as their name was at the time, or null for the system. A member of staff, never the
/// customer.
/// </param>
public sealed record CustomerTimelineEntryPayload(
    Guid EntryId,
    DateTimeOffset OccurredAt,
    string Source,
    string Kind,
    string Title,
    string? Detail,
    string? Reason,
    string? ReasonPermission,
    string? ReferenceType,
    Guid? ReferenceId,
    string? ExpandPermission,
    Guid? BranchId,
    string? ActorDisplayName)
{
    /// <summary>Projects one entry through the caller's mask.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="mask">The caller's mask for <c>customers.timeline</c>.</param>
    /// <returns>The payload.</returns>
    public static CustomerTimelineEntryPayload From(
        Tailor360.Platform.Abstractions.Ports.TimelineEntry entry,
        FieldMask mask)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(mask);

        return new CustomerTimelineEntryPayload(
            entry.Id,
            entry.OccurredAt,
            entry.Source,
            entry.Kind,
            entry.Title,
            Text("detail", entry.Detail),
            Text("reason", entry.Reason),
            Text("reasonPermission", entry.ReasonPermission),
            Text("referenceType", entry.ReferenceType),
            mask.Allows("referenceId") ? entry.ReferenceId : null,
            Text("expandPermission", entry.ExpandPermission),
            mask.Allows("branchId") ? entry.BranchId : null,
            Text("actorDisplayName", entry.ActorDisplayName));

        string? Text(string field, string? value) => mask.Allows(field) ? value : null;
    }
}
