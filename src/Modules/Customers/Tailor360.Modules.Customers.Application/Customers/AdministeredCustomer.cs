using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Modules.Customers.Application.Customers;

/// <summary>
/// One customer record as the application layer hands it back, with the version an edit must be
/// made against.
/// </summary>
/// <remarks>
/// Not a payload. The wire shape is declared in the <c>Api</c> project and projected from this
/// (ARCH-013), because a payload is published and an application type is free to change. It carries
/// the contact fields; whether a given caller is shown them is the endpoint's decision, taken once,
/// against <c>customers.read_contact</c>.
/// </remarks>
/// <param name="CustomerId">The record.</param>
/// <param name="CustomerNumber">The display number.</param>
/// <param name="OwningBranchId">The branch that created it.</param>
/// <param name="VisibilityBranchIds">The branches that see it in ordinary search results.</param>
/// <param name="DisplayName">The name as given.</param>
/// <param name="NativeName">The Tamil-script name, where there is one.</param>
/// <param name="PhoneE164">The primary telephone number.</param>
/// <param name="AlternatePhoneE164">The second telephone number.</param>
/// <param name="Email">The email address.</param>
/// <param name="AddressLine">The street line.</param>
/// <param name="Locality">The area or town.</param>
/// <param name="Postcode">The postal code.</param>
/// <param name="Language">The language the customer is written to in.</param>
/// <param name="Status">Whether the record is in use.</param>
/// <param name="Aliases">Previous names, spellings and merged numbers.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="UpdatedAt">When it was last changed.</param>
/// <param name="Version">The concurrency token.</param>
public sealed record AdministeredCustomer(
    Guid CustomerId,
    string CustomerNumber,
    Guid OwningBranchId,
    IReadOnlyList<Guid> VisibilityBranchIds,
    string DisplayName,
    string? NativeName,
    string PhoneE164,
    string? AlternatePhoneE164,
    string? Email,
    string? AddressLine,
    string? Locality,
    string? Postcode,
    string Language,
    CustomerStatus Status,
    IReadOnlyList<CustomerAliasView> Aliases,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    EntityTag Version)
{
    /// <summary>Projects a loaded aggregate.</summary>
    /// <param name="customer">The customer.</param>
    /// <param name="version">Its concurrency token.</param>
    /// <returns>The record.</returns>
    public static AdministeredCustomer From(Customer customer, EntityTag version)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return new AdministeredCustomer(
            customer.Id,
            customer.CustomerNumber,
            customer.OwningBranchId,
            [.. customer.VisibilityBranchIds],
            customer.DisplayName,
            customer.NativeName,
            customer.PhoneE164,
            customer.AlternatePhoneE164,
            customer.Email,
            customer.AddressLine,
            customer.Locality,
            customer.Postcode,
            customer.Language,
            customer.Status,
            [.. customer.Aliases.Select(CustomerAliasView.From)],
            customer.CreatedAt,
            customer.UpdatedAt,
            version);
    }
}

/// <summary>One alias held against a customer.</summary>
/// <param name="Kind">Why it is held.</param>
/// <param name="Value">The alias as it was written.</param>
/// <param name="RecordedAt">When it was recorded.</param>
public sealed record CustomerAliasView(CustomerAliasKind Kind, string Value, DateTimeOffset RecordedAt)
{
    /// <summary>Projects an alias.</summary>
    /// <param name="alias">The alias.</param>
    /// <returns>The view.</returns>
    public static CustomerAliasView From(CustomerAlias alias)
    {
        ArgumentNullException.ThrowIfNull(alias);

        return new CustomerAliasView(alias.Kind, alias.Value, alias.RecordedAt);
    }
}
