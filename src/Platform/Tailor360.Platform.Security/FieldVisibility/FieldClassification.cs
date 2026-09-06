namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>
/// What kind of thing a response field is, drawn from the handling classes of
/// <c>docs/nfr/data-classification.md</c>.
/// </summary>
/// <remarks>
/// The classification is not decoration. A view declares the classes it must never carry, and a field
/// of a withheld class cannot be added to it — the catalogue refuses to build. That is the difference
/// between "the job card happens not to have a price on it today" and "the job card cannot have a price
/// on it", and only the second survives the pull request that adds a field in a hurry.
/// </remarks>
[Flags]
public enum FieldClassification
{
    /// <summary>Nothing withheld.</summary>
    None = 0,

    /// <summary>Identifiers, codes, statuses, dates and workflow state. Not personal data.</summary>
    Operational = 1 << 0,

    /// <summary>The customer's name and customer number: enough to know whose garment this is.</summary>
    CustomerIdentity = 1 << 1,

    /// <summary>Phone, email and address. Personal, and separately permissioned throughout.</summary>
    CustomerContact = 1 << 2,

    /// <summary>Free text staff have written about a person.</summary>
    CustomerNotes = 1 << 3,

    /// <summary>Body measurements and the notes taken with them. Sensitive personal data.</summary>
    Measurement = 1 << 4,

    /// <summary>Images, which are streamed and audited rather than linked.</summary>
    Media = 1 << 5,

    /// <summary>Amounts, discounts, tax and anything else derived from a price list.</summary>
    Pricing = 1 << 6,

    /// <summary>What has been invoiced, paid, allocated or is outstanding.</summary>
    PaymentState = 1 << 7,

    /// <summary>Throughput, rework rates and anything else that measures a named member of staff.</summary>
    StaffPerformance = 1 << 8,
}
