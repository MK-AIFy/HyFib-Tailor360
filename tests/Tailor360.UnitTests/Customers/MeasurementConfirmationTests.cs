using Shouldly;
using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// INV-MSR-04: every value is checked against the template version at confirmation.
/// </summary>
/// <remarks>
/// <para>
/// The rule this is really about is <em>when</em>. A draft accepts an implausible shoulder because a half-measured
/// garment is a normal state and refusing one while somebody is holding a tape teaches them to type a plausible
/// lie and correct it later — which is exactly what this record exists not to contain. So everything below is the
/// confirmation, and the draft's own tests are about what it does with a field it has never heard of.
/// </para>
/// <para>
/// Every finding, not the first: a tailor wants to be told about all four fields at once rather than press
/// Confirm four times.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MeasurementConfirmationTests
{
    [Fact]
    public void Accepts_a_measurement_inside_every_band()
    {
        var version = Published(MeasurementTestData.Field("chest_bust"));

        var findings = MeasurementConfirmation.Check(version, [Inches("chest_bust", 36m)]);

        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Refuses_a_value_outside_the_hard_bounds()
    {
        // Hard bounds are deliberately wide and catch the impossible — a centimetre value typed into an inch
        // field, a three-metre shoulder. They are not the accuracy check.
        var version = Published(MeasurementTestData.Field("chest_bust"));

        var findings = MeasurementConfirmation.Check(
            version, [MeasurementValue.Measured(FieldKey.Create("chest_bust").Value, 4000m, DisplayUnit.Inch)]);

        findings.ShouldHaveSingleItem().Code.ShouldBe("measurements.value-out-of-bounds");
    }

    [Fact]
    public void Accepts_an_unusual_value_only_once_somebody_has_said_they_meant_it()
    {
        // The confirmation band never refuses: an unusual customer is still a customer, and a system that refused
        // to record them teaches staff to type a lie. What it does refuse is a confirmation nobody was asked for.
        var version = Published(
            MeasurementTestData.Field("chest_bust", bands: new ValidationBands(100m, 2000m, 500m, 1000m)));

        var unacknowledged = MeasurementConfirmation.Check(
            version, [MeasurementValue.Measured(FieldKey.Create("chest_bust").Value, 1500m, DisplayUnit.Centimetre)]);

        unacknowledged.ShouldHaveSingleItem().Code.ShouldBe("measurements.value-needs-acknowledgement");

        var acknowledged = MeasurementConfirmation.Check(
            version,
            [
                MeasurementValue.Measured(
                    FieldKey.Create("chest_bust").Value, 1500m, DisplayUnit.Centimetre, acknowledged: true),
            ]);

        acknowledged.ShouldBeEmpty();
    }

    [Fact]
    public void Refuses_a_value_that_is_not_on_the_field_own_step()
    {
        // A tape reads to an eighth of an inch. A value between two steps was not read off one, so it is a typing
        // or a unit mistake — and either is worth catching before it becomes evidence.
        var version = Published(MeasurementTestData.Field("chest_bust"));

        var findings = MeasurementConfirmation.Check(
            version,
            [MeasurementValue.Measured(FieldKey.Create("chest_bust").Value, 914.4m + 1.2m, DisplayUnit.Inch)]);

        findings.ShouldHaveSingleItem().Code.ShouldBe("measurements.value-off-step");
    }

    [Fact]
    public void Names_a_required_field_that_was_shown_and_not_answered()
    {
        var version = Published(
            MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Field("waist"));

        var findings = MeasurementConfirmation.Check(version, [Inches("chest_bust", 36m)]);

        findings.ShouldHaveSingleItem().Code.ShouldBe("measurements.value-missing");
    }

    [Fact]
    public void Says_nothing_about_an_optional_field_nobody_measured()
    {
        var version = Published(
            MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Field("waist", required: false));

        MeasurementConfirmation.Check(version, [Inches("chest_bust", 36m)]).ShouldBeEmpty();
    }

    [Fact]
    public void Says_nothing_about_a_required_field_the_rule_hides()
    {
        // A field that is not asked for cannot be missing. Reporting it would make the wizard demand a measurement
        // it never showed, which is the one failure a conditional field has.
        var version = Published(
            MeasurementTestData.Choice("sleeve_style", "PUFF", "PLAIN"),
            MeasurementTestData.Field(
                "sleeve_length",
                rule: new ConditionalRule(
                    RuleEffect.ShownWhen,
                    [new RuleClause(RuleScope.Field, "sleeve_style", RuleOperator.IsAnyOf, ["PUFF"])])));

        var findings = MeasurementConfirmation.Check(
            version, [MeasurementValue.Chosen(FieldKey.Create("sleeve_style").Value, "PLAIN")]);

        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Demands_a_required_field_the_rule_shows()
    {
        var version = Published(
            MeasurementTestData.Choice("sleeve_style", "PUFF", "PLAIN"),
            MeasurementTestData.Field(
                "sleeve_length",
                rule: new ConditionalRule(
                    RuleEffect.ShownWhen,
                    [new RuleClause(RuleScope.Field, "sleeve_style", RuleOperator.IsAnyOf, ["PUFF"])])));

        var findings = MeasurementConfirmation.Check(
            version, [MeasurementValue.Chosen(FieldKey.Create("sleeve_style").Value, "PUFF")]);

        findings.ShouldHaveSingleItem().Target.ShouldBe("sleeve_length");
    }

    [Fact]
    public void Refuses_an_option_the_version_does_not_offer()
    {
        var version = Published(MeasurementTestData.Choice("sleeve_style", "PUFF", "PLAIN"));

        var findings = MeasurementConfirmation.Check(
            version, [MeasurementValue.Chosen(FieldKey.Create("sleeve_style").Value, "BELL")]);

        findings.ShouldHaveSingleItem().Code.ShouldBe("measurements.choice-not-offered");
    }

    [Fact]
    public void Refuses_a_number_for_a_field_that_is_chosen_and_a_choice_for_one_that_is_measured()
    {
        var version = Published(
            MeasurementTestData.Choice("sleeve_style", "PUFF"),
            MeasurementTestData.Field("chest_bust"));

        var findings = MeasurementConfirmation.Check(
            version,
            [
                MeasurementValue.Measured(FieldKey.Create("sleeve_style").Value, 100m, DisplayUnit.Centimetre),
                MeasurementValue.Chosen(FieldKey.Create("chest_bust").Value, "PUFF"),
            ]);

        findings.Select(finding => finding.Code).ShouldBe(
            ["measurements.value-is-not-a-choice", "measurements.value-is-not-measured"],
            ignoreOrder: true);
    }

    [Fact]
    public void Stops_at_a_field_the_version_does_not_have_rather_than_concluding_anything_else()
    {
        // The draft and the version have diverged — a field was removed since measuring began. Nothing else
        // concluded from them is trustworthy: a rule may read a field that no longer exists, so "required" and
        // "hidden" both stop meaning anything.
        var version = Published(MeasurementTestData.Field("chest_bust"), MeasurementTestData.Field("waist"));

        var findings = MeasurementConfirmation.Check(version, [Inches("shoulder", 15m)]);

        findings.ShouldHaveSingleItem().Code.ShouldBe("measurements.unknown-field");
    }

    [Fact]
    public void Reports_every_field_that_is_wrong_rather_than_the_first()
    {
        // A tailor holding a tape wants to be told about all of them at once, not to press Confirm four times.
        var version = Published(
            MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Field("waist"),
            MeasurementTestData.Field("shoulder"));

        var findings = MeasurementConfirmation.Check(
            version,
            [MeasurementValue.Measured(FieldKey.Create("chest_bust").Value, 9000m, DisplayUnit.Centimetre)]);

        findings.Count.ShouldBe(3);
        findings.Select(finding => finding.Target).ShouldBe(
            ["chest_bust", "waist", "shoulder"], ignoreOrder: true);
    }

    [Fact]
    public void Names_a_field_answered_twice()
    {
        var version = Published(MeasurementTestData.Field("chest_bust"));

        var findings = MeasurementConfirmation.Check(
            version, [Inches("chest_bust", 36m), Inches("chest_bust", 38m)]);

        findings.ShouldContain(finding => finding.Code == "measurements.duplicate-value");
    }

    private static MeasurementValue Inches(string key, decimal inches)
        => MeasurementValue.Measured(
            FieldKey.Create(key).Value, UnitConversion.ToMillimetres(inches, DisplayUnit.Inch), DisplayUnit.Inch);

    /// <summary>A published version carrying the given fields.</summary>
    private static TemplateVersion Published(params TemplateFieldDefinition[] fields)
    {
        var (template, draft) = MeasurementTestData.WithDraft();
        var ids = new MeasurementTestData.CountingIds("confirmation");

        foreach (var field in fields)
        {
            draft.AddField(ids.NewId(), field, MeasurementTestData.Now, null).IsSuccess.ShouldBeTrue();
        }

        draft.Submit(MeasurementTestData.Now, null).IsSuccess.ShouldBeTrue();
        draft.Approve(MeasurementTestData.Now, null, soleAdministrator: true).IsSuccess.ShouldBeTrue();
        template.PublishVersion(draft.Id, MeasurementTestData.Now, null, "A synthetic publication.").IsSuccess
            .ShouldBeTrue();

        return draft;
    }
}
