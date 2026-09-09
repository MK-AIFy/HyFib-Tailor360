using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Application.Abstractions;

/// <summary>
/// Reading and writing measurement templates, without the Application layer knowing how.
/// </summary>
public interface IMeasurementTemplateStore
{
    /// <summary>One template and every version of it.</summary>
    /// <param name="templateId">The template.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The template, or null when this organisation does not have it.</returns>
    Task<MeasurementTemplate?> FindAsync(
        Guid templateId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>One template by its code.</summary>
    /// <param name="code">The stable machine key.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The template, or null.</returns>
    Task<MeasurementTemplate?> FindByCodeAsync(
        string code,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>The template holding a given version.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The template, or null.</returns>
    Task<MeasurementTemplate?> FindByVersionAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Every template in an organisation, without their fields.</summary>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The templates, in code order.</returns>
    Task<IReadOnlyList<MeasurementTemplate>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Whether this organisation has any template at all.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when at least one exists.</returns>
    Task<bool> AnyAsync(Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new template.</summary>
    /// <param name="template">The template.</param>
    void Add(MeasurementTemplate template);

    /// <summary>The concurrency token of a template, for <c>If-Match</c>.</summary>
    /// <param name="template">The template.</param>
    /// <returns>The tag.</returns>
    EntityTag EntityTagOf(MeasurementTemplate template);

    /// <summary>Commits everything left in the context.</summary>
    /// <remarks>
    /// Answers the stale write rather than throwing it. Every version row carries <c>xmin</c>, so a caller that
    /// checked its <c>If-Match</c> and then lost the row to somebody else between the check and the commit gets a
    /// concurrency exception from the database — and the endpoint contract for that is a <c>409</c> carrying the
    /// current version, not a five hundred. The window is small but it is exactly the window the precondition
    /// exists to describe, so it cannot be the one case that escapes as an unhandled fault.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the version having changed under the caller.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits a newly started template or draft.</summary>
    /// <remarks>
    /// The template code and the next version number are both read and then written, so two administrators acting
    /// in the same moment can choose the same one. The unique indexes settle it and the loser is answered a
    /// conflict rather than a five hundred; nothing is created, so asking again takes the next free value.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the conflict.</returns>
    Task<Result> SaveNewAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits a publication, which is also the retirement of the version it supersedes.</summary>
    /// <remarks>
    /// Separate from <see cref="SaveAsync"/> because a partial unique index holds "at most one published version
    /// per template", and two administrators publishing different drafts at once is a race the database settles.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the conflict.</returns>
    Task<Result> SavePublicationAsync(CancellationToken cancellationToken = default);
}
