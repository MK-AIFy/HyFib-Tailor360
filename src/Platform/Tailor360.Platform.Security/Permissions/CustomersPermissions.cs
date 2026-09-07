namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Customers and Measurements module: the customer record, consent, the
/// privacy actions, and the measurement record.
/// </summary>
/// <remarks>
/// Reading a customer is split from reading their contact details, their consent record and their
/// notes, because <c>docs/nfr/data-classification.md</c> classifies those separately and the workshop
/// roles need the first without the rest. That split is what lets a Tailor open a job card at all.
/// </remarks>
public static class CustomersPermissions
{
    /// <summary>Find a customer and read the identifying fields.</summary>
    public const string Read = "customers.read";

    /// <summary>Read a customer's phone number, email address and postal address.</summary>
    public const string ReadContact = "customers.read_contact";

    /// <summary>Read the consent record — which purposes were agreed, when and how.</summary>
    public const string ReadConsent = "customers.read_consent";

    /// <summary>Read the free-text notes held against a customer.</summary>
    public const string ReadNotes = "customers.read_notes";

    /// <summary>Create a customer record.</summary>
    public const string Create = "customers.create";

    /// <summary>Correct a customer record and record or withdraw consent.</summary>
    public const string Update = "customers.update";

    /// <summary>Deactivate a customer record, which keeps history resolvable.</summary>
    public const string Deactivate = "customers.deactivate";

    /// <summary>Merge two customer records into one.</summary>
    public const string Merge = "customers.merge";

    /// <summary>Export a customer's personal data in answer to a subject request.</summary>
    public const string Export = "customers.export";

    /// <summary>Restrict processing for a customer, which stops messages and analytics.</summary>
    public const string Restrict = "customers.restrict";

    /// <summary>Record a customer's deletion request and start the erasure workflow.</summary>
    public const string RequestDeletion = "customers.request_deletion";

    /// <summary>Capture and confirm a measurement version.</summary>
    public const string CaptureMeasurements = "measurements.capture";

    /// <summary>Read a confirmed measurement sheet. The read itself is audited.</summary>
    public const string ReadMeasurementSheet = "measurements.read_sheet";

    /// <summary>Draft and submit a measurement template version.</summary>
    public const string EditTemplates = "catalog.templates.edit";

    /// <summary>Publish or retire a measurement template version.</summary>
    public const string PublishTemplates = "catalog.templates.publish";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(Read, "Find a customer and read their identifying fields.", PermissionModules.Customers),
        new(ReadContact, "Read a customer's phone, email and address.", PermissionModules.Customers),
        new(ReadConsent, "Read a customer's consent record.", PermissionModules.Customers),
        new(ReadNotes, "Read the free-text notes held against a customer.", PermissionModules.Customers),
        new(Create, "Create a customer record.", PermissionModules.Customers),
        new(Update, "Correct a customer record and record or withdraw consent.",
            PermissionModules.Customers, RequiresReason: true),
        new(Deactivate, "Deactivate a customer record.",
            PermissionModules.Customers, RequiresReason: true),
        new(Merge, "Merge two customer records into one, which cannot be undone.",
            PermissionModules.Customers, RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(Export, "Export a customer's personal data for a subject access request.",
            PermissionModules.Customers, PermissionScope.Organisation, RequiresMfa: true, RequiresReason: true),
        new(Restrict, "Restrict processing for a customer.",
            PermissionModules.Customers, PermissionScope.Organisation, RequiresMfa: true, RequiresReason: true),
        new(RequestDeletion, "Record a deletion request and start the erasure workflow.",
            PermissionModules.Customers, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(CaptureMeasurements, "Capture and confirm a measurement version.",
            PermissionModules.Customers),
        new(ReadMeasurementSheet, "Read a confirmed measurement sheet.", PermissionModules.Customers),
        new(EditTemplates, "Draft and submit a measurement template version.",
            PermissionModules.Customers, PermissionScope.Organisation),
        new(PublishTemplates, "Publish or retire a measurement template version.",
            PermissionModules.Customers, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
    ];
}
