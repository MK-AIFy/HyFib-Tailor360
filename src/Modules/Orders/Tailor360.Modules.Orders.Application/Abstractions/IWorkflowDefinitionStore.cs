using Tailor360.Modules.Orders.Domain.Workflows;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Application.Abstractions;

/// <summary>
/// Reads and writes workflow definitions, each with every version it has ever drafted, published or retired.
/// </summary>
/// <remarks>
/// <para>
/// The module's internal port, so it speaks in domain types — a published contract may not. A definition is
/// loaded whole because every command on it needs the whole set: numbering a new version and refusing a second
/// published version both read every existing version, exactly the reasoning <c>IOrderDraftStore</c> gives for
/// loading a draft with its garment sections.
/// </para>
/// <para>
/// <strong>Every read is scoped by organisation.</strong> A workflow definition is organisation-wide
/// configuration, like a catalogue version, with no branch of its own — <c>IOrderDraftStore</c>'s remarks give
/// the reason a store still never infers reach from anything but the caller's claims.
/// </para>
/// </remarks>
public interface IWorkflowDefinitionStore
{
    /// <summary>
    /// Loads one definition for change, with every version, or null when no definition has that identity within
    /// the organisation.
    /// </summary>
    /// <param name="workflowDefinitionId">The definition.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The definition, or null.</returns>
    Task<WorkflowDefinition?> FindAsync(
        Guid workflowDefinitionId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The version that was published at a given instant, for the definition named.
    /// </summary>
    /// <remarks>
    /// What start-production (E06-F02-4) calls to resolve the version a new job pins. Answered without loading
    /// every other definition's versions, unlike <see cref="FindAsync"/>, because starting a job needs exactly
    /// one version and not the whole editing history of the process it belongs to.
    /// </remarks>
    /// <param name="workflowDefinitionId">The definition.</param>
    /// <param name="organisationId">The tenant the caller is acting within.</param>
    /// <param name="at">The instant to judge.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The version published at that instant, or null when none was.</returns>
    Task<WorkflowVersion?> FindPublishedVersionAsync(
        Guid workflowDefinitionId,
        Guid organisationId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a newly created definition to the unit of work.</summary>
    /// <param name="definition">The definition.</param>
    void Add(WorkflowDefinition definition);

    /// <summary>The concurrency token an edit to the definition's own row must be made against.</summary>
    /// <param name="definition">A tracked definition.</param>
    /// <returns>The entity tag.</returns>
    EntityTag EntityTagOf(WorkflowDefinition definition);

    /// <summary>The concurrency token an edit to one of its versions must be made against.</summary>
    /// <param name="version">A tracked version.</param>
    /// <returns>The entity tag.</returns>
    EntityTag EntityTagOf(WorkflowVersion version);

    /// <summary>
    /// Commits, turning the failure a second administrator can cause into a result rather than an exception.
    /// </summary>
    /// <remarks>
    /// Answers <c>OrdersErrors.ConcurrentChange</c> when a row moved between the read and the write — including
    /// the case two administrators raced to publish a second version of one definition, which the partial
    /// unique index refuses as a unique violation and this maps the same way <c>OrderDraftStore</c> maps the
    /// draft-garment position race.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the reason the write was refused.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
