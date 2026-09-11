using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Application.Abstractions;

/// <summary>
/// Where drafts and confirmed measurements are kept.
/// </summary>
/// <remarks>
/// Separate from <c>IMeasurementTemplateStore</c> even though both live in the <c>customers</c> schema, because
/// they are read by different people for different reasons: a template is administered a few times a year by an
/// Owner, and a measurement is taken every day at a counter. One interface would put the administration surface
/// in front of everything that captures.
/// </remarks>
public interface IMeasurementCaptureStore
{
    /// <summary>The branch's open draft for a customer and template, or null when there is none.</summary>
    /// <param name="branchId">The branch measuring.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="templateId">The template.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open draft, or null.</returns>
    Task<MeasurementDraft?> FindOpenDraftAsync(
        Guid branchId,
        Guid customerId,
        Guid templateId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>One draft by its identity, open or consumed.</summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="organisationId">The organisation it must belong to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or null.</returns>
    Task<MeasurementDraft?> FindDraftAsync(
        Guid draftId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>One confirmed version by its identity.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="organisationId">The organisation it must belong to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or null.</returns>
    Task<MeasurementVersion?> FindVersionAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>The number the next confirmed version for this customer and template will take.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="templateId">The template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One more than the highest, or one.</returns>
    /// <remarks>
    /// Read and then written, so two confirmations in the same instant can pick the same number. The unique index
    /// settles it and <see cref="SaveConfirmationAsync"/> turns the violation into a conflict a person can act on.
    /// </remarks>
    Task<int> NextVersionNumberAsync(
        Guid customerId,
        Guid templateId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new draft to the context.</summary>
    /// <param name="draft">The draft.</param>
    void Add(MeasurementDraft draft);

    /// <summary>Adds a new confirmed version to the context.</summary>
    /// <param name="version">The version.</param>
    void Add(MeasurementVersion version);

    /// <summary>The draft's concurrency token, as the <c>ETag</c> a client sends back.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>The tag.</returns>
    EntityTag EntityTagOf(MeasurementDraft draft);

    /// <summary>Commits a change to a draft.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the reason it could not be committed.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a confirmation: the consumed draft, the version, its values and the outbox message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One save, because <c>docs/architecture/invariants.md</c> section 4.3 puts all four inside one transactional
    /// boundary. A version committed without its event would leave every downstream reader unaware that a
    /// measurement exists; an event committed without its version would announce one that does not.
    /// </para>
    /// <para>
    /// Two unique violations are expected here and are turned into conflicts rather than faults: a second
    /// confirmation of the same draft (INV-MSR-02) and two confirmations racing for one version number.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the reason it could not be committed.</returns>
    Task<Result> SaveConfirmationAsync(CancellationToken cancellationToken = default);
}
