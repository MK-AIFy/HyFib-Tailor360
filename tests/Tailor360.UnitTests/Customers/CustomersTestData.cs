using System.Security.Cryptography;
using System.Text;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The values the Customers unit tests are built from.
/// </summary>
/// <remarks>
/// Every name, number and address here is synthetic and belongs to nobody. The identifiers are
/// derived from a name rather than generated, so a failure names the same value twice and a test that
/// wants "the same customer" gets it without threading a variable through.
/// </remarks>
internal static class CustomersTestData
{
    /// <summary>The instant every test runs at.</summary>
    public static readonly DateTimeOffset Now = new(2026, 5, 7, 11, 5, 0, TimeSpan.FromHours(5.5));

    /// <summary>The organisation the tests run in.</summary>
    public static readonly Guid Organisation = Id("organisation");

    /// <summary>The branch the tests create records at.</summary>
    public static readonly Guid Branch = Id("branch-cbe01");

    /// <summary>A second branch, for the visibility tests.</summary>
    public static readonly Guid OtherBranch = Id("branch-mdu01");

    /// <summary>The member of staff the tests act as.</summary>
    public static readonly Guid Actor = Id("reception");

    /// <summary>A deterministic identifier for a name, so a failure names the same value twice.</summary>
    /// <param name="name">Any name.</param>
    /// <returns>The identifier.</returns>
    public static Guid Id(string name)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16));

    /// <summary>Validated details for a customer, with everything overridable.</summary>
    /// <param name="displayName">The name.</param>
    /// <param name="phone">The primary number.</param>
    /// <param name="alternatePhone">The second number.</param>
    /// <param name="nativeName">The Tamil-script name.</param>
    /// <param name="locality">The area or town.</param>
    /// <param name="postcode">The postal code.</param>
    /// <param name="language">The language.</param>
    /// <returns>The details.</returns>
    public static CustomerDetails Details(
        string displayName = "Kavitha Raman",
        string phone = "98430 21174",
        string? alternatePhone = null,
        string? nativeName = null,
        string? locality = "RS Puram",
        string? postcode = "641002",
        string? language = null)
        => CustomerDetails.Create(
            displayName,
            nativeName,
            phone,
            alternatePhone,
            email: null,
            addressLine: null,
            locality,
            postcode,
            language).Value;

    /// <summary>A registered, active customer.</summary>
    /// <param name="displayName">The name.</param>
    /// <param name="phone">The primary number.</param>
    /// <returns>The customer.</returns>
    public static Customer Registered(string displayName = "Kavitha Raman", string phone = "98430 21174")
        => Customer.Register(
            Id(displayName),
            Organisation,
            "C-CBE01-004182",
            Branch,
            Details(displayName, phone),
            Now,
            Actor).Value;

    /// <summary>The comparable facts of a customer, for duplicate scoring.</summary>
    /// <param name="customer">The customer.</param>
    /// <returns>The subject.</returns>
    public static DuplicateSubject Subject(Customer customer)
        => new(
            customer.NormalisedName,
            customer.NativeName,
            customer.PhoneE164,
            customer.AlternatePhoneE164,
            customer.Locality,
            customer.Postcode);
}
