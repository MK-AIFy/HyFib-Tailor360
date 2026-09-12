using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>Reads and writes GST registrations.</summary>
public interface IGstRegistrationStore
{
    /// <summary>One registration. A registration of another organisation is not found.</summary>
    /// <param name="registrationId">The registration.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registration, or null.</returns>
    Task<GstRegistration?> FindAsync(
        Guid registrationId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Every registration of the organisation, by branch and then by first day.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registrations.</returns>
    Task<IReadOnlyList<GstRegistration>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Every registration of one branch, by first day.</summary>
    /// <param name="branchId">The branch.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registrations.</returns>
    Task<IReadOnlyList<GstRegistration>> ListForBranchAsync(
        Guid branchId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a registration to the context.</summary>
    /// <param name="registration">The registration.</param>
    void Add(GstRegistration registration);

    /// <summary>The registration's concurrency token.</summary>
    /// <param name="registration">The registration.</param>
    /// <returns>The tag.</returns>
    EntityTag EntityTagOf(GstRegistration registration);

    /// <summary>Commits, translating an overlapping-dates clash into a conflict.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the conflict.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
