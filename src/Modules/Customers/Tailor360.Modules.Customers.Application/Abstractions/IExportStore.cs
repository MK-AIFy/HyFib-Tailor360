using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Application.Abstractions;

/// <summary>
/// Reads and writes generated subject-access exports, and gathers what one is made of.
/// </summary>
/// <remarks>
/// The gathering lives behind the port rather than in the handler because it is several reads across
/// the module's own tables, and the handler's job is to decide whether an export may be taken, not to
/// know how consent history is stored.
/// </remarks>
public interface IExportStore
{
    /// <summary>
    /// Gathers everything the shop holds about one person, or null when no record has that identity.
    /// </summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="organisationId">The caller's organisation. A record outside it is not found.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The subject's held data, or null.</returns>
    Task<CustomerSubjectData?> GatherAsync(
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one export for change, or null when no export has that identity for that customer in
    /// that organisation.
    /// </summary>
    /// <remarks>
    /// All three parts are required together on purpose. An export identifier alone would let a
    /// caller who guessed one read a copy of somebody else's record from a route that names a
    /// different customer.
    /// </remarks>
    /// <param name="exportId">The export.</param>
    /// <param name="customerId">The customer the route names.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The export, or null.</returns>
    Task<CustomerExport?> FindAsync(
        Guid exportId,
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the exports for one customer that still hold a copy.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The live exports, oldest first.</returns>
    Task<IReadOnlyList<CustomerExport>> LiveForCustomerAsync(
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads exports whose lifetime has run out and which still hold a copy.</summary>
    /// <param name="asAt">The instant to judge expiry against, from <c>IClock</c>.</param>
    /// <param name="limit">The most to take in one pass, so a backlog cannot make one run unbounded.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The expired exports still holding a copy.</returns>
    Task<IReadOnlyList<CustomerExport>> ExpiredHoldingDataAsync(
        DateTimeOffset asAt,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new export to the unit of work.</summary>
    /// <param name="export">The export.</param>
    void Add(CustomerExport export);

    /// <summary>Commits.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or <c>CustomersErrors.ConcurrentChange</c>.</returns>
    Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything the Customers module holds about one person, gathered for an export.
/// </summary>
/// <remarks>
/// Domain types rather than a projection, because the renderer decides what reaches the document and
/// a projection here would be a second place that decides it.
/// </remarks>
/// <param name="Customer">The record itself, with its aliases and branch visibility.</param>
/// <param name="Consent">Every consent record ever written for them, oldest first.</param>
/// <param name="Preferences">How they asked to be contacted, if anybody recorded it.</param>
public sealed record CustomerSubjectData(
    Domain.Customers.Customer Customer,
    IReadOnlyList<Domain.Consent.ConsentRecord> Consent,
    Domain.Preferences.CommunicationPreferences? Preferences);
