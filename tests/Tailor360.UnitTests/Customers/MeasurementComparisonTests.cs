using Shouldly;
using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// What changed between two of a customer's measurements (issue #122).
/// </summary>
/// <remarks>
/// The comparison exists for one moment: somebody is about to reuse last year's numbers, and has to see what is
/// different before they do. So the tests below are mostly about the cases that make a naive comparison useless —
/// a template version change, a renamed field, a unit that differs without the measurement differing.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MeasurementComparisonTests
{
    [Fact]
    public void Says_nothing_changed_when_nothing_did()
    {
        var before = Measured(("chest_bust", 914.4m), ("waist", 711.2m));
        var after = Measured(("chest_bust", 914.4m), ("waist", 711.2m));

        var differences = MeasurementComparison.Compare(before, after).Value;

        differences.Select(one => one.Change)
            .ShouldAllBe(change => change == MeasurementChange.Unchanged);
    }

    [Fact]
    public void Names_the_field_whose_measurement_moved()
    {
        var before = Measured(("chest_bust", 914.4m), ("waist", 711.2m));
        var after = Measured(("chest_bust", 927.1m), ("waist", 711.2m));

        var differences = MeasurementComparison.Compare(before, after).Value;

        var changed = differences.Single(one => one.Change == MeasurementChange.Changed);

        changed.Key.ShouldBe("chest_bust");
        changed.Before!.Millimetres.ShouldBe(914.4m);
        changed.After!.Millimetres.ShouldBe(927.1m);
    }

    [Fact]
    public void Reads_the_same_measurement_taken_in_a_different_unit_as_unchanged()
    {
        // A chest recorded as 36 in and the same chest recorded as 91.44 cm is one measurement. Reporting it as a
        // change would send a reviewer looking for a difference that is not there — the unit is a fact about the
        // taking, not about the garment.
        var before = Measured(("chest_bust", 914.4m));
        var after = new[]
        {
            MeasurementValue.Measured(
                FieldKey.Create("chest_bust").Value, 914.4m, DisplayUnit.Centimetre),
        };

        var differences = MeasurementComparison.Compare(before, Version(after)).Value;

        differences.ShouldHaveSingleItem().Change.ShouldBe(MeasurementChange.Unchanged);
    }

    [Fact]
    public void Reports_a_field_the_newer_template_version_no_longer_asks_for_as_dropped()
    {
        // Not omitted. A field measured last time and not offered now is exactly what a reviewer has to be told
        // about before they reuse; a row that simply disappeared reads as an oversight.
        var before = Measured(("chest_bust", 914.4m), ("shoulder", 381m));
        var after = Measured(("chest_bust", 914.4m));

        var differences = MeasurementComparison.Compare(before, after).Value;

        var dropped = differences.Single(one => one.Change == MeasurementChange.Dropped);

        dropped.Key.ShouldBe("shoulder");
        dropped.Before.ShouldNotBeNull();
        dropped.After.ShouldBeNull();
    }

    [Fact]
    public void Reports_a_field_the_newer_template_version_asks_for_as_added()
    {
        var before = Measured(("chest_bust", 914.4m));
        var after = Measured(("chest_bust", 914.4m), ("sleeve_length", 254m));

        var differences = MeasurementComparison.Compare(before, after).Value;

        var added = differences.Single(one => one.Change == MeasurementChange.Added);

        added.Key.ShouldBe("sleeve_length");
        added.Before.ShouldBeNull();
        added.After.ShouldNotBeNull();
    }

    [Fact]
    public void Reads_a_renamed_field_as_one_dropped_and_one_added()
    {
        // Which is what a rename *is* once values are filed under a key: the old values stay under the old key
        // forever and the new key has none. A single "renamed" row would imply the numbers moved across.
        var before = Measured(("chest", 914.4m));
        var after = Measured(("chest_bust", 914.4m));

        var differences = MeasurementComparison.Compare(before, after).Value;

        differences.Select(one => (one.Key, one.Change)).ShouldBe(
            [("chest", MeasurementChange.Dropped), ("chest_bust", MeasurementChange.Added)],
            ignoreOrder: true);
    }

    [Fact]
    public void Matches_on_the_key_rather_than_the_field_identity()
    {
        // The case the comparison exists for. Two measurements taken against different template versions share no
        // field identity at all, because a version mints new identifiers for every field it carries. Matching on
        // identity would report every field as dropped and re-added — the opposite of useful.
        var before = Measured(("chest_bust", 914.4m), ("waist", 711.2m));
        var after = Version(
            [
                MeasurementValue.Measured(FieldKey.Create("chest_bust").Value, 914.4m, DisplayUnit.Inch),
                MeasurementValue.Measured(FieldKey.Create("waist").Value, 711.2m, DisplayUnit.Inch),
            ],
            templateVersionId: MeasurementTestData.Id("a-different-template-version"));

        var differences = MeasurementComparison.Compare(before, after).Value;

        differences.Count.ShouldBe(2);
        differences.Select(one => one.Change)
            .ShouldAllBe(change => change == MeasurementChange.Unchanged);
    }

    [Fact]
    public void Refuses_two_measurements_of_different_templates()
    {
        var before = Measured(("chest_bust", 914.4m));
        var after = Version(
            [MeasurementValue.Measured(FieldKey.Create("chest_bust").Value, 914.4m, DisplayUnit.Inch)],
            templateId: MeasurementTestData.Id("a-different-template"));

        var result = MeasurementComparison.Compare(before, after);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("measurements.comparison-subjects-do-not-match");
    }

    [Fact]
    public void Refuses_two_measurements_of_different_customers()
    {
        // Worse than a template mismatch and easier to do by accident from a list of identifiers.
        var before = Measured(("chest_bust", 914.4m));
        var after = Version(
            [MeasurementValue.Measured(FieldKey.Create("chest_bust").Value, 914.4m, DisplayUnit.Inch)],
            customerId: MeasurementTestData.Id("somebody-else"));

        MeasurementComparison.Compare(before, after).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Reads_a_changed_choice_as_a_change()
    {
        var before = Version([MeasurementValue.Chosen(FieldKey.Create("sleeve_style").Value, "PUFF")]);
        var after = Version([MeasurementValue.Chosen(FieldKey.Create("sleeve_style").Value, "PLAIN")]);

        MeasurementComparison.Compare(before, after).Value
            .ShouldHaveSingleItem().Change.ShouldBe(MeasurementChange.Changed);
    }

    [Fact]
    public void Orders_by_key_so_the_comparison_reads_the_same_whichever_side_it_came_from()
    {
        // The two sides may have been taken against template versions whose display orders disagree, so ordering
        // by either one's would flip rows depending on which the comparison happened to read from.
        var before = Measured(("waist", 711.2m), ("chest_bust", 914.4m));
        var after = Measured(("chest_bust", 914.4m), ("sleeve_length", 254m));

        MeasurementComparison.Compare(before, after).Value.Select(one => one.Key)
            .ShouldBe(["chest_bust", "sleeve_length", "waist"]);
    }

    /// <summary>
    /// Distinguishes the versions one test builds.
    /// </summary>
    /// <remarks>
    /// A counter rather than a fresh identifier each time, because the fixture's contract is that a failure names
    /// the same thing on a re-run. Two versions sharing an identity would be a fixture bug the real generator
    /// cannot have.
    /// </remarks>
    private static int _versions;

    private static MeasurementVersion Measured(params (string Key, decimal Millimetres)[] values)
        => Version(
            [
                .. values.Select(value => MeasurementValue.Measured(
                    FieldKey.Create(value.Key).Value, value.Millimetres, DisplayUnit.Inch)),
            ]);

    /// <summary>A confirmed measurement carrying the given values, for one synthetic customer.</summary>
    private static MeasurementVersion Version(
        IReadOnlyCollection<MeasurementValue> values,
        Guid? customerId = null,
        Guid? templateId = null,
        Guid? templateVersionId = null)
    {
        var draft = MeasurementDraft.Start(
            MeasurementTestData.Id("draft"),
            MeasurementTestData.OrganisationId,
            MeasurementTestData.Id("branch"),
            customerId ?? MeasurementTestData.Id("customer"),
            PublishedVersion(templateId, templateVersionId),
            null,
            MeasurementTestData.Now,
            TimeSpan.FromDays(2),
            null).Value;

        return MeasurementVersion.Of(
            MeasurementTestData.Id($"version-{Interlocked.Increment(ref _versions)}"),
            draft,
            1,
            values,
            null,
            null,
            MeasurementTestData.Now,
            null);
    }

    /// <summary>
    /// A published template version, used only for the identifiers a draft pins.
    /// </summary>
    /// <remarks>
    /// The comparison never reads the template — it matches on keys — so this needs to carry identity and a
    /// published status and nothing else. Building a full field set here would suggest the comparison consults
    /// one, and the whole point is that it does not.
    /// </remarks>
    private static TemplateVersion PublishedVersion(Guid? templateId, Guid? templateVersionId)
    {
        var (template, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
            MeasurementTestData.Id("field"),
            MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Now,
            null).IsSuccess.ShouldBeTrue();

        draft.Submit(MeasurementTestData.Now, null).IsSuccess.ShouldBeTrue();
        draft.Approve(MeasurementTestData.Now, null, soleAdministrator: true).IsSuccess.ShouldBeTrue();
        template.PublishVersion(draft.Id, MeasurementTestData.Now, null, "A synthetic publication.")
            .IsSuccess.ShouldBeTrue();

        // The draft a measurement is built from reads `MeasurementTemplateId` and `Id` off the version, so
        // overriding them is how two measurements are made to look like different templates or versions.
        if (templateId is { } template_)
        {
            Overwrite(draft, nameof(TemplateVersion.MeasurementTemplateId), template_);
        }

        if (templateVersionId is { } version)
        {
            Overwrite(draft, nameof(TemplateVersion.Id), version);
        }

        return draft;
    }

    private static void Overwrite(TemplateVersion version, string property, Guid value)
        => typeof(TemplateVersion).GetProperty(property)!.SetValue(version, value);
}
