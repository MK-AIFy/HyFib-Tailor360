using Shouldly;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// What republishing the catalogue does to a pinned draft (#30, issue #140): the pin holds until
/// migration is asked for, and the plan names exactly three things — an option retired, a group newly
/// required, a rule added — matched by <see cref="DesignOptionGroup.Key"/> and
/// <see cref="DesignOption.Key"/> rather than by code, so a rename in the meantime is silent.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DesignSelectionMigrationTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(CatalogTestData.Now.DateTime);

    [Fact]
    public void APinnedVersionThatIsStillCurrentPlansAsUpToDateWithNothingToMigrate()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var sleeve = Group(version, blouse, "sleeve_style").Value;
        Option(version, sleeve.Id, "FULL");
        var service = Service(version, blouse, "STITCHING", [sleeve.Id]);
        version.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var plan = DesignSelectionMigration.Plan(
            version, service.Id, version, [DesignDraftSelection.Of("sleeve_style", ["FULL"])],
            CatalogTestData.MainBranch, Today);

        plan.UpToDate.ShouldBeTrue();
        plan.ServiceTypeStillOffered.ShouldBeTrue();
        plan.HasChanges.ShouldBeFalse();
        plan.Changes.ShouldBeEmpty();
    }

    [Fact]
    public void AServiceTypeRemovedEntirelyIsNamedAndMigrationIsRefused()
    {
        var pinned = CatalogTestData.Draft(1);
        var pinnedBlouse = Category(pinned, "BLOUSE_PATTERN");
        var pinnedSleeve = Group(pinned, pinnedBlouse, "sleeve_style").Value;
        Option(pinned, pinnedSleeve.Id, "FULL");
        var pinnedService = Service(pinned, pinnedBlouse, "STITCHING", [pinnedSleeve.Id]);
        pinned.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        // A second, unrelated version: same organisation, no service type at all — as if the service
        // type were removed from a draft that was never published with it.
        var current = CatalogTestData.Draft(2);
        Category(current, "GOWN");
        current.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var plan = DesignSelectionMigration.Plan(
            pinned, pinnedService.Id, current, [DesignDraftSelection.Of("sleeve_style", ["FULL"])],
            CatalogTestData.MainBranch, Today);

        plan.UpToDate.ShouldBeFalse();
        plan.ServiceTypeStillOffered.ShouldBeFalse();
        plan.TargetServiceTypeId.ShouldBeNull();
        var change = plan.Changes.ShouldHaveSingleItem();
        change.Kind.ShouldBe("design.service-type-no-longer-offered");
    }

    [Fact]
    public void ARetiredOptionIsNamedAndDroppedFromTheMigratedSelection()
    {
        var pinned = CatalogTestData.Draft(1);
        var blouse = Category(pinned, "BLOUSE_PATTERN");
        var lining = Group(pinned, blouse, "lining").Value;
        var full = Option(pinned, lining.Id, "FULL").Value;
        Option(pinned, lining.Id, "NONE");
        var service = Service(pinned, blouse, "STITCHING", [lining.Id]);
        pinned.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var clone = pinned.CloneAsDraft(new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;
        var clonedLining = clone.DesignGroups.Single(group => group.Key == lining.Key);
        var clonedFull = clonedLining.Options.Single(option => option.Key == full.Key);
        clone.EditDesignOption(clonedFull.Id, clonedFull.Details with { Active = false }, CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();
        clone.Publish(CatalogTestData.Now, null, "Retired the plain full lining.").IsSuccess.ShouldBeTrue();

        var plan = DesignSelectionMigration.Plan(
            pinned, service.Id, clone, [DesignDraftSelection.Of("lining", ["FULL"])],
            CatalogTestData.MainBranch, Today);

        plan.UpToDate.ShouldBeFalse();
        plan.ServiceTypeStillOffered.ShouldBeTrue();
        var change = plan.Changes.ShouldHaveSingleItem();
        change.Kind.ShouldBe("design.option-retired");
        change.GroupCode.ShouldBe("lining");
        change.OptionCode.ShouldBe("FULL");

        // The whole group's selection is dropped — its one chosen option did not survive — so the next
        // check will find the group unset (and required, when it is) rather than pointing at a code the
        // migrated version no longer offers.
        plan.MigratedSelections.ShouldBeEmpty();
    }

    [Fact]
    public void AGroupThatBecameRequiredIsNamedAndAnUntouchedOneIsNot()
    {
        var pinned = CatalogTestData.Draft(1);
        var blouse = Category(pinned, "BLOUSE_PATTERN");
        var lining = Group(pinned, blouse, "lining", required: false).Value;
        var closure = Group(pinned, blouse, "closure", required: true).Value;
        Option(pinned, lining.Id, "FULL");
        Option(pinned, closure.Id, "HOOK");
        var service = Service(pinned, blouse, "STITCHING", [lining.Id, closure.Id]);
        pinned.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var clone = pinned.CloneAsDraft(new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;
        var clonedLining = clone.DesignGroups.Single(group => group.Key == lining.Key);
        clone.EditDesignGroup(clonedLining.Id, clonedLining.Details with { Required = true }, CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();
        clone.Publish(CatalogTestData.Now, null, "The lining is no longer optional.").IsSuccess.ShouldBeTrue();

        var plan = DesignSelectionMigration.Plan(pinned, service.Id, clone, [], CatalogTestData.MainBranch, Today);

        var change = plan.Changes.ShouldHaveSingleItem("closure was already required and must not be reported");
        change.Kind.ShouldBe("design.group-newly-required");
        change.GroupCode.ShouldBe("lining");
    }

    [Fact]
    public void ARuleAddedForAGroupThisServiceOffersIsNamedAndOneForAnUnrelatedGroupIsNot()
    {
        var pinned = CatalogTestData.Draft(1);
        var blouse = Category(pinned, "BLOUSE_PATTERN");
        var lining = Group(pinned, blouse, "lining").Value;
        var sleeve = Group(pinned, blouse, "sleeve_style").Value;
        Option(pinned, lining.Id, "FULL");
        Option(pinned, sleeve.Id, "SLEEVELESS");
        // Only lining is linked to this service type; sleeve_style exists in the category but is not
        // offered here, the same way a category can hold groups only some of its services use.
        var service = Service(pinned, blouse, "STITCHING", [lining.Id]);
        pinned.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var blouseKey = pinned.Find(blouse)!.Key;
        var clone = pinned.CloneAsDraft(new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;
        var clonedBlouse = clone.Categories.Single(category => category.Key == blouseKey).Id;
        AddRule(clone, clonedBlouse, 1, new DesignRuleDetails(
            DesignRuleType.Note, new DesignRuleOperand("lining", DesignOperandForm.Equals, ["FULL"]),
            null, "Press the lining seam flat.", null)).IsSuccess.ShouldBeTrue();
        AddRule(clone, clonedBlouse, 2, new DesignRuleDetails(
            DesignRuleType.Note, new DesignRuleOperand("sleeve_style", DesignOperandForm.Equals, ["SLEEVELESS"]),
            null, "Bind the armhole.", null)).IsSuccess.ShouldBeTrue();
        clone.Publish(CatalogTestData.Now, null, "Two new notes.").IsSuccess.ShouldBeTrue();

        var plan = DesignSelectionMigration.Plan(pinned, service.Id, clone, [], CatalogTestData.MainBranch, Today);

        var change = plan.Changes.ShouldHaveSingleItem(
            "the sleeve_style rule cannot fire on a garment of a service that never offered that group");
        change.Kind.ShouldBe("design.rule-added");
        change.RuleIdentifier.ShouldBe("DR-01");
    }

    [Fact]
    public void ARemovedGroupIsNamedAndItsSelectionIsDropped()
    {
        var pinned = CatalogTestData.Draft(1);
        var blouse = Category(pinned, "BLOUSE_PATTERN");
        var lining = Group(pinned, blouse, "lining").Value;
        var sleeve = Group(pinned, blouse, "sleeve_style").Value;
        Option(pinned, lining.Id, "FULL");
        Option(pinned, sleeve.Id, "CAP");
        var service = Service(pinned, blouse, "STITCHING", [lining.Id, sleeve.Id]);
        pinned.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var clone = pinned.CloneAsDraft(new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;
        var clonedLining = clone.DesignGroups.Single(group => group.Key == lining.Key);
        clone.RemoveDesignGroup(clonedLining.Id, CatalogTestData.Now, null).IsSuccess.ShouldBeTrue();
        clone.Publish(CatalogTestData.Now, null, "Lining dropped for this style.").IsSuccess.ShouldBeTrue();

        var plan = DesignSelectionMigration.Plan(
            pinned, service.Id, clone,
            [DesignDraftSelection.Of("lining", ["FULL"]), DesignDraftSelection.Of("sleeve_style", ["CAP"])],
            CatalogTestData.MainBranch, Today);

        plan.Changes.ShouldContain(change => change.Kind == "design.group-no-longer-offered" && change.GroupCode == "lining");
        plan.MigratedSelections.ShouldHaveSingleItem().GroupCode.ShouldBe("sleeve_style");
    }

    [Fact]
    public void AKeyMatchedGroupWhoseActivePeriodHasLapsedIsTreatedAsRemovedRatherThanAsASuccessor()
    {
        // The republished group is still the very same concept (same Key, same options), but it is no
        // longer offerable at all as of today — the same as if it had been removed outright, and never a
        // silent successor whose selection just carries forward.
        var pinned = CatalogTestData.Draft(1);
        var blouse = Category(pinned, "BLOUSE_PATTERN");
        var lining = Group(pinned, blouse, "lining").Value;
        var sleeve = Group(pinned, blouse, "sleeve_style").Value;
        Option(pinned, lining.Id, "FULL");
        Option(pinned, sleeve.Id, "CAP");
        var service = Service(pinned, blouse, "STITCHING", [lining.Id, sleeve.Id]);
        pinned.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var clone = pinned.CloneAsDraft(new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;
        var clonedLining = clone.DesignGroups.Single(group => group.Key == lining.Key);
        clone.EditDesignGroup(
                clonedLining.Id, clonedLining.Details with { ActiveTo = Today.AddDays(-1) },
                CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();
        clone.Publish(CatalogTestData.Now, null, "Lining's season ended.").IsSuccess.ShouldBeTrue();

        var plan = DesignSelectionMigration.Plan(
            pinned, service.Id, clone,
            [DesignDraftSelection.Of("lining", ["FULL"]), DesignDraftSelection.Of("sleeve_style", ["CAP"])],
            CatalogTestData.MainBranch, Today);

        var change = plan.Changes.ShouldHaveSingleItem(
            "a key-matched group that lapsed today is exactly as gone as one removed outright, not a successor");
        change.Kind.ShouldBe("design.group-no-longer-offered");
        change.GroupCode.ShouldBe("lining");
        plan.MigratedSelections.ShouldHaveSingleItem().GroupCode.ShouldBe("sleeve_style");
    }

    [Fact]
    public void AKeyMatchedGroupNoLongerOfferedAtThisDraftsBranchIsTreatedAsRemovedRatherThanAsASuccessor()
    {
        // The republished group is still active and still linked, but only at a branch that is not this
        // draft's own — the same "not this branch" gap DesignRuleEngine.IsOfferable already refuses a
        // selection over, now applied to whether a key-matched successor may be trusted at all.
        var pinned = CatalogTestData.Draft(1);

        // The category itself spans both branches, so a group narrowing from one to the other is a
        // configuration a publish accepts — the group's own branches stay a subset of the category's.
        var blouse = pinned.AddCategory(
                CatalogTestData.Id("cat-multi-branch-BLOUSE_PATTERN"),
                CatalogTestData.Id("cat-key-multi-branch-BLOUSE_PATTERN"),
                null,
                CatalogTestData.CategoryOf(
                    "BLOUSE_PATTERN", branches: [CatalogTestData.MainBranch, CatalogTestData.SecondBranch]),
                CatalogTestData.Now,
                null)
            .Value.Id;
        var lining = Group(pinned, blouse, "lining").Value;
        var sleeve = Group(pinned, blouse, "sleeve_style").Value;
        Option(pinned, lining.Id, "FULL");
        Option(pinned, sleeve.Id, "CAP");
        var service = Service(pinned, blouse, "STITCHING", [lining.Id, sleeve.Id]);
        pinned.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var clone = pinned.CloneAsDraft(new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;
        var clonedLining = clone.DesignGroups.Single(group => group.Key == lining.Key);
        clone.EditDesignGroup(
                clonedLining.Id, clonedLining.Details with { BranchIds = [CatalogTestData.SecondBranch] },
                CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();
        clone.Publish(CatalogTestData.Now, null, "Lining moved to the other branch.").IsSuccess.ShouldBeTrue();

        var plan = DesignSelectionMigration.Plan(
            pinned, service.Id, clone,
            [DesignDraftSelection.Of("lining", ["FULL"]), DesignDraftSelection.Of("sleeve_style", ["CAP"])],
            CatalogTestData.MainBranch, Today);

        var change = plan.Changes.ShouldHaveSingleItem(
            "a group offered only at another branch is exactly as gone as one removed outright, at this branch");
        change.Kind.ShouldBe("design.group-no-longer-offered");
        change.GroupCode.ShouldBe("lining");
        plan.MigratedSelections.ShouldHaveSingleItem().GroupCode.ShouldBe("sleeve_style");
    }

    [Fact]
    public void ARenamedGroupOrOptionCodeIsNeverReportedAndTheSelectionFollowsTheNewCode()
    {
        // Section 2 / DesignOption.FollowGroupCode: codes are immutable once published, but a draft that
        // has never been published may still rename what it cloned. The migration matches by Key, so the
        // rename is invisible to the prompt and the selection simply carries the new code from here on.
        var pinned = CatalogTestData.Draft(1);
        var blouse = Category(pinned, "BLOUSE_PATTERN");
        var sleeve = Group(pinned, blouse, "sleeve_style").Value;
        var full = Option(pinned, sleeve.Id, "FULL").Value;
        var service = Service(pinned, blouse, "STITCHING", [sleeve.Id]);
        pinned.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var clone = pinned.CloneAsDraft(new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;
        var clonedSleeve = clone.DesignGroups.Single(group => group.Key == sleeve.Key);
        clone.EditDesignGroup(
                clonedSleeve.Id, clonedSleeve.Details with { Code = "sleeve_length" }, CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();
        var renamedSleeve = clone.DesignGroups.Single(group => group.Key == sleeve.Key);
        var clonedFull = renamedSleeve.Options.Single(option => option.Key == full.Key);
        clone.EditDesignOption(
                clonedFull.Id, clonedFull.Details with { Code = "FULL_LENGTH" }, CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();
        clone.Publish(CatalogTestData.Now, null, "Renamed for clarity.").IsSuccess.ShouldBeTrue();

        var plan = DesignSelectionMigration.Plan(
            pinned, service.Id, clone, [DesignDraftSelection.Of("sleeve_style", ["FULL"])],
            CatalogTestData.MainBranch, Today);

        plan.Changes.ShouldBeEmpty("a rename never breaks a code-keyed selection and is not a migration prompt");
        var migrated = plan.MigratedSelections.ShouldHaveSingleItem();
        migrated.GroupCode.ShouldBe("sleeve_length");
        migrated.OptionCodes.ShouldBe(["FULL_LENGTH"]);
    }

    private static Guid Category(CatalogVersion version, string code)
        => version.AddCategory(
                CatalogTestData.Id($"cat-{version.VersionNumber}-{code}"),
                CatalogTestData.Id($"cat-key-{code}"),
                null,
                CatalogTestData.CategoryOf(code),
                CatalogTestData.Now,
                null)
            .Value.Id;

    private static Tailor360.Platform.Abstractions.Results.Result<DesignOptionGroup> Group(
        CatalogVersion version, Guid categoryId, string code, bool required = false, int displayOrder = 0)
        => version.AddDesignGroup(
            CatalogTestData.Id($"group-{version.VersionNumber}-{categoryId}-{code}"),
            CatalogTestData.Id($"group-key-{categoryId}-{code}"),
            categoryId,
            new DesignGroupDetails(
                code, code.Replace('_', ' '), null, DesignSelectionMode.MultipleChoice, required, displayOrder,
                null, null, [CatalogTestData.MainBranch]),
            CatalogTestData.Now,
            null);

    private static Tailor360.Platform.Abstractions.Results.Result<DesignOption> Option(
        CatalogVersion version, Guid groupId, string code, int displayOrder = 0)
        => version.AddDesignOption(
            CatalogTestData.Id($"option-{version.VersionNumber}-{groupId}-{code}"),
            CatalogTestData.Id($"option-key-{groupId}-{code}"),
            groupId,
            new DesignOptionDetails(
                code, code, null, "What the choice means for the garment.", null, "The shape in words.",
                null, 0, displayOrder, true),
            CatalogTestData.Now,
            null);

    private static ServiceType Service(CatalogVersion version, Guid categoryId, string code, IReadOnlyList<Guid> groupIds)
        => version.AddServiceType(
                CatalogTestData.Id($"svc-{version.VersionNumber}-{categoryId}-{code}"),
                CatalogTestData.Id($"svc-key-{categoryId}-{code}"),
                categoryId,
                CatalogTestData.ServiceOf(code) with { DesignOptionGroupIds = groupIds },
                CatalogTestData.Now,
                null)
            .Value;

    private static Tailor360.Platform.Abstractions.Results.Result<DesignRule> AddRule(
        CatalogVersion version, Guid categoryId, int number, DesignRuleDetails details)
        => version.AddDesignRule(
            CatalogTestData.Id($"rule-{version.VersionNumber}-{number}"),
            CatalogTestData.Id($"rule-key-{version.VersionNumber}-{number}"),
            categoryId,
            number,
            details,
            CatalogTestData.Now,
            null);
}
