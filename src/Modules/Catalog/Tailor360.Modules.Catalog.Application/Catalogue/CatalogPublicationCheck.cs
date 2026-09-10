using Microsoft.Extensions.Logging;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Asks every registered validator what is wrong with one catalogue version.
/// </summary>
/// <remarks>
/// <para>
/// One definition of "what is wrong with this version", used by three callers that would otherwise each grow their
/// own: publication refuses on it, the administration screen's check reports it, and the reconciliation re-asks it
/// after another module changed something (issue #91). A second copy is how a version comes to publish cleanly and
/// then reconcile as broken, or the reverse, for no reason a person could work out.
/// </para>
/// <para>
/// <strong>A validator that fails is not a validator that found nothing.</strong> Every registered validator is
/// asked even after an earlier one reported errors, because one report naming everything wrong is worth far more
/// than five rounds of fixing one reference at a time. But a validator that <em>throws</em> fails the whole check:
/// "we could not check" is a different answer from "we checked and it is right", and only one of them is a basis
/// for publishing — or for recording that a published version has gone bad.
/// </para>
/// </remarks>
/// <param name="store">The catalogue store, for the code history a candidate carries.</param>
/// <param name="validators">Every registered validator, this module's built-in one included.</param>
/// <param name="logger">The logger, for a validator that fell over.</param>
public sealed class CatalogPublicationCheck(
    ICatalogStore store,
    IEnumerable<ICatalogDependencyValidator> validators,
    ILogger<CatalogPublicationCheck> logger)
{
    private static readonly Action<ILogger, string, Guid, Exception?> ValidatorFailed =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Error,
            new EventId(2911, nameof(ValidatorFailed)),
            "Catalogue validator {Validator} failed while checking version {VersionId}.");

    /// <summary>Asks every validator about one version.</summary>
    /// <param name="version">The version — a draft being published, or a published one being re-checked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Everything found, or the reason the check could not be completed.</returns>
    public async Task<Result<CatalogValidationReport>> RunAsync(
        CatalogVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        var ledger = await store.ReadCodeHistoryAsync(version.OrganisationId, cancellationToken);
        var candidate = CatalogProjection.ToCandidate(version, ledger);
        var findings = new List<AttributedFinding>();

        foreach (var validator in validators)
        {
            try
            {
                var found = await validator.ValidatePublicationAsync(candidate, cancellationToken);

                findings.AddRange(found.Select(finding => new AttributedFinding(validator.Name, finding)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // A validator is another module's code; one that fell over must stop
            catch (Exception exception) // the command rather than take the whole request down.
#pragma warning restore CA1031
            {
                ValidatorFailed(logger, validator.Name, version.Id, exception);

                return Result.Failure<CatalogValidationReport>(
                    CatalogErrors.ValidatorUnavailable(validator.Name));
            }
        }

        return Result.Success(new CatalogValidationReport(version.Id, findings));
    }
}
