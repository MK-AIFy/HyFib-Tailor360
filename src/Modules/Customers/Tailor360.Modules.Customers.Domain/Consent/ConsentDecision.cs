namespace Tailor360.Modules.Customers.Domain.Consent;

/// <summary>
/// What a customer said about one purpose.
/// </summary>
/// <remarks>
/// <para>
/// There is no "not asked" member, and that is the point of the type. Absence of a record is how the
/// system says nobody has asked; a <see cref="Declined"/> record is how it says somebody asked and the
/// answer was no. Storing a decline as an absence would lose the difference, and the counter would ask
/// the same customer again every visit — which the walkthrough of a customer who declined marketing
/// messages (<c>docs/prd/walkthroughs.md</c>) states explicitly: the decline is stored as a consent
/// record, not as an absence.
/// </para>
/// <para>
/// <see cref="Withdrawn"/> is separate from <see cref="Declined"/> for the same reason: one is an
/// answer given at the start, the other is an answer changed later, and the evidence that a customer
/// withdrew is itself needed (<c>docs/nfr/data-classification.md</c> section 4.2).
/// </para>
/// </remarks>
public enum ConsentDecision
{
    /// <summary>The customer agreed to this purpose against the wording version recorded with it.</summary>
    Granted,

    /// <summary>The customer was asked and said no.</summary>
    Declined,

    /// <summary>The customer had agreed and has since withdrawn. Honoured immediately.</summary>
    Withdrawn,
}
