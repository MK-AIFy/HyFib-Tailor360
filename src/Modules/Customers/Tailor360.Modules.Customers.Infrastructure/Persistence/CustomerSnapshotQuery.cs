using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Contracts.Customers;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// The published customer snapshot, over the <c>customers</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The mask is applied in the database, not after the read.</strong> Whether the caller holds
/// <c>customers.read_contact</c> is decided once and passed into the projection, so for a caller who
/// does not hold it PostgreSQL returns nulls and the telephone number, email address and postal
/// address never travel to the application at all. Masking after loading would work equally well right
/// up until somebody logs the loaded row.
/// </para>
/// <para>
/// Projected rather than loaded for the reason the branch directory is: a module that wants a customer
/// number and a name to print on an invoice must not pull the aliases, the visibility rows and the
/// whole aggregate to get them.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class CustomerSnapshotQuery(CustomersDbContext context) : ICustomerSnapshotQuery
{
    /// <inheritdoc />
    public async Task<CustomerSnapshot?> GetAsync(
        Guid customerId,
        IReadOnlyCollection<string> callerPermissions,
        CancellationToken cancellationToken = default)
    {
        var mayReadContact = CustomerSnapshot.MayReadContact(callerPermissions);

        return await context.Customers
            .AsNoTracking()
            .Where(customer => customer.Id == customerId)
            .Select(customer => new CustomerSnapshot(
                customer.Id,
                customer.CustomerNumber,
                customer.DisplayName,
                customer.NativeName,
                customer.Language,
                customer.OwningBranchId,
                mayReadContact,
                mayReadContact ? customer.PhoneE164 : null,
                mayReadContact ? customer.AlternatePhoneE164 : null,
                mayReadContact ? customer.Email : null,
                mayReadContact ? customer.AddressLine : null,
                mayReadContact ? customer.Locality : null,
                mayReadContact ? customer.Postcode : null,
                // Never masked. Whether the record a consumer is holding still stands is not personal
                // data about the person; it is a fact about the record, and a consumer that cannot see
                // it re-points nothing and shows a customer who no longer exists.
                customer.MergedIntoCustomerId))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
