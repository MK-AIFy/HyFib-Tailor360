using Shouldly;
using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The per-field rules: what a band does with a value, what a precision may be, and what a field must carry.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MeasurementFieldRulesTests
{
    private static readonly FieldKey Key = FieldKey.Create("chest_bust").Value;

    [Theory]
    [InlineData(549, BandVerdict.Refused, "below the hard bound")]
    [InlineData(1501, BandVerdict.Refused, "above the hard bound")]
    [InlineData(550, BandVerdict.NeedsConfirmation, "on the hard bound but under the warning")]
    [InlineData(709, BandVerdict.NeedsConfirmation, "unusually small")]
    [InlineData(1271, BandVerdict.NeedsConfirmation, "unusually large")]
    [InlineData(710, BandVerdict.Inside, "on the lower warning threshold")]
    [InlineData(1000, BandVerdict.Inside, "ordinary")]
    [InlineData(1270, BandVerdict.Inside, "on the upper warning threshold")]
    public void TheBandsRefuseTheImpossibleAndOnlyQueryTheUnusual(
        int millimetres,
        BandVerdict expected,
        string because)
    {
        // The distinction is the point of section 7: hard bounds exclude the impossible and nothing else, and the
        // confirmation band never blocks. A system that refused an unusual but real customer would teach staff to
        // type a lie, which is worse than any measurement it could have caught.
        var bands = new ValidationBands(550m, 1500m, 710m, 1270m);

        bands.Judge(millimetres).ShouldBe(expected, because);
    }

    [Fact]
    public void AFieldWithNoBandsJudgesNothing()
    {
        // A choice field has no numeric band to be inside or outside of.
        ValidationBands.None.AreDeclared.ShouldBeFalse();
        ValidationBands.None.Judge(0m).ShouldBe(BandVerdict.Inside);
        ValidationBands.None.Judge(999_999m).ShouldBe(BandVerdict.Inside);
        ValidationBands.None.Validate(Key).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ABandMayWarnOnOneSideOnly()
    {
        var lowOnly = new ValidationBands(100m, 900m, 200m, null);

        lowOnly.Judge(150m).ShouldBe(BandVerdict.NeedsConfirmation);
        lowOnly.Judge(880m).ShouldBe(BandVerdict.Inside, "nothing was said about the upper end");
    }

    [Fact]
    public void AWarningBandInTheWrongOrderIsRefused()
    {
        new ValidationBands(100m, 900m, 800m, 200m).Validate(Key)
            .Error.Code.ShouldBe("measurements.warning-band-out-of-order");
    }

    [Theory]
    [InlineData(3, "measurements.precision-not-permitted")]
    [InlineData(10, "measurements.precision-not-permitted")]
    [InlineData(32, "measurements.precision-not-permitted")]
    public void AnInchStepMustBeOneATapeIsDividedInto(int denominator, string code)
    {
        new FieldPrecision(denominator, 1).Validate().Error.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void TheHalvingStepsAreAccepted(int denominator)
    {
        new FieldPrecision(denominator, 1).Validate().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ACentimetrePrecisionFinerThanATapeIsRefused()
    {
        new FieldPrecision(8, 3).Validate().Error.Code.ShouldBe("measurements.decimals-not-permitted");
        new FieldPrecision(8, -1).Validate().Error.Code.ShouldBe("measurements.decimals-not-permitted");
    }

    [Fact]
    public void APrecisionSaysWhichUnitsAFieldIsShownIn()
    {
        FieldPrecision.Eighths.Supports(DisplayUnit.Inch).ShouldBeTrue();
        FieldPrecision.Eighths.Supports(DisplayUnit.Centimetre).ShouldBeTrue();

        new FieldPrecision(0, 1).Supports(DisplayUnit.Inch).ShouldBeFalse("no inch step is declared");
        new FieldPrecision(8, 0).Supports(DisplayUnit.Centimetre).ShouldBeFalse();

        FieldPrecision.Whole.Supports(DisplayUnit.Count).ShouldBeTrue();
        FieldPrecision.Whole.Supports(DisplayUnit.Inch).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("Chest_Bust")]
    [InlineData("chest bust")]
    [InlineData("1_chest")]
    [InlineData("chest-bust")]
    public void AKeyThatIsNotLowerSnakeCaseIsRefused(string candidate)
    {
        FieldKey.IsWellFormed(candidate).ShouldBeFalse();
    }

    [Theory]
    [InlineData("chest_bust")]
    [InlineData("apex_to_apex")]
    [InlineData("kali_count")]
    [InlineData("aari_front_work_height")]
    public void AKeyFromTheSharedDictionaryIsAccepted(string candidate)
    {
        FieldKey.IsWellFormed(candidate).ShouldBeTrue();
    }

    [Fact]
    public void AKeyLongerThanItsColumnIsRefused()
    {
        FieldKey.Create(new string('a', FieldKey.MaximumLength + 1))
            .Error.Code.ShouldBe("measurements.field-key-malformed");
    }

    [Theory]
    [InlineData("elastic")]
    [InlineData("EL-ASTIC")]
    [InlineData("")]
    public void AChoiceCodeThatCannotBeStoredIsRefused(string code)
    {
        new ChoiceOption(code, "Elastic", null, 0).Validate(Key)
            .Error.Code.ShouldBe("measurements.choice-code-malformed");
    }

    [Theory]
    [InlineData("ELASTIC")]
    [InlineData("BOTH")]
    [InlineData("0_1")]
    [InlineData("12_14")]
    public void AChoiceCodeFromTheSeededSetIsAccepted(string code)
    {
        // The age bands lead with a digit, which is why the code pattern does not insist on a letter first.
        new ChoiceOption(code, code, null, 0).Validate(Key).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AChoiceOptionNeedsALabelAndANonNegativeOrder()
    {
        new ChoiceOption("ELASTIC", "  ", null, 0).Validate(Key)
            .Error.Code.ShouldBe("measurements.value-required");
        new ChoiceOption("ELASTIC", "Elastic", null, -1).Validate(Key)
            .Error.Code.ShouldBe("measurements.display-order-negative");
    }

    [Fact]
    public void AMeasuredFieldCarriesNoChoices()
    {
        var (_, draft) = MeasurementTestData.WithDraft();
        var withOptions = MeasurementTestData.Field("waist") with
        {
            Options = [new ChoiceOption("ELASTIC", "Elastic", null, 0)],
        };

        draft.AddField(MeasurementTestData.Id("f"), withOptions, MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.numeric-field-has-options");
    }

    [Fact]
    public void ACountIsWholeAndCarriesNoStep()
    {
        var (_, draft) = MeasurementTestData.WithDraft();
        var counted = MeasurementTestData.Field("kali_count") with
        {
            CanonicalUnit = CanonicalUnit.Count,
            Precision = FieldPrecision.Eighths,
        };

        draft.AddField(MeasurementTestData.Id("f"), counted, MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.precision-not-allowed-for-unit");
    }

    [Fact]
    public void AFieldNeedsALabelAGroupAndHelpText()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        Refused(draft, MeasurementTestData.Field("waist") with { Label = " " })
            .ShouldBe("measurements.value-required");
        Refused(draft, MeasurementTestData.Field("waist") with { GroupName = " " })
            .ShouldBe("measurements.value-required");
        Refused(draft, MeasurementTestData.Field("waist") with { HelpText = " " })
            .ShouldBe("measurements.value-required");
        Refused(draft, MeasurementTestData.Field("waist") with { DisplayOrder = -1 })
            .ShouldBe("measurements.display-order-negative");
    }

    [Fact]
    public void AValueLongerThanItsColumnIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        Refused(
                draft,
                MeasurementTestData.Field("waist") with
                {
                    Label = new string('x', TemplateField.MaximumLabelLength + 1),
                })
            .ShouldBe("measurements.value-too-long");
        Refused(
                draft,
                MeasurementTestData.Field("waist") with
                {
                    HelpText = new string('x', TemplateField.MaximumHelpTextLength + 1),
                })
            .ShouldBe("measurements.value-too-long");
    }

    [Fact]
    public void ATemplateCodeThatIsNotUpperSnakeCaseIsRefused()
    {
        Create("mt_blouse").Error.Code.ShouldBe("measurements.template-code-malformed");
        Create("MT BLOUSE").Error.Code.ShouldBe("measurements.template-code-malformed");
        Create("1_BLOUSE").Error.Code.ShouldBe("measurements.template-code-malformed");
        Create("MT_BLOUSE_PATTERN").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ATemplateNeedsANameThatFitsItsColumn()
    {
        MeasurementTemplate.Create(
                MeasurementTestData.Id("t"), MeasurementTestData.OrganisationId, "MT_X", "  ", null,
                MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.value-required");

        MeasurementTemplate.Create(
                MeasurementTestData.Id("t"), MeasurementTestData.OrganisationId, "MT_X",
                new string('x', MeasurementTemplate.MaximumNameLength + 1), null,
                MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.value-too-long");
    }

    [Fact]
    public void AVersionNameAndItsNotesMustFitTheirColumns()
    {
        var template = MeasurementTestData.Template();
        var ids = new MeasurementTestData.CountingIds("names");

        template.StartDraft(ids, "  ", null, DisplayUnit.Inch, null, MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.value-required");
        template.StartDraft(
                ids, new string('x', TemplateVersion.MaximumNameLength + 1), null, DisplayUnit.Inch, null,
                MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.value-too-long");
        template.StartDraft(
                ids, "Version 1", new string('x', TemplateVersion.MaximumNotesLength + 1), DisplayUnit.Inch, null,
                MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.value-too-long");
    }

    [Fact]
    public void ATemplateDoesNotOpenInAUnitNobodyEntersLengthsIn()
    {
        MeasurementTestData.Template()
            .StartDraft(
                new MeasurementTestData.CountingIds("unit"), "Version 1", null, DisplayUnit.Count, null,
                MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.default-unit-not-enterable");
    }

    [Fact]
    public void CloningAVersionThatIsNotThereIsRefused()
    {
        MeasurementTestData.Template()
            .StartDraft(
                new MeasurementTestData.CountingIds("clone"), "Version 2", null, DisplayUnit.Inch,
                MeasurementTestData.Id("nowhere"), MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.version-not-found");
    }

    [Fact]
    public void EditingOrRemovingAFieldThatIsNotThereIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.EditField(
                MeasurementTestData.Id("nowhere"), MeasurementTestData.Field("waist"),
                MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.field-not-found");
        draft.RemoveField(MeasurementTestData.Id("nowhere"), MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.field-not-found");
    }

    [Fact]
    public void ARemovedFieldLeavesTheDraft()
    {
        var (_, draft) = MeasurementTestData.WithDraft();
        var field = draft.AddField(
            MeasurementTestData.Id("f"), MeasurementTestData.Field("waist"),
            MeasurementTestData.Now, null).Value;

        draft.RemoveField(field.Id, MeasurementTestData.Now, null).IsSuccess.ShouldBeTrue();
        draft.Fields.ShouldBeEmpty();
    }

    [Fact]
    public void TheGroupsComeBackInTheOrderATailorMeasures()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        Add(draft, MeasurementTestData.Field("apex_to_apex") with { GroupName = "Shaping", DisplayOrder = 2 });
        Add(draft, MeasurementTestData.Field("chest_bust") with { GroupName = "Bodice", DisplayOrder = 0 });
        Add(draft, MeasurementTestData.Field("waist") with { GroupName = "Bodice", DisplayOrder = 1 });

        var groups = draft.InGroupOrder();

        groups.Count.ShouldBe(2);
        groups[0].Key.ShouldBe("Bodice");
        groups[0].Select(field => field.Key.Value).ShouldBe(["chest_bust", "waist"]);
        groups[1].Key.ShouldBe("Shaping");
    }

    private static void Add(TemplateVersion draft, TemplateFieldDefinition definition)
        => draft.AddField(
                MeasurementTestData.Id($"f-{definition.Key}"), definition, MeasurementTestData.Now, null)
            .IsSuccess.ShouldBeTrue();

    private static string Refused(TemplateVersion draft, TemplateFieldDefinition definition)
        => draft.AddField(MeasurementTestData.Id("f"), definition, MeasurementTestData.Now, null).Error.Code;

    private static Tailor360.Platform.Abstractions.Results.Result<MeasurementTemplate> Create(string code)
        => MeasurementTemplate.Create(
            MeasurementTestData.Id(code), MeasurementTestData.OrganisationId, code, "Name", null,
            MeasurementTestData.Now, null);
}
