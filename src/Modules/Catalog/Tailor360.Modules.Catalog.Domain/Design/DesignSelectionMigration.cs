using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// What republishing the catalogue does to an open draft (<c>docs/prd/design-options.md</c> section 7):
/// pure, deterministic, and read-only over both versions until <see cref="DesignSelectionDraft.MigrateTo"/>
/// is actually asked to apply the plan it computes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The pin holds until migration is asked for.</strong> This type never mutates anything; it
/// only says what a migration <em>would</em> do, so a read can show the prompt without moving the
/// draft, and <c>…/migrate</c> can apply exactly the plan the prompt described.
/// </para>
/// <para>
/// <strong>Matched by <see cref="DesignOptionGroup.Key"/> and <see cref="DesignOption.Key"/>, never by
/// code.</strong> A code may change between versions — a draft that only ever touches its own rows may
/// rename a group or an option it cloned from the published one before publishing it again — so the
/// concept identity is what says "this is the same group, under whatever it is called now" the way
/// <c>DesignOption.FollowGroupCode</c> already relies on for illustration references.
/// </para>
/// <para>
/// <strong>Three things are worth naming, and only three</strong> (<c>docs/prd/design-options.md</c>
/// section 7): an option the customer could have chosen that is now retired or gone, a group that
/// became required, and a rule the category gained that reads a group this service type offers. A
/// group whose own <see cref="DesignOptionGroup.Required"/> stayed the same, a rule that already existed,
/// and a relabelled option are not reported — a rename never breaks anything a code-keyed selection
/// depends on, and the migrated selection simply carries the new label from here on.
/// </para>
/// <para>
/// <strong>A selection whose option was retired cannot survive migration</strong>: it is silently absent
/// from <see cref="DesignSelectionMigrationPlan.MigratedSelections"/>, exactly as an unset group is,
/// leaving the next <c>Validate</c> call to say whether the now-empty group was required.
/// </para>
/// </remarks>
public static class DesignSelectionMigration
{
    /// <summary>Plans what migrating a draft to the currently published version would do.</summary>
    /// <param name="pinnedVersion">The version the draft is pinned to. Published or retired.</param>
    /// <param name="pinnedServiceTypeId">The service type the draft names, a row of <paramref name="pinnedVersion"/>.</param>
    /// <param name="currentVersion">The currently published version.</param>
    /// <param name="selections">What the draft holds today, coded against <paramref name="pinnedVersion"/>.</param>
    /// <param name="branchId">The draft's branch, which decides whether a key-matched successor is one it can still choose from.</param>
    /// <param name="on">The day, in the branch's timezone, which decides a successor's active period.</param>
    /// <returns>The plan. See <see cref="DesignSelectionMigrationPlan.UpToDate"/> for whether it is a no-op.</returns>
    public static DesignSelectionMigrationPlan Plan(
        CatalogVersion pinnedVersion,
        Guid pinnedServiceTypeId,
        CatalogVersion currentVersion,
        IReadOnlyCollection<DesignDraftSelection> selections,
        Guid branchId,
        DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(pinnedVersion);
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(selections);

        if (pinnedVersion.Id == currentVersion.Id)
        {
            return DesignSelectionMigrationPlan.Current(currentVersion.Id, pinnedServiceTypeId);
        }

        if (pinnedVersion.FindService(pinnedServiceTypeId) is not { } pinnedService)
        {
            return DesignSelectionMigrationPlan.ServiceTypeGone(currentVersion.Id);
        }

        var currentService = currentVersion.ServiceTypes
            .SingleOrDefault(candidate => candidate.Key == pinnedService.Key);

        if (currentService is null)
        {
            return DesignSelectionMigrationPlan.ServiceTypeGone(currentVersion.Id);
        }

        var pinnedGroups = GroupsOf(pinnedVersion, pinnedService);
        var currentGroupsByKey = GroupsOf(currentVersion, currentService).ToDictionary(group => group.Key);

        var changes = new List<DesignMigrationChange>();

        // Group Code (pinned) -> the same group in the current version, or null when it is gone.
        var successor = new Dictionary<string, DesignOptionGroup?>(StringComparer.Ordinal);

        foreach (var oldGroup in pinnedGroups)
        {
            currentGroupsByKey.TryGetValue(oldGroup.Key, out var matched);

            // A key match that is no longer offerable at this branch today — its active period lapsed,
            // or the branch was dropped from it — is not a usable successor: the customer could never
            // choose it again here, so a selection kept by key alone would freeze on an option nobody
            // at this branch can see. Read exactly like a group that was removed outright.
            var newGroup = matched is not null && IsOfferable(matched, branchId, on) ? matched : null;
            successor[oldGroup.Code] = newGroup;

            if (newGroup is null)
            {
                changes.Add(DesignMigrationChange.GroupNoLongerOffered(oldGroup.Code));
                continue;
            }

            if (newGroup.Required && !oldGroup.Required)
            {
                changes.Add(DesignMigrationChange.GroupNewlyRequired(newGroup.Code));
            }

            foreach (var oldOption in oldGroup.Options.Where(option => option.Active))
            {
                var newOption = newGroup.Options.SingleOrDefault(candidate => candidate.Key == oldOption.Key);

                if (newOption is null || !newOption.Active)
                {
                    changes.Add(DesignMigrationChange.OptionRetired(newGroup.Code, oldOption.Code));
                }
            }
        }

        // A group the pinned version never linked to this service at all — not merely one whose own
        // Required flag flipped — can never produce GroupNewlyRequired above, since the loop that raises
        // it only ever walks groups the pinned version already offered. Named here instead, so a required
        // choice a republish newly links to this service shows up in the prompt rather than only
        // surfacing afterwards as a blocking validation error nobody was warned about.
        var pinnedGroupKeys = pinnedGroups.Select(group => group.Key).ToHashSet();

        foreach (var newlyLinkedGroup in currentGroupsByKey.Values
                     .Where(group => !pinnedGroupKeys.Contains(group.Key) && group.Required && IsOfferable(group, branchId, on))
                     .OrderBy(group => group.DisplayOrder)
                     .ThenBy(group => group.Code, StringComparer.Ordinal))
        {
            changes.Add(DesignMigrationChange.GroupNewlyRequired(newlyLinkedGroup.Code));
        }

        var offeredGroupCodes = currentGroupsByKey.Values.Select(group => group.Code).ToHashSet(StringComparer.Ordinal);
        var pinnedRuleKeys = pinnedVersion.DesignRulesOf(pinnedService.CategoryId)
            .Select(rule => rule.Key)
            .ToHashSet();

        foreach (var newRule in currentVersion.DesignRulesOf(currentService.CategoryId))
        {
            if (pinnedRuleKeys.Contains(newRule.Key))
            {
                continue;
            }

            // Only a rule this service type could ever meet: one whose every group is one it offers.
            // A rule the category gained for a group this service type has never linked can never fire
            // on a garment of this service, so naming it would tell Reception about a change nobody's
            // picker shows.
            if (newRule.Details.GroupCodes.All(offeredGroupCodes.Contains))
            {
                changes.Add(DesignMigrationChange.RuleAdded(newRule.Identifier, newRule.Statement));
            }
        }

        var migrated = new List<DesignSelectionInput>();

        foreach (var selection in selections)
        {
            if (successor.GetValueOrDefault(selection.GroupCode) is not { } newGroup)
            {
                continue;
            }

            var oldGroup = pinnedGroups.First(group => group.Code == selection.GroupCode);
            var surviving = new List<string>();

            foreach (var code in selection.OptionCodes)
            {
                if (oldGroup.FindOptionByCode(code) is not { } oldOption)
                {
                    continue;
                }

                var newOption = newGroup.Options.SingleOrDefault(candidate => candidate.Key == oldOption.Key);

                if (newOption is { Active: true })
                {
                    surviving.Add(newOption.Code);
                }
            }

            if (surviving.Count > 0)
            {
                migrated.Add(new DesignSelectionInput(newGroup.Code, surviving));
            }
        }

        return DesignSelectionMigrationPlan.Changed(
            currentVersion.Id, currentService.Id, changes, migrated);
    }

