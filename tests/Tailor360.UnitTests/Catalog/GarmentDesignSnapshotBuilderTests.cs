using System.Text.Json;
using Shouldly;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// The design snapshot builder (#30, issue #140): a pure copy of a validated selection set, ordered the
/// way a job card renders it, unaffected by a retired option, a reordered group, a renamed label or a
/// later republish of the catalogue.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GarmentDesignSnapshotBuilderTests
{
    [Fact]
    public void BuildsEverySectionSevenFieldForOneSelection()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var sleeve = Group(version, blouse, "sleeve_style", displayOrder: 0).Value;
        var full = Option(
            version, sleeve.Id, "FULL", displayOrder: 1,
            illustrationKey: "design_blouse_sleeve_v1#sleeve_style.FULL", priceListItemCode: "PI_FULL").Value;
        var service = Service(version, blouse, "STITCHING", [sleeve.Id]);
        version.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var built = GarmentDesignSnapshotBuilder.From(
            version, blouse, service.Id,
            [new DesignSelectionInput("sleeve_style", ["FULL"])],
            ["Press the seam on the reverse."],
            "Slightly loose at the shoulder.");

        built.IsSuccess.ShouldBeTrue(built.IsFailure ? built.Error.Message : string.Empty);
        var snapshot = built.Value;

        snapshot.CatalogVersionId.ShouldBe(version.Id);
        snapshot.CatalogVersionNumber.ShouldBe(version.VersionNumber);
        snapshot.CategoryCode.ShouldBe("BLOUSE_PATTERN");
        snapshot.ServiceTypeCode.ShouldBe("STITCHING");
        snapshot.ConditionalNotes.ShouldBe(["Press the seam on the reverse."]);
        snapshot.Instructions.ShouldBe("Slightly loose at the shoulder.");

        var selection = snapshot.Selections.ShouldHaveSingleItem();
        selection.GroupCode.ShouldBe("sleeve_style");
        selection.GroupLabel.ShouldBe(sleeve.Name);
        selection.GroupDisplayOrder.ShouldBe(0);
        selection.OptionCode.ShouldBe("FULL");
        selection.OptionLabel.ShouldBe(full.Name);
        selection.OptionDisplayOrder.ShouldBe(1);
        selection.IllustrationKey.ShouldBe("design_blouse_sleeve_v1#sleeve_style.FULL");
        selection.IllustrationAlt.ShouldBe(full.IllustrationAlt);
        selection.PriceListItemCode.ShouldBe("PI_FULL");
        selection.OptionVersion.ShouldBe(version.VersionNumber);
    }

    [Fact]
    public void BlankInstructionsAreDroppedRatherThanStoredAsWhitespace()
    {
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var sleeve = Group(version, blouse, "sleeve_style").Value;
        Option(version, sleeve.Id, "FULL");
        var service = Service(version, blouse, "STITCHING", [sleeve.Id]);

        var built = GarmentDesignSnapshotBuilder.From(
            version, blouse, service.Id,
            [new DesignSelectionInput("sleeve_style", ["FULL"])], [], "   ");

        built.Value.Instructions.ShouldBeNull();
    }

    [Fact]
    public void ARetiredOptionStillBuildsBecauseTheSnapshotIsAFrozenCopyNotALiveOfferabilityCheck()
    {
        // Section 7: "a reprint a year later may find the group retired". The builder runs on selections
        // IDesignSelectionValidator has already approved; it is not itself an offerability check, so a
        // selection naming an option retired since is still rendered, exactly as it was chosen.
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var lining = Group(version, blouse, "lining").Value;
        var full = Option(version, lining.Id, "FULL").Value;
        var service = Service(version, blouse, "STITCHING", [lining.Id]);

        version.EditDesignOption(full.Id, full.Details with { Active = false }, CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();

        var built = GarmentDesignSnapshotBuilder.From(
            version, blouse, service.Id, [new DesignSelectionInput("lining", ["FULL"])], [], null);

        built.IsSuccess.ShouldBeTrue();
        var selection = built.Value.Selections.ShouldHaveSingleItem();
        selection.OptionCode.ShouldBe("FULL");
        selection.OptionLabel.ShouldBe(full.Name);
    }

    [Fact]
    public void SelectionsAreOrderedByDisplayOrderRatherThanByInputOrCode()
    {
        // "closure" sorts before "sleeve_style" alphabetically, but sleeve_style is DisplayOrder 0 and
        // closure is DisplayOrder 1 — the card renders groups in the catalogue's own order, never the
        // caller's or the alphabet's.
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var closure = Group(version, blouse, "closure", displayOrder: 1).Value;
        var sleeve = Group(version, blouse, "sleeve_style", displayOrder: 0).Value;
        Option(version, closure.Id, "HOOK", displayOrder: 0);
        Option(version, sleeve.Id, "CAP", displayOrder: 5);
        Option(version, sleeve.Id, "FULL", displayOrder: 1);
        var service = Service(version, blouse, "STITCHING", [closure.Id, sleeve.Id]);

        var built = GarmentDesignSnapshotBuilder.From(
            version, blouse, service.Id,
            [
                new DesignSelectionInput("closure", ["HOOK"]),
                new DesignSelectionInput("sleeve_style", ["CAP", "FULL"]),
            ],
            [], null);

        built.Value.Selections.Select(selection => (selection.GroupCode, selection.OptionCode)).ShouldBe(
            [("sleeve_style", "FULL"), ("sleeve_style", "CAP"), ("closure", "HOOK")]);
    }

    [Fact]
    public void ARenamedGroupOrOptionLabelIsPickedUpTheNextTimeTheSnapshotIsBuilt()
    {
        // The builder reads the version's current words. The snapshot itself, once handed to a caller and
        // frozen elsewhere (Orders, at confirmation), never sees a later rename — this only proves the
        // builder is not caching a stale label from an earlier call against the same version.
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var sleeve = Group(version, blouse, "sleeve_style").Value;
        var full = Option(version, sleeve.Id, "FULL").Value;
        var service = Service(version, blouse, "STITCHING", [sleeve.Id]);
        version.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var before = GarmentDesignSnapshotBuilder.From(
            version, blouse, service.Id, [new DesignSelectionInput("sleeve_style", ["FULL"])], [], null).Value;
        before.Selections[0].GroupLabel.ShouldBe("sleeve style");
        before.Selections[0].OptionLabel.ShouldBe("FULL");

        version.CorrectDesignGroupPresentation(
                sleeve.Id, new DesignGroupPresentation("Sleeve length", null, 0), "Clearer label.",
                CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();
        version.CorrectDesignOptionPresentation(
                full.Id,
                new DesignOptionPresentation(
                    "Full sleeve", null, full.HelpText, full.IllustrationAlt, full.DisplayOrder),
                "Clearer label.", CatalogTestData.Now, null)
            .IsSuccess.ShouldBeTrue();

        var after = GarmentDesignSnapshotBuilder.From(
            version, blouse, service.Id, [new DesignSelectionInput("sleeve_style", ["FULL"])], [], null).Value;
        after.Selections[0].GroupLabel.ShouldBe("Sleeve length");
        after.Selections[0].OptionLabel.ShouldBe("Full sleeve");
    }

    [Fact]
    public void ASnapshotBuiltBeforeAnUnrelatedRepublishIsByteForByteIdenticalToOneBuiltAfterwards()
    {
        // The acceptance criterion, read literally: "serialised as JSON". Object equality is not the
        // right tool here — a record's collection-typed fields compare by reference, not by content — so
        // the two builds are compared the way the value object itself is described: as JSON.
        var version = CatalogTestData.Draft();
        var blouse = Category(version, "BLOUSE_PATTERN");
        var sleeve = Group(version, blouse, "sleeve_style").Value;
        Option(version, sleeve.Id, "FULL");
        var service = Service(version, blouse, "STITCHING", [sleeve.Id]);
        version.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var selections = new[] { new DesignSelectionInput("sleeve_style", ["FULL"]) };

        var before = GarmentDesignSnapshotBuilder.From(
            version, blouse, service.Id, selections, ["A note."], "Instructions.").Value;

        // A republish is modelled as an entirely new version — the pinned CatalogVersion instance and
        // everything reachable from it never changes, because a published version is immutable but for
        // the four presentation columns this test does not touch.
        var republished = version.CloneAsDraft(
            new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;
        republished.Publish(CatalogTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        var after = GarmentDesignSnapshotBuilder.From(
            version, blouse, service.Id, selections, ["A note."], "Instructions.").Value;

        Serialized(before).ShouldBe(Serialized(after));
    }

    private static string Serialized(GarmentDesignSnapshotResult snapshot) => JsonSerializer.Serialize(snapshot);

    private static Guid Category(CatalogVersion version, string code)
        => version.AddCategory(
                CatalogTestData.Id($"cat-{code}"), CatalogTestData.Id($"cat-key-{code}"), null,
                CatalogTestData.CategoryOf(code), CatalogTestData.Now, null)
            .Value.Id;

    private static Tailor360.Platform.Abstractions.Results.Result<DesignOptionGroup> Group(
        CatalogVersion version, Guid categoryId, string code, int displayOrder = 0)
        => version.AddDesignGroup(
            CatalogTestData.Id($"group-{categoryId}-{code}"),
            CatalogTestData.Id($"group-key-{categoryId}-{code}"),
            categoryId,
            new DesignGroupDetails(
                code, code.Replace('_', ' '), null, DesignSelectionMode.MultipleChoice, false, displayOrder,
                null, null, [CatalogTestData.MainBranch]),
            CatalogTestData.Now,
            null);

    private static Tailor360.Platform.Abstractions.Results.Result<DesignOption> Option(
        CatalogVersion version,
        Guid groupId,
        string code,
        int displayOrder = 0,
        string? illustrationKey = null,
        string? priceListItemCode = null)
        => version.AddDesignOption(
            CatalogTestData.Id($"option-{groupId}-{code}"),
            CatalogTestData.Id($"option-key-{groupId}-{code}"),
            groupId,
            new DesignOptionDetails(
                code, code, null, "What the choice means for the garment.", illustrationKey,
                "The shape in words.", priceListItemCode, 0, displayOrder, true),
            CatalogTestData.Now,
            null);

    private static ServiceType Service(CatalogVersion version, Guid categoryId, string code, IReadOnlyList<Guid> groupIds)
        => version.AddServiceType(
                CatalogTestData.Id($"svc-{categoryId}-{code}"), CatalogTestData.Id($"svc-key-{categoryId}-{code}"),
                categoryId, CatalogTestData.ServiceOf(code) with { DesignOptionGroupIds = groupIds },
                CatalogTestData.Now, null)
            .Value;
}
