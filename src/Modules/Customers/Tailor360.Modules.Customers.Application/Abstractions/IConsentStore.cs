using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Application.Abstractions;

/// <summary>
/// Reads the consent register and appends to the consent record.
/// </summary>
/// <remarks>
/// <para>
/// The module's internal port, so it may speak in domain types — unlike a published contract, which
/// may not. <c>IConsentQuery</c> is the published counterpart and answers one question for another
/// module; this one serves the counter screen, which needs the whole register and the whole history.
/// </para>
/// <para>
/// <strong>There is no update and no delete, and there never will be.</strong> The table is
/// append-only and its trigger enforces that: withdrawing appends a record, and so does agreeing
/// again. A port that offered a mutation would be offering something the database refuses.
/// </para>
/// </remarks>
public interface IConsentStore
{
    /// <summary>The organisation's whole consent register, retired purposes included.</summary>
    /// <remarks>
    /// Retired purposes come back because a customer's answer to one still stands and still has to be
    /// readable. Whether a screen offers to ask about one is the screen's decision, made from
    /// <see cref="ConsentPurpose.IsRetired"/>.
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The purposes, by key.</returns>
    Task<IReadOnlyList<ConsentPurpose>> ReadRegisterAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>One purpose from the register, or null when no purpose carries that key.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="purposeKey">The key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The purpose, or null.</returns>
    Task<ConsentPurpose?> FindPurposeAsync(
        Guid organisationId,
        string purposeKey,
        CancellationToken cancellationToken = default);

    /// <summary>Every answer this customer has given, newest first.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The records, newest first, across every purpose.</returns>
    Task<IReadOnlyList<ConsentRecord>> ReadRecordsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds one answer to the unit of work.</summary>
    /// <param name="record">The record.</param>
    void Add(ConsentRecord record);

    /// <summary>Commits.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the reason the write failed.</returns>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default);
}
