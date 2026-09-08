namespace Tailor360.Modules.Customers.Domain.Customers;

/// <summary>Whether a customer record is in use.</summary>
/// <remarks>
/// There are two states and no third, because there is no delete. Orders, invoices, payments and
/// custody events name a customer, and a record removed from under them would make years of business
/// history unreadable — so a customer who has moved away, asked to be left alone, or turned out to be
/// a duplicate is deactivated. Erasure, when the retention policy finally allows it, is
/// pseudonymisation of the record rather than removal of the row, and it belongs to issue #57
/// (<c>docs/nfr/data-classification.md</c> section 5.2).
/// </remarks>
public enum CustomerStatus
{
    /// <summary>In use. The record is offered by search and may be attached to new work.</summary>
    Active = 0,

    /// <summary>
    /// Withdrawn from ordinary use. Still readable, still named by every order that referenced it,
    /// and still findable by the roles that maintain records; not offered when somebody is starting
    /// something new.
    /// </summary>
    Deactivated = 1,
}
