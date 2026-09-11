using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The design copy a garment job is confirmed against (INV-JOB-01).
/// </summary>
/// <remarks>
/// <para>
/// It embeds everything the job card and its printed fallback need without a catalogue lookup — codes
/// <em>and</em> labels, both display orders, the option version, the price-list item and the
/// illustration with its alternative text (<c>docs/prd/design-options.md</c> section 7). The reason is
/// stated there: a reprint a year later may find the group retired or reordered, and it still has to
/// render the garment that was agreed.
/// </para>
/// <para>
/// Codes are what a price list, a report and an export refer to; labels are what people read. Renaming
/// an option is a label change that must break nothing, including a job card printed before the rename.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class DesignSnapshotTests
{
    /* One selection ----------------------------------------------------------------------------- */

    [Fact]
    public void ASelectionCarriesBothCodesBothLabelsAndBothDisplayOrders()
    {
        var selection = Selection();

        selection.IsSuccess.ShouldBeTrue();
        selection.Value.GroupCode.ShouldBe("neckline");
        selection.Value.GroupLabel.ShouldBe("Neckline");
        selection.Value.GroupDisplayOrder.ShouldBe(1);
        selection.Value.OptionCode.ShouldBe("round");
        selection.Value.OptionLabel.ShouldBe("Round");
        selection.Value.OptionDisplayOrder.ShouldBe(2);
        selection.Value.OptionVersion.ShouldBe(3);
        selection.Value.PriceListItemCode.ShouldBe("PL-NECK-ROUND");
    }

    [Theory]
    [InlineData("groupCode")]
    [InlineData("groupLabel")]
    [InlineData("optionCode")]
    [InlineData("optionLabel")]
    public void ASelectionMissingACodeOrALabelIsRefused(string field)
    {
        // A code with no label prints an internal key on a job card; a label with no code cannot be
        // priced, reported or exported. Both halves are carried or the selection is refused.
        var selection = Selection(
            groupCode: field == "groupCode" ? "  " : "neckline",
            groupLabel: field == "groupLabel" ? null : "Neckline",
            optionCode: field == "optionCode" ? "" : "round",
            optionLabel: field == "optionLabel" ? "   " : "Round");

        selection.IsFailure.ShouldBeTrue();
        selection.Error.Code.ShouldBe("orders.value-required");
        selection.Error.Target.ShouldBe(field);
    }

    [Theory]
    [InlineData("groupCode", DesignSelection.MaximumCodeLength)]
    [InlineData("groupLabel", DesignSelection.MaximumLabelLength)]
    [InlineData("optionCode", DesignSelection.MaximumCodeLength)]
    [InlineData("optionLabel", DesignSelection.MaximumLabelLength)]
    public void ASelectionLongerThanTheColumnHoldsIsRefusedRatherThanTruncated(string field, int maximum)
    {
        var tooLong = new string('x', maximum + 1);

        var selection = Selection(
            groupCode: field == "groupCode" ? tooLong : "neckline",
            groupLabel: field == "groupLabel" ? tooLong : "Neckline",
            optionCode: field == "optionCode" ? tooLong : "round",
            optionLabel: field == "optionLabel" ? tooLong : "Round");

        selection.IsFailure.ShouldBeTrue();
        selection.Error.Code.ShouldBe("orders.value-too-long");
        selection.Error.Target.ShouldBe(field);
    }

    /// <summary>
    /// Option versions are numbered from one, so a zero is an integer nobody filled in. A revision that
    /// cannot say which drawing of the option was agreed cannot be compared against the next one.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void ASelectionThatCannotSayWhichDrawingWasAgreedIsRefused(int optionVersion)
    {
        var selection = Selection(optionVersion: optionVersion);

        selection.IsFailure.ShouldBeTrue();
        selection.Error.Code.ShouldBe("orders.value-required");
        selection.Error.Target.ShouldBe("optionVersion");
    }

    [Fact]
    public void ASelectionThatCostsNothingExtraCarriesNoPriceListItem()
    {
        var selection = Selection(priceListItemCode: "   ");

        selection.IsSuccess.ShouldBeTrue();
        selection.Value.PriceListItemCode.ShouldBeNull();
    }

    [Fact]
    public void APriceListItemCodeLongerThanTheColumnHoldsIsRefused()
    {
        var selection = Selection(
            priceListItemCode: new string('x', DesignSelection.MaximumCodeLength + 1));

        selection.IsFailure.ShouldBeTrue();
        selection.Error.Code.ShouldBe("orders.value-too-long");
        selection.Error.Target.ShouldBe("priceListItemCode");
    }

    /// <summary>
    /// Alternative text is mandatory on every illustration (<c>docs/prd/design-options.md</c>
    /// section 7): a snapshot carrying a picture nobody can read is a job card that does not work for
    /// the person holding it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnIllustrationWithoutAlternativeTextIsRefused(string? alternativeText)
    {
        var selection = Selection(
            illustrationMediaId: OrdersTestData.Id("illustration"),
            illustrationAlternativeText: alternativeText);

        selection.IsFailure.ShouldBeTrue();
        selection.Error.Code.ShouldBe("orders.illustration-alternative-text-required");
    }

    [Fact]
    public void AnIllustrationIsKeptWithTheTextThatDescribesIt()
    {
        var selection = Selection(
            illustrationMediaId: OrdersTestData.Id("illustration"),
            illustrationAlternativeText: " A rounded neckline with a narrow facing. ");

        selection.IsSuccess.ShouldBeTrue();
        selection.Value.IllustrationMediaId.ShouldBe(OrdersTestData.Id("illustration"));
        selection.Value.IllustrationAlternativeText.ShouldBe("A rounded neckline with a narrow facing.");
    }

    /// <summary>
    /// An empty identifier is no illustration rather than a broken one, so the alternative-text rule asks
    /// about a picture that is really there — and text describing a picture nobody has is dropped rather
    /// than printed under nothing.
    /// </summary>
    [Fact]
    public void AnEmptyIllustrationIsNoIllustrationAndItsDescriptionGoesWithIt()
    {
        var selection = Selection(
            illustrationMediaId: Guid.Empty,
            illustrationAlternativeText: "A rounded neckline.");

        selection.IsSuccess.ShouldBeTrue();
        selection.Value.IllustrationMediaId.ShouldBeNull();
        selection.Value.IllustrationAlternativeText.ShouldBeNull();
    }

    [Fact]
    public void AlternativeTextLongerThanTheColumnHoldsIsRefusedRatherThanTruncated()
    {
        var selection = Selection(
            illustrationMediaId: OrdersTestData.Id("illustration"),
            illustrationAlternativeText: new string(
                'x', DesignSelection.MaximumAlternativeTextLength + 1));

        selection.IsFailure.ShouldBeTrue();
        selection.Error.Code.ShouldBe("orders.value-too-long");
        selection.Error.Target.ShouldBe("illustrationAlternativeText");
    }

    /* The snapshot ------------------------------------------------------------------------------ */

    [Fact]
    public void ASnapshotRecordsTheCatalogueVersionTheCategoryAndTheServiceType()
    {
        var snapshot = Snapshot();

        snapshot.IsSuccess.ShouldBeTrue();
        snapshot.Value.CatalogVersionId.ShouldBe(OrdersTestData.CatalogVersion);
        snapshot.Value.CategoryKey.ShouldBe("blouse");
        snapshot.Value.CategoryLabel.ShouldBe("Blouse");
        snapshot.Value.ServiceTypeKey.ShouldBe("stitch-new");
        snapshot.Value.ServiceTypeLabel.ShouldBe("Stitch a new garment");
        snapshot.Value.FrozenAt.ShouldBe(OrdersTestData.Now);
    }

    /// <summary>
    /// INV-ORD-02: a figure or a garment that cannot be recomputed from its own snapshot is a defect, and
    /// a missing catalogue version is what would make it one.
    /// </summary>
    [Fact]
    public void ASnapshotThatCannotSayWhichCatalogueItWasTakenFromIsRefused()
    {
        var snapshot = Snapshot(catalogVersionId: Guid.Empty);

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.configuration-version-missing");
        snapshot.Error.Target.ShouldBe("catalogVersionId");
    }

    [Theory]
    [InlineData("categoryKey")]
    [InlineData("categoryLabel")]
    [InlineData("serviceTypeKey")]
    [InlineData("serviceTypeLabel")]
    public void ASnapshotMissingACategoryOrServiceTypeIsRefused(string field)
    {
        var snapshot = Snapshot(
            categoryKey: field == "categoryKey" ? null : "blouse",
            categoryLabel: field == "categoryLabel" ? "  " : "Blouse",
            serviceTypeKey: field == "serviceTypeKey" ? "" : "stitch-new",
            serviceTypeLabel: field == "serviceTypeLabel" ? null : "Stitch a new garment");

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.value-required");
        snapshot.Error.Target.ShouldBe(field);
    }

    [Fact]
    public void TwoSelectionsAnsweringTheSameGroupAreRefusedAndTheGroupIsNamed()
    {
        // One question, two answers: which neckline is the garment being cut to?
        var snapshot = Snapshot(
            selections:
            [
                Selection().Value,
                Selection(groupCode: "sleeve", groupLabel: "Sleeve", optionCode: "cap").Value,
                Selection(optionCode: "square").Value,
            ]);

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.duplicate-design-group");
        snapshot.Error.Target.ShouldBe("neckline");
    }

    /// <summary>
    /// A hole in the list is answered with a refusal rather than with a <c>NullReferenceException</c>
    /// thrown from inside the factory — a <c>Create</c> that returns a <c>Result</c> returns one for
    /// everything it was handed.
    /// </summary>
    [Fact]
    public void AHoleInTheSelectionsIsRefusedRatherThanThrownFromInsideTheFactory()
    {
        var snapshot = Snapshot(selections: [Selection().Value, null!]);

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.value-required");
        snapshot.Error.Target.ShouldBe("selections");
    }

    [Fact]
    public void ASnapshotOfAGarmentThatChoseNothingCarriesNoSelections()
    {
        // Which groups must be answered is a design rule, and design rules are Catalog's to enforce
        // against the published version before confirmation. This type copies what was chosen; it does
        // not re-derive a decision another module has already made.
        var snapshot = Snapshot(selections: []);

        snapshot.IsSuccess.ShouldBeTrue();
        snapshot.Value.Selections.ShouldBeEmpty();
    }

    /// <summary>
    /// Two rules can attach the same standing instruction — "press on the reverse" hangs off both the
    /// fabric and the lining — and printing it twice makes the card look like it is asking for two
    /// different things.
    /// </summary>
    [Fact]
    public void AStandingInstructionAttachedTwiceIsPrintedOnce()
    {
        var snapshot = Snapshot(
            conditionalNotes: ["Press on the reverse.", "  ", "Press on the reverse.", "Use a cool iron."]);

        snapshot.IsSuccess.ShouldBeTrue();
        snapshot.Value.ConditionalNotes.ShouldBe(["Press on the reverse.", "Use a cool iron."]);
    }

    [Fact]
    public void ANoteLongerThanTheColumnHoldsIsRefusedRatherThanTruncated()
    {
        var snapshot = Snapshot(
            conditionalNotes: [new string('x', DesignSnapshot.MaximumNoteLength + 1)]);

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.value-too-long");
        snapshot.Error.Target.ShouldBe("conditionalNotes");
    }

    [Fact]
    public void InstructionsLongerThanTheColumnHoldsAreRefusedRatherThanTruncated()
    {
        var snapshot = Snapshot(
            garmentInstructions: new string('x', DesignSnapshot.MaximumInstructionsLength + 1));

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.value-too-long");
        snapshot.Error.Target.ShouldBe("garmentInstructions");
    }

    [Fact]
    public void BlankInstructionsAreNoInstructionsRatherThanAnEmptyLineOnTheCard()
    {
        var snapshot = Snapshot(garmentInstructions: "   ");

        snapshot.IsSuccess.ShouldBeTrue();
        snapshot.Value.GarmentInstructions.ShouldBeNull();
    }

    /// <summary>
    /// INV-JOB-01. The snapshot is a copy the moment it is taken, so a list the caller goes on to edit is
    /// not the copy the garment was confirmed against.
    /// </summary>
    [Fact]
    public void TheSelectionsAreCopiedSoTheSnapshotCannotBeEditedThroughTheListItWasBuiltFrom()
    {
        var selections = new List<DesignSelection> { Selection().Value };
        var snapshot = Snapshot(selections: selections).Value;

        selections.Add(Selection(groupCode: "sleeve", groupLabel: "Sleeve", optionCode: "cap").Value);

        snapshot.Selections.ShouldHaveSingleItem().GroupCode.ShouldBe("neckline");
    }

    /* Shape ------------------------------------------------------------------------------------- */

    /// <summary>
    /// The factories are the only ways in. A positional record would publish a constructor and an
    /// <c>init</c> setter would reopen the <c>with</c> expression, either of which could put an
    /// illustration back without its alternative text or drop a label the printed fallback depends on.
    /// </summary>
    [Fact]
    public void ASnapshotCannotBeConstructedOrRewrittenAroundItsFactory()
    {
        foreach (var type in new[] { typeof(DesignSnapshot), typeof(DesignSelection) })
        {
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ShouldAllBe(property => property.SetMethod == null);
        }
    }

    /* Helpers ----------------------------------------------------------------------------------- */

    private static Result<DesignSelection> Selection(
        string? groupCode = "neckline",
        string? groupLabel = "Neckline",
        string? optionCode = "round",
        string? optionLabel = "Round",
        int optionVersion = 3,
        string? priceListItemCode = "PL-NECK-ROUND",
        Guid? illustrationMediaId = null,
        string? illustrationAlternativeText = null)
        => DesignSelection.Create(
            groupCode,
            groupLabel,
            groupDisplayOrder: 1,
            optionCode,
            optionLabel,
            optionDisplayOrder: 2,
            optionVersion,
            priceListItemCode,
            illustrationMediaId,
            illustrationAlternativeText);

    private static Result<DesignSnapshot> Snapshot(
        Guid? catalogVersionId = null,
        string? categoryKey = "blouse",
        string? categoryLabel = "Blouse",
        string? serviceTypeKey = "stitch-new",
        string? serviceTypeLabel = "Stitch a new garment",
        IReadOnlyCollection<DesignSelection>? selections = null,
        string? garmentInstructions = null,
        IReadOnlyCollection<string>? conditionalNotes = null)
        => DesignSnapshot.Create(
            catalogVersionId ?? OrdersTestData.CatalogVersion,
            categoryKey,
            categoryLabel,
            serviceTypeKey,
            serviceTypeLabel,
            selections ?? [Selection().Value],
            garmentInstructions,
            conditionalNotes,
            OrdersTestData.Now);
}
