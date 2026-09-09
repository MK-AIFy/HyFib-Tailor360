using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// The Customers views: the record and the search card the counter reads, and the measurement sheet
/// the workshop reads.
/// </summary>
/// <remarks>
/// <para>
/// The measurement sheet is Sensitive Personal on paper as much as on screen. Its own permission,
/// <c>measurements.read_sheet</c>, is the gate on the figures themselves — Delivery Staff, Cashier and
/// Inventory Clerk hold no reason to read them and are not granted it — and reading it is audited
/// explicitly rather than left to the ordinary request log
/// (<c>docs/nfr/data-classification.md</c> section 5.4).
/// </para>
/// <para>
/// The other two are <see cref="ViewSurface.Counter"/> surfaces and carry contact details, which no
/// workshop surface may. That is not a relaxation of the workshop rule: it is the rule applied to the
/// screen it was written about. Ringing a customer to say her blouse is ready is what the counter is
/// for, and <c>customers.read_contact</c> is the field-by-field gate on doing it.
/// </para>
/// </remarks>
public static class CustomersResponseViews
{
    /// <summary>The printable measurement sheet for one garment job.</summary>
    public const string MeasurementSheet = "customers.measurement_sheet";

    /// <summary>One customer record, as the counter opens it.</summary>
    public const string Record = "customers.record";

    /// <summary>One customer as a search result, which is also how a duplicate is spotted.</summary>
    public const string SearchCard = "customers.search_card";

    /// <summary>One customer's merged history, composed by the host from every module that holds part of it.</summary>
    public const string Timeline = "customers.timeline";

    /// <summary>
    /// The classes a counter surface may never carry, whoever is reading it.
    /// </summary>
    /// <remarks>
    /// It is <see cref="SurfaceRules.ForbiddenOnACounterSurface"/> and not a list of its own, for the
    /// same reason the workshop set is not one: measurements have their own view and their own
    /// permission, and a price, a payment state and a member of staff's throughput are three other
    /// modules' business. <see cref="ResponseView"/> refuses a counter view that withholds less.
    /// </remarks>
    public const FieldClassification WithheldFromACustomerScreen = SurfaceRules.ForbiddenOnACounterSurface;

