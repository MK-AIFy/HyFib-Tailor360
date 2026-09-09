using Shouldly;
using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The publish-time checks of <c>docs/prd/measurement-templates.md</c> section 6.
/// </summary>
/// <remarks>
/// One test per finding, because each exists to stop a different way a published template can be wrong — and a
/// published template cannot be corrected in place, only superseded, with every measurement captured meanwhile
/// carrying the mistake.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TemplateValidationTests
{
    [Fact]
    public void AVersionWithNoFieldsIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        var findings = TemplateValidation.Validate(draft);

        findings.ShouldHaveSingleItem().Code.ShouldBe("version-has-no-fields");
        TemplateValidation.HasErrors(findings).ShouldBeTrue();
    }

    [Fact]
    public void ARuleReadingAFieldThatIsNotThereIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();
        var rule = new ConditionalRule(
            RuleEffect.HiddenWhen,
            [new RuleClause(RuleScope.Field, "has_sleeves", RuleOperator.IsAnyOf, ["NO"])]);

        Add(draft, MeasurementTestData.Field("sleeve_length", rule: rule));

        var findings = TemplateValidation.Validate(draft);

        findings.ShouldContain(finding => finding.Code == "unknown-rule-field");
        TemplateValidation.HasErrors(findings).ShouldBeTrue();
    }

    [Fact]
    public void RulesThatDependOnEachOtherInALoopAreRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        Add(draft, MeasurementTestData.Field("field_a", rule: Reads("field_b")));
        Add(draft, MeasurementTestData.Field("field_b", rule: Reads("field_c")));
        Add(draft, MeasurementTestData.Field("field_c", rule: Reads("field_a")));

        var findings = TemplateValidation.Validate(draft);
        var cycle = findings.Single(finding => finding.Code == "cyclic-condition");

        cycle.Message.ShouldContain("field_a");
        cycle.Message.ShouldContain("field_b");
        cycle.Message.ShouldContain("field_c");
    }

    [Fact]
    public void ARuleThatReadsItsOwnFieldIsRefusedWithItsOwnMessage()
    {
        // A one-node loop reads as a typo, so it gets a message that says so rather than being reported as a cycle.
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
                MeasurementTestData.Id("field_a"),
                MeasurementTestData.Field("field_a", rule: Reads("field_a")),
                MeasurementTestData.Now,
                null)
            .Error.Code.ShouldBe("measurements.rule-reads-itself");
    }

    [Fact]
    public void ARequiredFieldHiddenByARuleThatAlwaysMatchesIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();
        var always = new ConditionalRule(RuleEffect.HiddenWhen, []);

        Add(draft, MeasurementTestData.Field("sleeve_length", required: true, rule: always));

        var findings = TemplateValidation.Validate(draft);

        findings.ShouldContain(finding => finding.Code == "required-field-never-shown");
        TemplateValidation.HasErrors(findings).ShouldBeTrue();
    }

    [Fact]
    public void AnOptionalFieldHiddenByARuleThatAlwaysMatchesIsAllowed()
    {
        // Not an error: an administrator turning a field off for now, without deleting it, is a reasonable thing
        // to do and loses nothing, because an optional hidden field stores no value either way.
        var (_, draft) = MeasurementTestData.WithDraft();

        Add(draft, MeasurementTestData.Field("sleeve_length", required: false,
            rule: new ConditionalRule(RuleEffect.HiddenWhen, [])));

        TemplateValidation.Validate(draft)
            .ShouldNotContain(finding => finding.Code == "required-field-never-shown");
    }

    [Fact]
    public void AWarningBandOutsideTheHardBoundsIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        // Rejected on the way in, which is where an administrator sees it soonest.
        draft.AddField(
                MeasurementTestData.Id("f"),
                MeasurementTestData.Field("waist", bands: new ValidationBands(450m, 1500m, 100m, 1200m)),
                MeasurementTestData.Now,
                null)
            .Error.Code.ShouldBe("measurements.warning-outside-bounds");
    }

    [Fact]
    public void AMinimumAboveItsMaximumIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
                MeasurementTestData.Id("f"),
                MeasurementTestData.Field("waist", bands: new ValidationBands(1500m, 450m, null, null)),
                MeasurementTestData.Now,
                null)
            .Error.Code.ShouldBe("measurements.bounds-out-of-order");
    }

    [Fact]
    public void AChoiceFieldWithNothingToChooseIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
                MeasurementTestData.Id("f"),
                MeasurementTestData.Choice("waist_finish"),
                MeasurementTestData.Now,
                null)
            .Error.Code.ShouldBe("measurements.choice-field-has-no-options");
    }

    [Fact]
    public void ALengthWithNoStepInEitherUnitIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
                MeasurementTestData.Id("f"),
                MeasurementTestData.Field("waist") with { Precision = FieldPrecision.Whole },
                MeasurementTestData.Now,
                null)
            .Error.Code.ShouldBe("measurements.precision-missing");
    }

    [Fact]
    public void AnInchStepThatIsNotOnATapeIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
                MeasurementTestData.Id("f"),
                MeasurementTestData.Field("waist") with { Precision = new FieldPrecision(10, 1) },
                MeasurementTestData.Now,
                null)
            .Error.Code.ShouldBe("measurements.precision-not-permitted");
    }

    [Fact]
    public void ADiagramWithoutAlternativeTextIsRefused()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
                MeasurementTestData.Id("f"),
                MeasurementTestData.Field("waist") with { DiagramAlt = null },
                MeasurementTestData.Now,
                null)
            .Error.Code.ShouldBe("measurements.diagram-alt-missing");
    }

    [Fact]
    public void AFieldWithNoDiagramOrTamilLabelIsWarnedAboutAndStillPublishable()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        Add(draft, MeasurementTestData.Field("waist") with { DiagramKey = null, DiagramAlt = null });

        var findings = TemplateValidation.Validate(draft);

        findings.ShouldContain(finding => finding.Code == "field-has-no-diagram");
        findings.ShouldContain(finding => finding.Code == "field-has-no-tamil-label");
        findings.ShouldAllBe(finding => finding.Severity == TemplateFindingSeverity.Warning);
        TemplateValidation.HasErrors(findings).ShouldBeFalse("neither stands between a shop and a template");
    }

    [Fact]
    public void AWellFormedVersionReportsNoErrors()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        Add(draft, MeasurementTestData.Field("chest_bust") with { LabelTamil = "மார்பு" });
        Add(draft, MeasurementTestData.Field("waist") with { LabelTamil = "இடை" });

        TemplateValidation.HasErrors(TemplateValidation.Validate(draft)).ShouldBeFalse();
    }

    private static void Add(TemplateVersion draft, TemplateFieldDefinition definition)
        => draft.AddField(
            MeasurementTestData.Id($"field-{definition.Key}"), definition, MeasurementTestData.Now, null)
            .IsSuccess.ShouldBeTrue($"'{definition.Key}' should have been accepted");

    private static ConditionalRule Reads(string key)
        => new(RuleEffect.HiddenWhen, [new RuleClause(RuleScope.Field, key, RuleOperator.IsAnyOf, ["NO"])]);
}
