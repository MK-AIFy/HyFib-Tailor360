using System.Collections.Frozen;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Application.Preferences;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Customers.Application.Customers;

/// <summary>
/// Which of this module's audit actions reach a customer's timeline, what each is called there, and
/// what a caller must hold to be shown it.
/// </summary>
/// <remarks>
/// <para>
/// The list is closed on purpose. An audit action not named here does not appear on the timeline, so
/// adding an audited action to this module does not silently publish it to every screen that can open a
/// customer — somebody has to decide it belongs there, give it a title in the shop's words, and say
/// what seeing it costs. The reverse is checked by test: every action this module writes is either in
/// this map or is listed as deliberately absent.
/// </para>
/// <para>
/// <strong>The permissions are not decoration.</strong> Reading a customer is split from reading their
/// consent record and from exporting their data, and the timeline is exactly the surface where that
/// split would quietly stop applying: an entry saying "consent for marketing withdrawn" tells a reader
/// the thing <c>customers.read_consent</c> exists to gate, whether or not they can open the consent
/// screen. So the entry is withheld, not merely its detail.
/// </para>
/// </remarks>
public static class TimelineActions
{
    /// <summary>
    /// The audit entity type every one of this module's entries is written against.
    /// </summary>
    /// <remarks>
    /// One type for the record, its consent, its preferences and its exports, which is what makes a
    /// customer's whole history one query. The audit helpers that write it are internal to this
    /// project, so the constant is repeated here rather than reached for — and a test holds the two
    /// equal, because a mistyped entity type would produce an empty timeline rather than an error.
    /// </remarks>
    public const string EntityType = "customers.customer";

    /// <summary>
    /// What a caller must hold to be shown the reason an actor gave.
    /// </summary>
    /// <remarks>
    /// A reason is free text a member of staff typed about a named person — "confirmed with the
    /// customer at the counter", "she asked us to close the record" — which
    /// <c>docs/nfr/data-classification.md</c> classifies as customer notes rather than as operational
    /// data. <c>customers.read_notes</c> is the permission that class already has, granted to the Owner
    /// and the Branch Manager, and this is the first thing in the application to gate on it.
    /// </remarks>
    public const string ReasonPermission = CustomersPermissions.ReadNotes;

    private static readonly FrozenDictionary<string, TimelineAction> Actions =
        new Dictionary<string, TimelineAction>(StringComparer.Ordinal)
        {
            [CustomerHandler.RegisteredAction] =
                new("Customer registered", null),
            [CustomerHandler.CorrectedAction] =
                new("Record corrected", null),
            [CustomerHandler.DeactivatedAction] =
                new("Record withdrawn from use", null),
            [CustomerHandler.ReactivatedAction] =
                new("Record returned to use", null),
            [CustomerHandler.OpenedAtBranchAction] =
                new("A second branch began serving this customer", null),
            [CustomerHandler.MergedAction] =
                new("Another record was folded into this one", null),
            [CustomerHandler.MergedAwayAction] =
                new("This record was folded into another", null),

            // Consent and communication preferences are one inventory row in
            // data-classification.md section 5.3 and one permission, so they gate together.
            [ConsentHandler.RecordedAction] =
                new("Consent recorded", CustomersPermissions.ReadConsent),
            [ConsentHandler.WithdrawnAction] =
                new("Consent withdrawn", CustomersPermissions.ReadConsent),
            [PreferenceHandler.ChangedAction] =
                new("How to contact this customer changed", CustomersPermissions.ReadConsent),

            // Measurements taken is exactly what a customer timeline is for, and it is the only one of the
            // three capture actions that is evidence — a draft is work in progress that may expire and be
            // deleted. Gated on the capture permission because a measurement is sensitive personal data under
            // data-classification.md section 5.3: somebody who may not take measurements should not be told
            // from a timeline that they exist.
            [MeasurementCaptureHandler.ConfirmedAction] =
                new("Measurements taken", CustomersPermissions.CaptureMeasurements),

            // A subject-access export is the record of who has seen this person's whole file. Whoever
            // may take one may see that one was taken; nobody else needs to.
            [CustomerExportHandler.GeneratedAction] =
                new("Subject-access export taken", CustomersPermissions.Export),
            [CustomerExportHandler.DownloadedAction] =
                new("Subject-access export downloaded", CustomersPermissions.Export),
            [CustomerExportHandler.PurgedAction] =
                new("Subject-access export destroyed", CustomersPermissions.Export),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every action this module puts on the timeline.</summary>
    public static IReadOnlyCollection<string> All => Actions.Keys;

    /// <summary>The actions a caller holding these permissions may be shown.</summary>
    /// <param name="permissions">The caller's effective permissions.</param>
    /// <returns>The action names, as a set to test membership against.</returns>
    public static IReadOnlySet<string> VisibleTo(IReadOnlySet<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return Actions
            .Where(pair => pair.Value.RequiredPermission is null
                           || permissions.Contains(pair.Value.RequiredPermission))
            .Select(pair => pair.Key)
            .ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>The title an action is shown under, or the action itself when it has none.</summary>
    /// <param name="action">The audit action.</param>
    /// <returns>The title.</returns>
    public static string TitleOf(string action)
        => Actions.TryGetValue(action, out var known) ? known.Title : action;

    /// <summary>What a caller must hold to be shown this action, or null when the view's own is enough.</summary>
    /// <param name="action">The audit action.</param>
    /// <returns>The permission, or null.</returns>
    public static string? PermissionFor(string action)
        => Actions.TryGetValue(action, out var known) ? known.RequiredPermission : null;

    private sealed record TimelineAction(string Title, string? RequiredPermission);
}
