using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The measurement copy a garment job is confirmed against (INV-JOB-01).
/// </summary>
/// <remarks>
/// <para>
/// A copy and not a reference: capturing a newer measurement version for the customer never changes a
/// confirmed garment, because the garment was cut to the reading that was taken at the counter. The
/// only write path is an order revision, and that closes the moment any garment leaves confirmed
/// (INV-ORD-05).
/// </para>
/// <para>
/// Nothing here is logged, messaged or put in an identifier: a measurement is sensitive personal data
/// (<c>docs/nfr/data-classification.md</c> section 5.2), so a refusal names the field key and never the
/// reading. The values in these tests are synthetic and belong to nobody.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MeasurementSnapshotTests
{
    /* One measured value ------------------------------------------------------------------------ */

    [Fact]
    public void AReadingIsHeldInMillimetresBesideTheUnitItWasEnteredIn()
    {
        // The reading is stored once, in one unit, so a report never adds centimetres to inches; the
        // unit the tailor typed is kept beside it so the job card reads back the way it was written.
        var value = MeasuredValue.Create("chest", 860m, "cm", choice: null, acknowledged: false);

        value.IsSuccess.ShouldBeTrue();
        value.Value.Key.ShouldBe("chest");
        value.Value.Millimetres.ShouldBe(860m);
        value.Value.EnteredUnit.ShouldBe("cm");
        value.Value.IsChoice.ShouldBeFalse();
    }

    /// <summary>
    /// A choice carries no unit, which is what Customers' own <c>MeasurementValue.Chosen</c> says in as
    /// many words. A unit offered alongside one is dropped rather than frozen onto the garment, so a job
    /// card never prints "regular, in centimetres" — and INV-JOB-01 would have printed it for ever.
    /// </summary>
    [Fact]
    public void AChosenOptionIsAMeasurementFieldTooAndCarriesNoUnit()
    {
        var value = MeasuredValue.Create("fit", millimetres: null, "cm", "regular", acknowledged: false);

        value.IsSuccess.ShouldBeTrue();
        value.Value.Choice.ShouldBe("regular");
        value.Value.IsChoice.ShouldBeTrue();
        value.Value.Millimetres.ShouldBeNull();
        value.Value.EnteredUnit.ShouldBeNull();
    }

    /// <summary>
    /// A reading of zero or below is not a length. The snapshot is immutable from confirmation
    /// (INV-JOB-01), so one frozen onto a garment job is printed on the job card for ever with no
    /// correction path short of cancelling the order — which is why it is refused at the counter.
    /// </summary>
    [Theory]
    [InlineData(-500)]
    [InlineData(-1)]
    [InlineData(0)]
    public void AReadingThatIsNotALengthIsRefused(int millimetres)
    {
        var value = MeasuredValue.Create("chest", millimetres, "cm", choice: null, acknowledged: false);

        value.IsFailure.ShouldBeTrue();
        value.Error.Code.ShouldBe("orders.measurement-not-positive");
        value.Error.Target.ShouldBe("chest");
    }

    /// <summary>
    /// The upper end is deliberately not bounded: what counts as unusual is a band on the template
    /// version Customers owns, and the person in front of the tailor is the authority on their own
    /// measurements.
    /// </summary>
    [Fact]
    public void AVeryLargeReadingIsNotRefusedBecauseTheRangeIsNotThisModulesToJudge()
    {
        var value = MeasuredValue.Create("chest", 5_000m, "cm", choice: null, acknowledged: true);

        value.IsSuccess.ShouldBeTrue();
        value.Value.Millimetres.ShouldBe(5_000m);
    }

    /// <summary>
    /// A field holds either a reading or a chosen option, and exactly one of the two. Both is a screen
    /// sending two answers to one question; neither is a row nobody can cut a garment from.
    /// </summary>
    [Theory]
    [InlineData(860, "regular")]
    [InlineData(null, null)]
    public void AFieldThatHoldsBothOrNeitherIsRefused(int? millimetres, string? choice)
    {
        var value = MeasuredValue.Create("chest", millimetres, "cm", choice, acknowledged: false);

        value.IsFailure.ShouldBeTrue();
        value.Error.Code.ShouldBe("orders.measured-value-ambiguous");
        value.Error.Target.ShouldBe("chest");
    }

    [Fact]
    public void ABlankChoiceIsNoChoiceRatherThanAnEmptyAnswer()
    {
        var value = MeasuredValue.Create("chest", 860m, "cm", "   ", acknowledged: false);

        value.IsSuccess.ShouldBeTrue();
        value.Value.Choice.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AValueWithoutAFieldKeyIsRefused(string? key)
    {
        var value = MeasuredValue.Create(key, 860m, "cm", choice: null, acknowledged: false);

        value.IsFailure.ShouldBeTrue();
        value.Error.Code.ShouldBe("orders.value-required");
        value.Error.Target.ShouldBe("key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AValueWithoutTheUnitItWasEnteredInIsRefused(string? unit)
    {
        // Without the unit the reading is a number nobody can read back, and the job card cannot print
        // what the tailor actually wrote down.
        var value = MeasuredValue.Create("chest", 860m, unit, choice: null, acknowledged: false);

        value.IsFailure.ShouldBeTrue();
        value.Error.Code.ShouldBe("orders.value-required");
        value.Error.Target.ShouldBe("enteredUnit");
    }

    [Theory]
    [InlineData("key")]
    [InlineData("enteredUnit")]
    [InlineData("choice")]
    public void AValueLongerThanTheColumnHoldsIsRefusedRatherThanTruncated(string field)
    {
        var value = MeasuredValue.Create(
            field == "key" ? new string('x', MeasuredValue.MaximumKeyLength + 1) : "chest",
            field == "choice" ? null : 860m,
            field == "enteredUnit" ? new string('x', MeasuredValue.MaximumUnitLength + 1) : "cm",
            field == "choice" ? new string('x', MeasuredValue.MaximumChoiceLength + 1) : null,
            acknowledged: false);

        value.IsFailure.ShouldBeTrue();
        value.Error.Code.ShouldBe("orders.value-too-long");
        value.Error.Target.ShouldBe(field);
    }

    [Fact]
    public void TheKeyTheUnitAndTheChoiceAreTrimmedSoOneStraySpaceIsNotASecondField()
    {
        var chosen = MeasuredValue.Create(" fit ", millimetres: null, " cm ", " regular ", acknowledged: false);

        chosen.IsSuccess.ShouldBeTrue();
        chosen.Value.Key.ShouldBe("fit");
        chosen.Value.Choice.ShouldBe("regular");
        chosen.Value.EnteredUnit.ShouldBeNull();

        var measured = MeasuredValue.Create(" chest ", 860m, " cm ", choice: null, acknowledged: false);

        measured.IsSuccess.ShouldBeTrue();
        measured.Value.Key.ShouldBe("chest");
        measured.Value.EnteredUnit.ShouldBe("cm");
    }

    /// <summary>
    /// A reading outside the template's usual range is acknowledged rather than refused, because the
    /// person in front of the tailor is the authority on their own measurements.
    /// </summary>
    [Fact]
    public void AnAcknowledgedOutOfRangeReadingIsCarriedAsAcknowledged()
    {
        var value = MeasuredValue.Create("chest", 1_800m, "cm", choice: null, acknowledged: true);

        value.IsSuccess.ShouldBeTrue();
        value.Value.Acknowledged.ShouldBeTrue();
    }

    /* The snapshot ------------------------------------------------------------------------------ */

    [Fact]
    public void ASnapshotRecordsTheTemplateTheVersionAndWhenItWasTakenAndFrozen()
    {
        var snapshot = Snapshot();

        snapshot.IsSuccess.ShouldBeTrue();
        snapshot.Value.MeasurementTemplateId.ShouldBe(OrdersTestData.Id("measurement-template"));
        snapshot.Value.TemplateVersionId.ShouldBe(OrdersTestData.Id("measurement-template-version"));
        snapshot.Value.VersionNumber.ShouldBe(2);
        snapshot.Value.TakenAt.ShouldBe(OrdersTestData.Now.AddDays(-1));
        snapshot.Value.TakenBy.ShouldBe(OrdersTestData.Actor);
        snapshot.Value.FrozenAt.ShouldBe(OrdersTestData.Now);
    }

    [Theory]
    [InlineData("measurementTemplateId")]
    [InlineData("templateVersionId")]
    public void ASnapshotThatCannotSayWhichTemplateItAnswersIsRefused(string field)
    {
        var snapshot = MeasurementSnapshot.Create(
            OrdersTestData.Id("measurement-version"),
            field == "measurementTemplateId" ? Guid.Empty : OrdersTestData.Id("measurement-template"),
            field == "templateVersionId" ? Guid.Empty : OrdersTestData.Id("measurement-template-version"),
            versionNumber: 2,
            OrdersTestData.Now.AddDays(-1),
            OrdersTestData.Actor,
            [OrdersTestData.Measurements().Values[0]],
            OrdersTestData.Now);

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.value-required");
        snapshot.Error.Target.ShouldBe(field);
    }

    /// <summary>
    /// Measurement versions are numbered from one, so a zero is the default of an integer nobody filled
    /// in rather than a real version — and a job card headed "version 0" helps nobody.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ASnapshotOfAVersionThatWasNeverNumberedIsRefused(int versionNumber)
    {
        var snapshot = Snapshot(versionNumber: versionNumber);

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.value-required");
        snapshot.Error.Target.ShouldBe("versionNumber");
    }

    /// <summary>
    /// Two rows answering one field is a copy nobody can read back: which of the two is the measurement
    /// the garment was cut to?
    /// </summary>
    [Fact]
    public void TwoValuesAnsweringTheSameFieldAreRefusedAndTheFieldIsNamed()
    {
        var snapshot = Snapshot(
            values:
            [
                Value("chest", 860m),
                Value("waist", 740m),
                Value("chest", 880m),
            ]);

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.duplicate-measurement-key");
        snapshot.Error.Target.ShouldBe("chest");
    }

    /// <summary>
    /// A hole in the list is a caller defect, but a factory that returns a <c>Result</c> answers it with a
    /// refusal rather than with a <c>NullReferenceException</c> thrown from inside itself — which is what
    /// dereferencing the element's key used to do, and what <c>Order.Confirm</c> already does for a null
    /// garment.
    /// </summary>
    [Fact]
    public void AHoleInTheValuesIsRefusedRatherThanThrownFromInsideTheFactory()
    {
        var snapshot = Snapshot(values: [Value("chest", 860m), null!]);

        snapshot.IsFailure.ShouldBeTrue();
        snapshot.Error.Code.ShouldBe("orders.value-required");
        snapshot.Error.Target.ShouldBe("values");
    }

    /// <summary>
    /// INV-JOB-01. The snapshot is a copy the moment it is taken, so a list the caller goes on to edit is
    /// not the copy the garment was confirmed against.
    /// </summary>
    [Fact]
    public void TheValuesAreCopiedSoTheSnapshotCannotBeEditedThroughTheListItWasBuiltFrom()
    {
        var values = new List<MeasuredValue> { Value("chest", 860m) };
        var snapshot = Snapshot(values: values).Value;

        values.Add(Value("waist", 740m));

        snapshot.Values.ShouldHaveSingleItem().Key.ShouldBe("chest");
    }

    /// <summary>
    /// A measurement taken and frozen in the same breath has no stored version to point back at, so the
    /// provenance is optional while the template it answers is not.
    /// </summary>
    [Fact]
    public void ASnapshotWithNoStoredVersionBehindItIsStillASnapshot()
    {
        var snapshot = MeasurementSnapshot.Create(
            measurementVersionId: null,
            OrdersTestData.Id("measurement-template"),
            OrdersTestData.Id("measurement-template-version"),
            versionNumber: 1,
            OrdersTestData.Now.AddDays(-1),
            OrdersTestData.Actor,
            [Value("chest", 860m)],
            OrdersTestData.Now);

        snapshot.IsSuccess.ShouldBeTrue();
        snapshot.Value.MeasurementVersionId.ShouldBeNull();
    }

    /* Shape ------------------------------------------------------------------------------------- */

    /// <summary>
    /// <see cref="MeasurementSnapshot.Create"/> and <see cref="MeasuredValue.Create"/> are the only ways
    /// in. A positional record would publish a constructor and an <c>init</c> setter would reopen the
    /// <c>with</c> expression, and either would be a second route past the validation above — one that
    /// could store a reading with no unit, or two rows answering one field.
    /// </summary>
    [Fact]
    public void ASnapshotCannotBeConstructedOrRewrittenAroundItsFactory()
    {
        foreach (var type in new[] { typeof(MeasurementSnapshot), typeof(MeasuredValue) })
        {
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ShouldAllBe(property => property.SetMethod == null);
        }
    }

    /* Helpers ----------------------------------------------------------------------------------- */

    private static MeasuredValue Value(string key, decimal millimetres)
        => MeasuredValue.Create(key, millimetres, "cm", choice: null, acknowledged: false).Value;

    private static Result<MeasurementSnapshot> Snapshot(
        int versionNumber = 2,
        IReadOnlyCollection<MeasuredValue>? values = null)
        => MeasurementSnapshot.Create(
            OrdersTestData.Id("measurement-version"),
            OrdersTestData.Id("measurement-template"),
            OrdersTestData.Id("measurement-template-version"),
            versionNumber,
            OrdersTestData.Now.AddDays(-1),
            OrdersTestData.Actor,
            values ?? [Value("chest", 860m)],
            OrdersTestData.Now);
}
