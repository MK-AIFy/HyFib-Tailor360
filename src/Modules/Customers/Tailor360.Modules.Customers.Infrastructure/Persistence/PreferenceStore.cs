using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Preferences;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// A customer's communication preference, over the <c>customers</c> schema.
/// </summary>
/// <remarks>
/// Tracked rather than read no-tracking, because the caller changes what it reads. The row carries
/// <c>xmin</c> as its concurrency token, so two counters saving at once make one of them re-read
/// instead of overwriting the other silently.
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class PreferenceStore(CustomersDbContext context) : IPreferenceStore
{
    /// <inheritdoc />
    public async Task<CommunicationPreferences?> FindAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await context.CommunicationPreferences
            .FirstOrDefaultAsync(entry => entry.CustomerId == customerId, cancellationToken);

    /// <inheritdoc />
    public void Add(CommunicationPreferences preferences)
        => context.CommunicationPreferences.Add(preferences);

    /// <inheritdoc />
    public async Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody else changed the preference between it being read and being written. The screen
            // can say so and re-read; an exception here would become a 500 and say nothing.
            return Result.Failure(CustomersErrors.ConcurrentChange);
        }
    }

    /// <inheritdoc />
    public EntityTag EntityTagOf(CommunicationPreferences preferences)
        => context.EntityTagOf(preferences);
}
