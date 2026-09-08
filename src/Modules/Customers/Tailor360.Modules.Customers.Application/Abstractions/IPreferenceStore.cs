using Tailor360.Modules.Customers.Domain.Preferences;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Application.Abstractions;

/// <summary>
/// Reads and writes a customer's communication preference.
/// </summary>
/// <remarks>
/// Separate from <see cref="IConsentStore"/> because a preference is a different kind of thing: a
/// current instruction that is edited in place, not evidence that is appended to. They are classified
/// together (<c>docs/nfr/data-classification.md</c> section 5.3) and they are checked together before
/// a send, and neither of those makes them one aggregate.
/// </remarks>
public interface IPreferenceStore
{
    /// <summary>
    /// One customer's preference for change, or null when nobody has recorded one.
    /// </summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The preference, or null.</returns>
    Task<CommunicationPreferences?> FindAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a first preference to the unit of work.</summary>
    /// <param name="preferences">The preference.</param>
    void Add(CommunicationPreferences preferences);

    /// <summary>
    /// Commits, turning a lost optimistic-concurrency race into a failure rather than an exception.
    /// </summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or <c>CustomersErrors.ConcurrentChange</c>.</returns>
    Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>The concurrency token an edit must be made against.</summary>
    /// <param name="preferences">A tracked preference.</param>
    /// <returns>The entity tag.</returns>
    EntityTag EntityTagOf(CommunicationPreferences preferences);
}
