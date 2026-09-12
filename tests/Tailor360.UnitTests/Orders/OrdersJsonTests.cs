using System.Text.Json;
using Shouldly;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Modules.Orders.Infrastructure.Persistence;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// How the four document columns of the <c>orders</c> schema are written and read.
/// </summary>
/// <remarks>
/// <para>
/// A measurement snapshot's values, a design snapshot's selections and its conditional notes, and a garment job's
/// ready-state blocks are documents rather than rows. Each is only ever read and written whole, and the first
/// three have a harder reason besides: <see cref="MeasurementSnapshot"/> and <see cref="DesignSnapshot"/> take
/// their collection as a constructor parameter, so an owned collection mapped to a child table could not
/// materialise them at all.
/// </para>
/// <para>
/// <strong>The property under test is that a row comes back as something the Domain agrees is well formed.</strong>
/// Every value object here has a private constructor and get-only properties, and nothing in
/// <c>OrdersJson</c> deserialises a Domain type directly — each document is a private record that puts the value
/// back through the Domain's own factory. A round trip is therefore the assertion that matters: a materialisation
/// that quietly rebuilt a value the factory would have refused is a value nothing downstream can trust.
/// </para>
/// <para>
/// The second property is the storage format itself. A predicate is written as its <em>name</em>, so reordering
/// <see cref="ReadyGatePredicate"/> cannot silently re-explain every block already stored — the reason
/// <c>customers.duplicate_candidates.reasons</c> gives for the same choice. These assertions read the JSON text
/// rather than only round-tripping it, because a round trip passes just as well when both sides agree on the
/// wrong format.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OrdersJsonTests
{
    /* Measured values ---------------------------------------------------------------------------- */

    /// <summary>
    /// A reading and a chosen option are the two shapes <c>MeasuredValue.Create</c> allows — both or neither, and
    /// never both — so a round trip has to preserve which of the two a value was.
    /// </summary>
    [Fact]
    public void AMeasuredReadingAndAChosenOptionBothSurviveTheRoundTrip()
    {
        var reading = MeasuredValue.Create("chest", 1_015m, "cm", null, acknowledged: false).Value;
        var choice = MeasuredValue.Create("fit", null, null, "regular", acknowledged: true).Value;

        var read = OrdersJson.ReadValues(OrdersJson.Write([reading, choice]));

        read.Count.ShouldBe(2);

        read[0].Key.ShouldBe("chest");
        read[0].Millimetres.ShouldBe(1_015m);
        read[0].EnteredUnit.ShouldBe("cm");
        read[0].Choice.ShouldBeNull();
        read[0].Acknowledged.ShouldBeFalse();

        read[1].Key.ShouldBe("fit");
        read[1].Millimetres.ShouldBeNull();

        // A chosen option was not entered in anything: "regular" is not centimetres.
        read[1].EnteredUnit.ShouldBeNull();
        read[1].Choice.ShouldBe("regular");
        read[1].Acknowledged.ShouldBeTrue();
    }

    /// <summary>
    /// A millimetre reading is a <see cref="decimal"/> and must come back to the same figure. Storing money or a
    /// measurement through a binary float is the defect this asserts the absence of.
    /// </summary>
    [Fact]
    public void AReadingKeepsItsExactDecimalValue()
    {
        var value = MeasuredValue.Create("waist", 812.5m, "cm", null, acknowledged: false).Value;

        var read = OrdersJson.ReadValues(OrdersJson.Write([value]));

        read[0].Millimetres.ShouldBe(812.5m);
        read[0].Millimetres!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture).ShouldBe("812.5");
    }

    /* Design selections -------------------------------------------------------------------------- */

    [Fact]
    public void ADesignSelectionSurvivesTheRoundTripWithEveryMemberIntact()
    {
        var illustration = Guid.Parse("019299aa-0000-7000-8000-0000000000ff");
        var selection = DesignSelection.Create(
            "collar",
            "Collar",
            2,
            "mandarin",
            "Mandarin",
            5,
            7,
            "PLI-COLLAR-MANDARIN",
            illustration,
            "A mandarin collar").Value;

        var read = OrdersJson.ReadSelections(OrdersJson.Write([selection]));

        read.Count.ShouldBe(1);
        read[0].GroupCode.ShouldBe("collar");
        read[0].GroupLabel.ShouldBe("Collar");
        read[0].GroupDisplayOrder.ShouldBe(2);
        read[0].OptionCode.ShouldBe("mandarin");
        read[0].OptionLabel.ShouldBe("Mandarin");
        read[0].OptionDisplayOrder.ShouldBe(5);
        read[0].OptionVersion.ShouldBe(7);
        read[0].PriceListItemCode.ShouldBe("PLI-COLLAR-MANDARIN");
        read[0].IllustrationMediaId.ShouldBe(illustration);
        read[0].IllustrationAlternativeText.ShouldBe("A mandarin collar");
    }

    /// <summary>
    /// The three optional members are optional in the column too, and a selection that had none must not come
    /// back carrying empty strings where it carried nothing.
    /// </summary>
    [Fact]
    public void ASelectionWithNoPriceItemAndNoIllustrationComesBackWithNoneOfThem()
    {
        var selection = DesignSelection.Create(
            "cuff", "Cuff", 1, "single", "Single", 1, 1, null, null, null).Value;

        var read = OrdersJson.ReadSelections(OrdersJson.Write([selection]));

        read[0].PriceListItemCode.ShouldBeNull();
        read[0].IllustrationMediaId.ShouldBeNull();
        read[0].IllustrationAlternativeText.ShouldBeNull();
    }

    /* Conditional notes -------------------------------------------------------------------------- */

    [Fact]
    public void ConditionalNotesSurviveTheRoundTripInOrder()
    {
        IReadOnlyList<string> notes = ["Cuff to be finished narrow", "Confirm hem before pressing"];

        var read = OrdersJson.ReadNotes(OrdersJson.Write(notes));

        read.ShouldBe(notes);
    }

    /* Ready-state blocks ------------------------------------------------------------------------- */

    [Fact]
    public void AReadyStateBlockSurvivesTheRoundTripWithItsReference()
    {
        var block = ReadyGateBlock.Create(ReadyGatePredicate.DependenciesMet, "GJ-2026-0001").Value;

        var read = OrdersJson.ReadBlocks(OrdersJson.Write([block]));

        read.Count.ShouldBe(1);
        read.Single().Predicate.ShouldBe(ReadyGatePredicate.DependenciesMet);
        read.Single().Reference.ShouldBe("GJ-2026-0001");
    }

    /// <summary>
    /// A predicate that names nothing blocks just the same, and the screen shows the reason code alone. The
    /// column has to carry that rather than refusing to render.
    /// </summary>
    [Fact]
    public void ABlockThatNamesNothingSurvivesTheRoundTrip()
    {
        var block = ReadyGateBlock.Create(ReadyGatePredicate.QcPassed, null).Value;

        var read = OrdersJson.ReadBlocks(OrdersJson.Write([block]));

        read.Single().Predicate.ShouldBe(ReadyGatePredicate.QcPassed);
        read.Single().Reference.ShouldBeNull();
    }

    /// <summary>
    /// Every one of the six predicates section 9.1 names, so a member added to the enum without a thought for
    /// storage is caught here rather than by a screen showing the wrong reason.
    /// </summary>
    [Theory]
    [InlineData(ReadyGatePredicate.WorkflowComplete)]
    [InlineData(ReadyGatePredicate.QcPassed)]
    [InlineData(ReadyGatePredicate.DocumentationComplete)]
    [InlineData(ReadyGatePredicate.NoOpenHold)]
    [InlineData(ReadyGatePredicate.DependenciesMet)]
    [InlineData(ReadyGatePredicate.CustodyReconciled)]
    public void EveryPredicateSurvivesTheRoundTrip(ReadyGatePredicate predicate)
    {
        var block = ReadyGateBlock.Create(predicate, null).Value;

        OrdersJson.ReadBlocks(OrdersJson.Write([block])).Single().Predicate.ShouldBe(predicate);
    }

    /// <summary>
    /// <strong>The format assertion, not a round trip.</strong> A predicate is written as its name, so that
    /// reordering <see cref="ReadyGatePredicate"/> cannot silently re-explain every block already stored. An
    /// ordinal would round-trip perfectly today and mean something else after the first insertion into the enum.
    /// </summary>
    [Fact]
    public void APredicateIsWrittenAsItsNameAndNeverAsItsOrdinal()
    {
        var block = ReadyGateBlock.Create(ReadyGatePredicate.CustodyReconciled, null).Value;

        var json = OrdersJson.Write([block]);

        json.ShouldContain("CustodyReconciled");
        json.ShouldNotContain("\"predicate\":5");
        json.ShouldNotContain("\"predicate\": 5");
    }

    /// <summary>
    /// The column is data, not something somebody reads: camel-cased members and no indentation.
    /// </summary>
    [Fact]
    public void ADocumentIsCamelCasedAndUnindented()
    {
        var block = ReadyGateBlock.Create(ReadyGatePredicate.NoOpenHold, "HOLD-FABRIC").Value;

        var json = OrdersJson.Write([block]);

        json.ShouldContain("\"predicate\"");
        json.ShouldContain("\"reference\"");
        json.ShouldNotContain("\n");
        json.ShouldNotContain("  ");
    }

    /* Reading what is not there ------------------------------------------------------------------ */

    /// <summary>
    /// A column that is null, empty or blank is no document rather than a malformed one. Every read takes the
    /// same path, and each returns an empty collection instead of throwing on a row written before the column
    /// existed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAbsentDocumentReadsAsAnEmptyCollectionAndNotAsAFailure(string? json)
    {
        OrdersJson.ReadValues(json).ShouldBeEmpty();
        OrdersJson.ReadSelections(json).ShouldBeEmpty();
        OrdersJson.ReadNotes(json).ShouldBeEmpty();
        OrdersJson.ReadBlocks(json).ShouldBeEmpty();
    }

    /// <summary>
    /// A literal <c>null</c> document deserialises to a null list, which each read replaces with an empty one —
    /// the arm that keeps a caller from having to null-check a collection the Domain says is never null.
    /// </summary>
    [Fact]
    public void ALiteralNullDocumentReadsAsAnEmptyCollection()
    {
        OrdersJson.ReadValues("null").ShouldBeEmpty();
        OrdersJson.ReadSelections("null").ShouldBeEmpty();
        OrdersJson.ReadNotes("null").ShouldBeEmpty();
        OrdersJson.ReadBlocks("null").ShouldBeEmpty();
    }

    /// <summary>An empty collection is written as an empty document and reads back as one.</summary>
    [Fact]
    public void AnEmptyCollectionRoundTripsAsEmpty()
    {
        OrdersJson.Write(Array.Empty<MeasuredValue>()).ShouldBe("[]");
        OrdersJson.Write(Array.Empty<DesignSelection>()).ShouldBe("[]");
        OrdersJson.Write(Array.Empty<string>()).ShouldBe("[]");
        OrdersJson.Write(Array.Empty<ReadyGateBlock>()).ShouldBe("[]");

        OrdersJson.ReadBlocks("[]").ShouldBeEmpty();
    }

    /// <summary>
    /// Nothing here accepts a null collection. The column is written from a Domain value that is never null, so a
    /// null is a defect in the caller and is refused rather than written as an absent document.
    /// </summary>
    [Fact]
    public void WritingANullCollectionIsRefused()
    {
        Should.Throw<ArgumentNullException>(() => OrdersJson.Write((IReadOnlyList<MeasuredValue>)null!));
        Should.Throw<ArgumentNullException>(() => OrdersJson.Write((IReadOnlyList<DesignSelection>)null!));
        Should.Throw<ArgumentNullException>(() => OrdersJson.Write((IReadOnlyList<string>)null!));
        Should.Throw<ArgumentNullException>(() => OrdersJson.Write((IReadOnlyCollection<ReadyGateBlock>)null!));
    }

    /// <summary>
    /// A document whose text is not JSON at all is a corrupt column, and corrupting a read into an empty
    /// collection would hide it. It throws, which is what puts the row in front of somebody.
    /// </summary>
    [Fact]
    public void AMalformedDocumentIsNotQuietlyReadAsEmpty()
        => Should.Throw<JsonException>(() => OrdersJson.ReadBlocks("{ not json"));
}
