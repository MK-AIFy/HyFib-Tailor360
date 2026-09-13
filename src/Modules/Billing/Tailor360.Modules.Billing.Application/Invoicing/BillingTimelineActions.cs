using System.Collections.Frozen;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>
/// Which of this module's invoice facts reach a customer's timeline, what each is called there, and
/// what a caller must hold to be shown it.
/// </summary>
/// <remarks>
/// <para>
/// The list is closed on purpose, in the shape <c>Customers.Application.Customers.TimelineActions</c>
/// established first: an action not named here does not appear on any customer's timeline, so a new
/// invoice action does not silently publish itself — somebody has to give it a title in the shop's words
/// and say what seeing it costs. A unit test holds the reverse: every action <see cref="InvoiceHandler"/>
/// declares is either mapped here or recorded as deliberately absent.
/// </para>
/// <para>
/// <strong>A draft and a discarded draft are deliberately absent.</strong> Nothing about a draft is
/// authoritative (<c>InvoiceStatus.Draft</c>'s own remark), and putting one on a customer's history would
/// show her a document that may never exist — the reversible reading of open decision <b>OD-22</b>: adding
/// an entry later is additive, whereas a customer shown a document that never existed cannot be un-shown.
/// </para>
/// <para>
/// This map covers the documents of #42 only. Payments, receipts, refunds and cashier sessions are #43's
/// facts and are deliberately not here, so that whichever slice adds them extends this map rather than
/// rewriting it.
/// </para>
/// </remarks>
public static class BillingTimelineActions
{
    /// <summary>The name this module's entries are attributed to.</summary>
    public const string SourceName = "billing";

    /// <summary>
    /// What a caller must hold to be shown the reason an actor gave for a cancellation or a note.
    /// </summary>
    /// <remarks>
    /// A reason is free text a member of staff typed about a named person, which
    /// <c>docs/nfr/data-classification.md</c> classifies as customer notes rather than as operational
    /// data — the same judgement <c>Customers.Application.Customers.TimelineActions.ReasonPermission</c>
    /// already made, reused deliberately so the application has one answer to "who may read a reason
    /// somebody typed about a customer" rather than two.
    /// </remarks>
    public const string ReasonPermission = CustomersPermissions.ReadNotes;

    private static readonly FrozenDictionary<string, TimelineAction> Actions =
        new Dictionary<string, TimelineAction>(StringComparer.Ordinal)
        {
            [InvoiceHandler.PostedAction] = new("Invoice posted"),
            [InvoiceHandler.CancelledAction] = new("Invoice cancelled"),
            [InvoiceHandler.CreditNotePostedAction] = new("Credit note posted"),
            [InvoiceHandler.DebitNotePostedAction] = new("Debit note posted"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every invoice action this module puts on a customer's timeline.</summary>
    public static IReadOnlyCollection<string> All => Actions.Keys;

    /// <summary>
    /// The actions a caller holding these permissions may be shown. Every mapped action costs the same
    /// key, <see cref="BillingPermissions.CreateInvoice"/> — the key that already gates reading an
    /// invoice — so a caller either sees every one of this module's entries or none of them.
    /// </summary>
    /// <param name="permissions">The caller's effective permissions.</param>
    /// <returns>The action names, as a set to test membership against.</returns>
    public static IReadOnlySet<string> VisibleTo(IReadOnlySet<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return permissions.Contains(BillingPermissions.CreateInvoice)
            ? Actions.Keys.ToFrozenSet(StringComparer.Ordinal)
            : FrozenSet<string>.Empty;
    }

    /// <summary>The title an action is shown under, or the action itself when it has none.</summary>
    /// <param name="action">The invoice action.</param>
    /// <returns>The title.</returns>
    public static string TitleOf(string action)
        => Actions.TryGetValue(action, out var known) ? known.Title : action;

    private sealed record TimelineAction(string Title);
}