    /// <summary>The Customers views.</summary>
    public static IReadOnlyCollection<ResponseView> All { get; } =
    [
        new ResponseView(
            MeasurementSheet,
            PermissionModules.Customers,
            ViewSurface.Workshop,
            "The figures the garment is cut to, rendered for the workshop and for print.",
            CustomersPermissions.ReadMeasurementSheet,
            OrdersResponseViews.WithheldFromTheWorkshop,
            [
                new ViewField("jobNumber", FieldClassification.Operational, null,
                    "Which job the sheet belongs to, so a loose sheet is never anonymous."),
                new ViewField("templateName", FieldClassification.Operational, null,
                    "The measurement template the figures were captured against."),
                new ViewField("templateVersion", FieldClassification.Operational, null,
                    "The published version pinned at confirmation, so a sheet reprinted a year later "
                    + "renders exactly as it did."),
                new ViewField("capturedAt", FieldClassification.Operational, null,
                    "When the figures were taken, which is how a stale set is spotted."),
                new ViewField("capturedBy", FieldClassification.Operational, null,
                    "Who took them, so a question about a figure has somebody to ask."),
                new ViewField("customerName", FieldClassification.CustomerIdentity, null,
                    "Whose figures these are. Nothing else about the customer is on the sheet."),
                new ViewField("values", FieldClassification.Measurement, null,
                    "The measured figures, stored in millimetres and rendered in the branch's display "
                    + "unit. The view's own permission is the gate; there is no sheet without them."),
                new ViewField("easeNotes", FieldClassification.Measurement, null,
                    "Ease and growth-allowance notes, which are read with the figures and classified "
                    + "with them."),
                new ViewField("fieldDiagrams", FieldClassification.Media, MediaPermissions.Read,
                    "The template's line drawings, streamed by the authorising endpoint like all media."),
            ]),

        new ResponseView(
            Record,
            PermissionModules.Customers,
            ViewSurface.Counter,
            "One customer as the counter opens them: who they are, how to reach them, and whether the "
            + "record still stands.",
            CustomersPermissions.Read,
            WithheldFromACustomerScreen,
            [
                new ViewField("customerId", FieldClassification.Operational, null,
                    "The record's identifier, which every other call about this person is made with."),
                new ViewField("customerNumber", FieldClassification.CustomerIdentity, null,
                    "The number written on the card the customer carries."),
                new ViewField("displayName", FieldClassification.CustomerIdentity, null,
                    "The name as the customer gave it, which is the whole point of opening the record."),
                new ViewField("nativeName", FieldClassification.CustomerIdentity, null,
                    "The Tamil-script name, where there is one, so the counter can read it back as "
                    + "written."),
                new ViewField("phone", FieldClassification.CustomerContact, CustomersPermissions.ReadContact,
                    "The primary telephone number. Contact is split from identity because "
                    + "docs/nfr/data-classification.md section 5.2 classifies it separately."),
                new ViewField("alternatePhone", FieldClassification.CustomerContact, CustomersPermissions.ReadContact,
                    "The second number, tried when the first does not answer."),
                new ViewField("email", FieldClassification.CustomerContact, CustomersPermissions.ReadContact,
                    "The email address, under the same permission as the telephone numbers."),
                new ViewField("addressLine", FieldClassification.CustomerContact, CustomersPermissions.ReadContact,
                    "The street line, needed to deliver and for nothing else."),
                new ViewField("locality", FieldClassification.CustomerContact, CustomersPermissions.ReadContact,
                    "The area or town, under the same permission."),
                new ViewField("postcode", FieldClassification.CustomerContact, CustomersPermissions.ReadContact,
                    "The postal code, under the same permission."),
                new ViewField("contactIncluded", FieldClassification.Operational, null,
                    "Whether the contact fields above were included for this caller, so that a client "
                    + "can tell a withheld number from a customer who never gave one."),
                new ViewField("language", FieldClassification.Operational, null,
                    "The language the customer is written to in, which decides what a message looks "
                    + "like rather than saying anything about the person."),
                new ViewField("status", FieldClassification.Operational, null,
                    "Whether the record is in use, so a deactivated one is not offered actions."),
                new ViewField("owningBranchId", FieldClassification.Operational, null,
                    "The branch that created the record."),
                new ViewField("visibilityBranchIds", FieldClassification.Operational, null,
                    "The branches that see the record in ordinary search results "
                    + "(docs/prd/workflows/branch-scenarios.md section 3.2)."),
                new ViewField("aliases", FieldClassification.CustomerIdentity, null,
                    "Previous names, spellings and merged customer numbers — identity under another "
                    + "writing, and never a telephone number or an address."),
                new ViewField("createdAt", FieldClassification.Operational, null,
                    "When the record was created."),
                new ViewField("updatedAt", FieldClassification.Operational, null,
                    "When it was last changed."),
                new ViewField("version", FieldClassification.Operational, null,
                    "The concurrency token an edit must be made against."),
                new ViewField("mergedIntoCustomerId", FieldClassification.Operational, null,
                    "The record this one was folded into, or null while it stands on its own. Never "
                    + "gated: whether the record still stands is a fact about the record rather than "
                    + "about the person, and a screen that cannot see it offers actions against a "
                    + "customer who no longer exists."),
                new ViewField("mergedAt", FieldClassification.Operational, null,
                    "When it was folded in, or null while it stands on its own."),
            ]),

        new ResponseView(
            SearchCard,
            PermissionModules.Customers,
            ViewSurface.Counter,
            "One customer as a search result: enough to tell two people apart before a second record "
            + "is created for one of them.",
            CustomersPermissions.Read,
            WithheldFromACustomerScreen | FieldClassification.CustomerNotes,
            [
                new ViewField("customerId", FieldClassification.Operational, null,
                    "The record the card leads to."),
                new ViewField("customerNumber", FieldClassification.CustomerIdentity, null,
                    "The number, which is what an old bill or a card in a purse carries."),
                new ViewField("displayName", FieldClassification.CustomerIdentity, null,
                    "The name as given, which is what the counter searched for."),
                new ViewField("nativeName", FieldClassification.CustomerIdentity, null,
                    "The Tamil-script name, where there is one."),
                new ViewField("maskedPhone", FieldClassification.CustomerContact, null,
                    "The number with everything but its last four digits replaced. It carries no "
                    + "permission because it is masked for everybody, whatever they hold: enough to "
                    + "confirm a number a customer is reading out, and not enough to be a contact "
                    + "list, which is what makes a search that reaches across branches safe "
                    + "(docs/prd/workflows/branch-scenarios.md section 3.1)."),
                new ViewField("owningBranchId", FieldClassification.Operational, null,
                    "The branch that created the record."),
                new ViewField("visibleToCaller", FieldClassification.Operational, null,
                    "False when the record is outside the caller's branches, which is what turns the "
                    + "card into a disambiguation card rather than a result."),
                new ViewField("status", FieldClassification.Operational, null,
                    "Whether the record is in use."),
                new ViewField("lastSeenAt", FieldClassification.Operational, null,
                    "When the record was last changed, which is what the list is ordered by."),
            ]),

        new ResponseView(
            Timeline,
            PermissionModules.Customers,
            ViewSurface.Counter,
            "What has happened to one customer, merged from every module that holds part of it.",
            CustomersPermissions.Read,
            WithheldFromACustomerScreen,
            [
                new ViewField("entryId", FieldClassification.Operational, null,
                    "The entry, which is half of the position the next page resumes from."),
                new ViewField("occurredAt", FieldClassification.Operational, null,
                    "When it happened, by the server's clock — the client's clock is evidence and is "
                    + "never what a history is ordered by (docs/architecture/conventions.md 2.4)."),
                new ViewField("source", FieldClassification.Operational, null,
                    "Which module contributed it, so a screen can say where a fact came from and a "
                    + "missing source can be named."),
                new ViewField("kind", FieldClassification.Operational, null,
                    "The stable dotted kind, which is the module's own audit action and is what a "
                    + "screen turns into an icon and a label."),
                new ViewField("title", FieldClassification.Operational, null,
                    "What happened, in the shop's words."),
                new ViewField("detail", FieldClassification.Operational, null,
                    "The longer description the recording module wrote. Operational prose about the "
                    + "record, never the customer's own data and never anything typed freehand."),
                new ViewField("reason", FieldClassification.CustomerNotes, CustomersPermissions.ReadNotes,
                    "The reason the actor gave. Free text a member of staff typed about a named "
                    + "person, which data-classification.md classifies as customer notes — so it is "
                    + "gated separately from the entry that carries it, and this is the first field "
                    + "in the application to gate on customers.read_notes."),
                new ViewField("reasonPermission", FieldClassification.Operational, null,
                    "What would have shown the reason, set whenever one was given. It is what lets a "
                    + "screen distinguish 'no reason was given' from 'a reason was given that you may "
                    + "not read', and it names a permission rather than repeating any of the text."),
                new ViewField("referenceType", FieldClassification.Operational, null,
                    "The kind of thing the entry links to, where it links to one."),
                new ViewField("referenceId", FieldClassification.Operational, null,
                    "What it links to. A UUIDv7, like every identifier that crosses the wire."),
                new ViewField("expandPermission", FieldClassification.Operational, null,
                    "What a caller must hold to open the reference. The entry is on the timeline "
                    + "either way: what it links to is a different question from whether it happened."),
                new ViewField("branchId", FieldClassification.Operational, null,
                    "The branch the entry belongs to, where it belongs to one."),
                new ViewField("actorDisplayName", FieldClassification.Operational, null,
                    "Who did it, as their name was at the time. A member of staff's name is not the "
                    + "customer's data and is not a measure of that member of staff, which is why it "
                    + "is operational and not StaffPerformance."),
            ]),
    ];
}
