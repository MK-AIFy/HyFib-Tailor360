using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain;
using Tailor360.Modules.Catalog.Domain.Design;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// The published <see cref="IDesignSelectionQuery"/>: freshly validates a draft's selections against its
/// pinned version and builds the snapshot they would freeze to (#30, issue #140).
/// </summary>
/// <param name="draftStore">The draft store.</param>
/// <param name="store">The catalogue store, for the version a draft is pinned to.</param>
/// <param name="validator">The rule evaluator.</param>
/// <param name="clock">The clock, for the day the rules are read against.</param>
public sealed class DesignSelectionQuery(
    IDesignSelectionDraftStore draftStore,
    ICatalogStore store,
    IDesignSelectionValidator validator,
    IClock clock)
    : IDesignSelectionQuery
{
    /// <inheritdoc />
    public async Task<Result<DesignSelectionSnapshot>> GetAsync(
        Guid draftId,
        Guid organisationId,
        bool hasReferenceImage,
        CancellationToken cancellationToken = default)
    {
        var draft = await draftStore.FindAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<DesignSelectionSnapshot>(CatalogErrors.DesignDraftNotFound);
        }

        var version = await store.FindAsync(draft.CatalogVersionId, organisationId, cancellationToken);

        if (version is null)
        {
            return Result.Failure<DesignSelectionSnapshot>(CatalogErrors.VersionNotFound);
        }

        var service = version.FindService(draft.ServiceTypeId);

        if (service is null)
        {
            return Result.Failure<DesignSelectionSnapshot>(CatalogErrors.ServiceTypeNotFound);
        }

        var inputs = draft.Selections
            .Select(selection => new DesignSelectionInput(selection.GroupCode, selection.OptionCodes))
            .ToList();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, IndiaTimeZone.Instance).DateTime);

        var evaluated = await validator.ValidateAsync(
            new DesignSelectionRequest(
                organisationId,
                version.Id,
                service.CategoryId,
                draft.BranchId,
                today,
                [.. inputs.Select(input => new DesignSelection(input.GroupCode, input.OptionCodes))],
                hasReferenceImage),
            cancellationToken);

        if (evaluated.IsFailure)
        {
            return Result.Failure<DesignSelectionSnapshot>(evaluated.Error);
        }

        // The validator answers over the whole category; narrowed here to what this draft's own service
        // type offers, so a required group (or a requires rule's target) that belongs only to a sibling
        // service can neither block this draft nor auto-select an option this service never offered
        // (#140).
        var scoped = DesignEvaluationScope.Narrow(
            evaluated.Value, DesignEvaluationScope.OfferedGroupCodesOf(version, service));

        // A requires rule with exactly one admissible option is settled on the customer's behalf rather
        // than raised as a violation; merged in here so the frozen snapshot — its price-list item and
        // job-card details included — matches what the rule engine actually treated as chosen, not only
        // what the draft happened to store.
        var withAutoSelections = MergeAutoSelections(inputs, scoped.AutoSelections);

        var built = GarmentDesignSnapshotBuilder.From(
            version,
            service.CategoryId,
            service.Id,
            withAutoSelections,
            [.. scoped.Notes.Select(note => note.Text)],
            draft.Instructions);

        if (built.IsFailure)
        {
            return Result.Failure<DesignSelectionSnapshot>(built.Error);
        }

        // Open means may still be consumed: Consume itself refuses an expired draft, so a draft that has
        // simply outlived its lifetime and has not yet been reaped must not read as open here either.
        var isOpen = draft.IsOpen && !draft.IsExpired(clock.UtcNow);

        return Result.Success(new DesignSelectionSnapshot(
            draft.Id,
            draft.BranchId,
            isOpen,
            scoped.IsConfirmable,
            scoped.Violations,
            GarmentDesignSnapshotMapper.ToContract(built.Value)));
    }

    /// <summary>Unions a rule's automatic selections into the explicitly stored inputs, one entry per group.</summary>
    /// <param name="inputs">What the draft itself holds.</param>
    /// <param name="autoSelections">What a requires rule selected on the customer's behalf.</param>
    /// <returns>The merged set, ready for <see cref="GarmentDesignSnapshotBuilder.From"/>.</returns>
    private static List<DesignSelectionInput> MergeAutoSelections(
        IReadOnlyList<DesignSelectionInput> inputs, IReadOnlyList<DesignAutoSelection> autoSelections)
    {
        var byGroup = inputs.ToDictionary(
            input => input.GroupCode, input => new List<string>(input.OptionCodes), StringComparer.Ordinal);

        foreach (var auto in autoSelections)
        {
            if (!byGroup.TryGetValue(auto.GroupCode, out var codes))
            {
                codes = [];
                byGroup[auto.GroupCode] = codes;
            }

            if (!codes.Contains(auto.OptionCode, StringComparer.Ordinal))
            {
                codes.Add(auto.OptionCode);
            }
        }

        return [.. byGroup.Select(pair => new DesignSelectionInput(pair.Key, pair.Value))];
    }
}