    /// <summary>
    /// The groups a service type offers, as rows of its own version. Public so the one place that
    /// resolves "the groups this draft's service type actually offers" is shared with
    /// <see cref="DesignSelectionDraft.Save"/> and with narrowing a category-wide evaluation to this
    /// service (#140) rather than duplicated at each.
    /// </summary>
    public static List<DesignOptionGroup> GroupsOf(CatalogVersion version, ServiceType serviceType)
        => [.. serviceType.DesignOptionGroupIds.Select(version.FindDesignGroup).OfType<DesignOptionGroup>()];

    /// <summary>Whether a group could be chosen from at this branch today: active, and offered here.</summary>
    private static bool IsOfferable(DesignOptionGroup group, Guid branchId, DateOnly on)
        => group.IsActiveOn(on) && group.BranchIds.Contains(branchId);
}

/// <summary>What migrating a draft to the currently published version would do.</summary>
/// <param name="UpToDate">True when the draft is already pinned to the currently published version.</param>
/// <param name="ServiceTypeStillOffered">
/// False when the published version no longer offers this service type at all — migration is refused
/// rather than silently dropping every selection.
/// </param>
/// <param name="TargetCatalogVersionId">The version migrating would pin to.</param>
/// <param name="TargetServiceTypeId">The equivalent service type in that version, or null when it is gone.</param>
/// <param name="Changes">Every reason the prompt names, in the order this type found them.</param>
/// <param name="MigratedSelections">What would survive, ready for <see cref="DesignSelectionDraft.MigrateTo"/>.</param>
public sealed record DesignSelectionMigrationPlan(
    bool UpToDate,
    bool ServiceTypeStillOffered,
    Guid TargetCatalogVersionId,
    Guid? TargetServiceTypeId,
    IReadOnlyList<DesignMigrationChange> Changes,
    IReadOnlyList<DesignSelectionInput> MigratedSelections)
{
    /// <summary>Whether there is anything for a migration prompt to say.</summary>
    public bool HasChanges => Changes.Count > 0;

    /// <summary>The no-op answer: the draft already names the currently published version.</summary>
    internal static DesignSelectionMigrationPlan Current(Guid versionId, Guid serviceTypeId)
        => new(true, true, versionId, serviceTypeId, [], []);

    /// <summary>The published version no longer offers this service type at all.</summary>
    internal static DesignSelectionMigrationPlan ServiceTypeGone(Guid versionId)
        => new(false, false, versionId, null, [DesignMigrationChange.ServiceTypeNoLongerOffered()], []);

    /// <summary>A migration is available, whether or not it changes anything a caller need act on.</summary>
    internal static DesignSelectionMigrationPlan Changed(
        Guid versionId,
        Guid serviceTypeId,
        IReadOnlyList<DesignMigrationChange> changes,
        IReadOnlyList<DesignSelectionInput> migratedSelections)
        => new(false, true, versionId, serviceTypeId, changes, migratedSelections);
}

