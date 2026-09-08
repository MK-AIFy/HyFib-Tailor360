namespace Tailor360.Modules.Customers.Domain.Customers;

/// <summary>Why an alias is held against a customer.</summary>
/// <remarks>
/// An alias exists so that a search for what somebody used to be called still finds them. The kind is
/// stored because the three cases are answered differently on a screen: a previous name is a fact
/// about the person, a spelling is a fact about how the shop wrote it down, and a merged number is a
/// fact about the record.
/// </remarks>
public enum CustomerAliasKind
{
    /// <summary>A name the customer used before — most often after a marriage.</summary>
    PreviousName = 0,

    /// <summary>Another spelling of the same name, recorded because somebody searched for it.</summary>
    Spelling = 1,

    /// <summary>
    /// The customer number of a record that was merged into this one. Kept searchable so that a card
    /// or an old invoice carrying the merged number still leads somewhere (issue #26).
    /// </summary>
    MergedCustomerNumber = 2,
}
