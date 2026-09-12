using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain;
using Tailor360.Modules.Catalog.Domain.Design;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// The published <see cref="IDesignSelectionValidator"/>: loads the version the request names and hands
/// its category's groups and rules to the pure engine.
/// </summary>
/// <remarks>
/// A version is read by identity, published or not: a draft is pinned to the version it was started
/// against and keeps it through a republish (<c>docs/prd/design-options.md</c> section 7), and a design
/// revision passes the currently published one. Nothing here decides which; the caller does.
/// </remarks>
/// <param name="store">The catalogue store.</param>
public sealed class DesignSelectionValidator(ICatalogStore store) : IDesignSelectionValidator
{
    /// <inheritdoc />
    public async Task<Result<DesignEvaluation>> ValidateAsync(
        DesignSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var version = await store.FindAsync(request.CatalogVersionId, request.OrganisationId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<DesignEvaluation>(CatalogErrors.VersionNotFound);
        }

        if (version.Find(request.CategoryId) is null)
        {
            return Result.Failure<DesignEvaluation>(CatalogErrors.CategoryNotFound);
        }

        var evaluated = DesignRuleEngine.Evaluate(
            [.. version.DesignGroupsOf(request.CategoryId)],
            [.. version.DesignRulesOf(request.CategoryId)],
            [.. request.Selections.Select(selection => new DesignSelectionInput(selection.GroupCode, selection.OptionCodes))],
            request.BranchId,
            request.EvaluatedOn,
            request.HasReferenceImage);

        return Result.Success(new DesignEvaluation(
            evaluated.IsConfirmable,
            [.. evaluated.Violations.Select(violation => new DesignViolation(
                violation.Code,
                violation.RuleIdentifier,
                violation.GroupCode,
                violation.OptionCodes,
                violation.RelatedGroupCode,
                violation.RelatedOptionCodes,
                violation.Message,
                violation.Blocks))],
            [.. evaluated.AutoSelections.Select(auto => new DesignAutoSelection(auto.RuleIdentifier, auto.GroupCode, auto.OptionCode))],
            [.. evaluated.Notes.Select(note => new DesignNote(note.RuleIdentifier, note.Text))]));
    }
}