/// <summary>One thing the republish changed that the pinned draft does not yet know about.</summary>
/// <param name="Kind">A stable dotted code the client branches on, for example <c>design.option-retired</c>.</param>
/// <param name="GroupCode">The group the change is about, in the currently published version.</param>
/// <param name="OptionCode">The option, when the change is about one.</param>
/// <param name="RuleIdentifier">The rule's <c>DR-nn</c>, when the change is a rule added.</param>
/// <param name="Message">What changed, in the shop's words.</param>
public sealed record DesignMigrationChange(
    string Kind,
    string? GroupCode,
    string? OptionCode,
    string? RuleIdentifier,
    string Message)
{
    /// <summary>A group the draft could choose from is no longer offered by this service type at all.</summary>
    public static DesignMigrationChange GroupNoLongerOffered(string groupCode)
        => new(
            "design.group-no-longer-offered",
            groupCode,
            null,
            null,
            $"'{groupCode}' is no longer offered for this service. Any choice already made there is dropped.");

    /// <summary>A group that was optional now demands a choice.</summary>
    public static DesignMigrationChange GroupNewlyRequired(string groupCode)
        => new(
            "design.group-newly-required",
            groupCode,
            null,
            null,
            $"'{groupCode}' now needs a choice before the garment can be confirmed. It did not before.");

    /// <summary>An option the draft could have chosen is retired or gone.</summary>
    public static DesignMigrationChange OptionRetired(string groupCode, string optionCode)
        => new(
            "design.option-retired",
            groupCode,
            optionCode,
            null,
            $"'{groupCode}.{optionCode}' is no longer offered. A selection naming it cannot survive migration.");

    /// <summary>The published version no longer offers this service type at all.</summary>
    public static DesignMigrationChange ServiceTypeNoLongerOffered()
        => new(
            "design.service-type-no-longer-offered",
            null,
            null,
            null,
            "This service is no longer in the published catalogue. The pinned selections can be finished "
            + "as they are, but they can never be migrated to the current version.");

    /// <summary>A rule this service type could meet was added.</summary>
    public static DesignMigrationChange RuleAdded(string ruleIdentifier, string statement)
        => new(
            "design.rule-added",
            null,
            null,
            ruleIdentifier,
            $"{ruleIdentifier} is new: {statement}. It applies from the moment of migration, never "
            + "retrospectively to the pinned version.");
}
