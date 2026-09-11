using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Estimates;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The estimate sub-lifecycle — a priced snapshot of a draft, beside the order lifecycle and not inside it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EstimateTests
{
    private static readonly DateTimeOffset Later = OrdersTestData.Now.AddHours(4);

    /* Issuing one -------------------------------------------------------------------------------- */

    [Fact]
    public void AnIssuedEstimateIsOutstanding()
    {
        var estimate = OrdersTestData.IssuedEstimate();

        estimate.Status.ShouldBe(EstimateStatus.Issued);
        estimate.IssuedAt.ShouldBe(OrdersTestData.Now);
        estimate.IssuedBy.ShouldBe(OrdersTestData.Actor);
        estimate.SupersededAt.ShouldBeNull();
        estimate.ConvertedAt.ShouldBeNull();
        estimate.ArtefactChecksum.ShouldBeNull();
    }

    [Fact]
    public void AnEstimateIsNumberedFromTheEstimateSeriesAndNeverTheInvoiceSeries()
    {
        // INV-ORD-04: an estimate is never posted and never consumes an invoice number. A separate
        // namespace letter and a separate sequence are half of how that is kept true.
        var estimate = OrdersTestData.IssuedEstimate();

        EstimateNumber.Namespace.ShouldBe("E");
        estimate.EstimateNumber.Value.ShouldStartWith("E-");
        OrderNumber.IsWellFormed(estimate.EstimateNumber.Value).ShouldBeFalse();
    }

    [Fact]
    public void AnEstimateCannotBeIssuedWithoutAnIdentity()
    {
        var refused = Issue(id: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("id");
    }

    [Fact]
    public void AnEstimateCannotBeIssuedOutsideAnOrganisation()
    {
        var refused = Issue(organisationId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("organisationId");
    }

    [Fact]
    public void AnEstimateCannotBeIssuedOutsideABranch()
    {
        // The branch is also the sequence key the number came from, so an estimate with none could not be
        // numbered honestly.
        var refused = Issue(branchId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.no-branch-in-context");
    }

    [Fact]
    public void AnEstimateCannotBeIssuedWithoutTheDraftItPrices()
    {
        var refused = Issue(orderDraftId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("orderDraftId");
    }

    [Fact]
    public void AnEstimateCannotBeIssuedWithoutACustomerToQuoteTo()
    {
        var refused = Issue(customerId: Guid.Empty);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("customerId");
    }

    [Fact]
    public void AQuoteCannotStopBeingValidBeforeTheDayItWasIssued()
    {
        var refused = Issue(validUntil: OrdersTestData.Today.AddDays(-1));

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.estimate-validity-before-issue");
        refused.Error.Target.ShouldBe("validUntil");
    }

    [Fact]
    public void AQuoteMayStandForTheDayItWasIssuedAlone()
    {
        // No default window is documented anywhere, so the caller supplies the date rather than the domain
        // inventing one: how long a quote stands is a product decision.
        var issued = Issue(validUntil: OrdersTestData.Today);

        issued.IsSuccess.ShouldBeTrue();
        issued.Value.IsValidOn(OrdersTestData.Today).ShouldBeTrue();
        issued.Value.IsValidOn(OrdersTestData.Today.AddDays(1)).ShouldBeFalse();
    }

    [Fact]
    public void AnEstimateMissingSomethingTheTypeSystemSaidCouldNotBeNullIsADefect()
    {
        Should.Throw<ArgumentNullException>(() => Estimate.Issue(
            OrdersTestData.Id("estimate-1"),
            OrdersTestData.Organisation,
            OrdersTestData.Branch,
            OrdersTestData.Id("draft"),
            OrdersTestData.Customer,
            null!,
            OrdersTestData.Price(),
            OrdersTestData.Today,
            OrdersTestData.Today.AddDays(14),
            OrdersTestData.Now,
            OrdersTestData.Actor));

        Should.Throw<ArgumentNullException>(() => Estimate.Issue(
            OrdersTestData.Id("estimate-1"),
            OrdersTestData.Organisation,
            OrdersTestData.Branch,
            OrdersTestData.Id("draft"),
            OrdersTestData.Customer,
            OrdersTestData.QuoteNumber(),
            null!,
            OrdersTestData.Today,
            OrdersTestData.Today.AddDays(14),
            OrdersTestData.Now,
            OrdersTestData.Actor));
    }

    /* Validity, which is derived and never stored ------------------------------------------------ */

    [Fact]
    public void AnEstimateIsValidFromTheDayItWasIssuedToTheDayItStops()
    {
        var estimate = OrdersTestData.IssuedEstimate(validUntil: OrdersTestData.Today.AddDays(14));

        estimate.IsValidOn(OrdersTestData.Today.AddDays(-1)).ShouldBeFalse();
        estimate.IsValidOn(OrdersTestData.Today).ShouldBeTrue();
        estimate.IsValidOn(OrdersTestData.Today.AddDays(14)).ShouldBeTrue();
        estimate.IsValidOn(OrdersTestData.Today.AddDays(15)).ShouldBeFalse();
    }

    [Fact]
    public void ExpiryIsDerivedFromTheDatesAndIsNeverAStoredStatus()
    {
        // A stored expiry would need a writer, and the only honest writer would be a clock — which would
        // leave the shop treating an estimate as live or dead depending on when a job last ran. An
        // estimate past its date is reissued at current prices, never silently honoured.
        var estimate = OrdersTestData.IssuedEstimate(validUntil: OrdersTestData.Today);

        estimate.IsValidOn(OrdersTestData.Today.AddDays(30)).ShouldBeFalse();
        estimate.Status.ShouldBe(EstimateStatus.Issued);

        Enum.GetNames<EstimateStatus>().Length.ShouldBe(3);
        Enum.GetNames<EstimateStatus>().ShouldNotContain("Expired");
    }

    /* A reissue supersedes rather than edits ----------------------------------------------------- */

    [Fact]
    public void AReissueSupersedesRatherThanEditsTheEstimateTheCustomerIsHolding()
    {
        // The customer may already be holding the first one — it was shared through an expiring link — so
        // the price it quoted has to stay readable exactly as it was quoted.
        var estimate = OrdersTestData.IssuedEstimate();
        var quotedAt = estimate.Totals;
        var number = estimate.EstimateNumber;
        var newer = OrdersTestData.Id("estimate-2");

        var superseded = estimate.Supersede(newer, Later, OrdersTestData.Actor);

        superseded.IsSuccess.ShouldBeTrue();
        estimate.Status.ShouldBe(EstimateStatus.Superseded);
        estimate.SupersededByEstimateId.ShouldBe(newer);
        estimate.SupersededAt.ShouldBe(Later);
        estimate.SupersededBy.ShouldBe(OrdersTestData.Actor);
        estimate.Totals.ShouldBe(quotedAt);
        estimate.EstimateNumber.ShouldBe(number);
        estimate.IssuedOn.ShouldBe(OrdersTestData.Today);
    }

    [Fact]
    public void AnEstimateCannotReplaceItself()
    {
        var estimate = OrdersTestData.IssuedEstimate();

        var refused = estimate.Supersede(estimate.Id, Later, OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.estimate-cannot-supersede-itself");
        estimate.Status.ShouldBe(EstimateStatus.Issued);
    }

    [Fact]
    public void SupersedingWithoutNamingTheNewerEstimateIsRefused()
    {
        var estimate = OrdersTestData.IssuedEstimate();

        var refused = estimate.Supersede(Guid.Empty, Later, OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("supersedingEstimateId");
    }

    [Fact]
    public void AnEstimateIsSupersededOnlyOnce()
    {
        // A second reissue says "a newer estimate has replaced that one" rather than the vaguer "no longer
        // outstanding", so the person at the counter knows which document to work from.
        var estimate = OrdersTestData.IssuedEstimate();
        estimate.Supersede(OrdersTestData.Id("estimate-2"), Later, OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var refused = estimate.Supersede(OrdersTestData.Id("estimate-3"), Later, OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.estimate-already-superseded");
        estimate.SupersededByEstimateId.ShouldBe(OrdersTestData.Id("estimate-2"));
    }

    [Fact]
    public void AConvertedEstimateIsNeverSuperseded()
    {
        // The draft it priced has become an order, and rewriting the history of the quote the customer
        // accepted is what INV-ORD-04 is there to prevent.
        var estimate = OrdersTestData.IssuedEstimate();
        estimate.Convert(
            OrdersTestData.Id("order"),
            OrdersTestData.Id("draft"),
            Later,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var refused = estimate.Supersede(OrdersTestData.Id("estimate-2"), Later, OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.estimate-already-converted");
        estimate.Status.ShouldBe(EstimateStatus.Converted);
    }

    /* Becoming an order -------------------------------------------------------------------------- */

    [Fact]
    public void ConvertingRecordsTheOrderTheDraftBecame()
    {
        var estimate = OrdersTestData.IssuedEstimate();
        var order = OrdersTestData.Id("order");

        var converted = estimate.Convert(order, OrdersTestData.Id("draft"), Later, OrdersTestData.Actor);

        converted.IsSuccess.ShouldBeTrue();
        estimate.Status.ShouldBe(EstimateStatus.Converted);
        estimate.ConvertedToOrderId.ShouldBe(order);
        estimate.ConvertedAt.ShouldBe(Later);
        estimate.ConvertedBy.ShouldBe(OrdersTestData.Actor);
    }

    [Fact]
    public void ConvertingWithoutTheOrderTheDraftBecameIsRefused()
    {
        var estimate = OrdersTestData.IssuedEstimate();

        var refused = estimate.Convert(Guid.Empty, OrdersTestData.Id("draft"), Later, OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("orderId");
    }

    /// <summary>
    /// Section 2.2 defines the conversion relative to the draft this estimate prices — "order confirmed
    /// <strong>from the draft</strong>". Recording it against an order made from a different draft would
    /// spend this quote on somebody else's order and strand the one still genuinely outstanding for this
    /// draft: a converted estimate is never superseded, so nothing could retire it afterwards.
    /// </summary>
    [Fact]
    public void AnEstimateIsNotConvertedByAnOrderMadeFromADifferentDraft()
    {
        var estimate = OrdersTestData.IssuedEstimate();

        var refused = estimate.Convert(
            OrdersTestData.Id("order"),
            OrdersTestData.Id("another-draft"),
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.estimate-not-for-this-draft");
        refused.Error.Target.ShouldBe("orderDraftId");
        estimate.Status.ShouldBe(EstimateStatus.Issued);
        estimate.ConvertedToOrderId.ShouldBeNull();
    }

    [Fact]
    public void AnEstimateIsConvertedOnlyOnce()
    {
        var estimate = OrdersTestData.IssuedEstimate();
        estimate.Convert(
            OrdersTestData.Id("order"),
            OrdersTestData.Id("draft"),
            Later,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var refused = estimate.Convert(
            OrdersTestData.Id("a-second-order"),
            OrdersTestData.Id("draft"),
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.estimate-already-converted");
        estimate.ConvertedToOrderId.ShouldBe(OrdersTestData.Id("order"));
    }

    [Fact]
    public void ASupersededEstimateCannotBeConverted()
    {
        var estimate = OrdersTestData.IssuedEstimate();
        estimate.Supersede(OrdersTestData.Id("estimate-2"), Later, OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var refused = estimate.Convert(
            OrdersTestData.Id("order"),
            OrdersTestData.Id("draft"),
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.estimate-already-superseded");
        estimate.ConvertedToOrderId.ShouldBeNull();
    }

    /* The stored document ------------------------------------------------------------------------ */

    [Fact]
    public void TheStoredDocumentsChecksumIsRecordedOnce()
    {
        var estimate = OrdersTestData.IssuedEstimate();

        var recorded = estimate.RecordArtefact("  sha256:0f1e2d  ", Later);

        recorded.IsSuccess.ShouldBeTrue();
        estimate.ArtefactChecksum.ShouldBe("sha256:0f1e2d");
        estimate.ArtefactRecordedAt.ShouldBe(Later);
    }

    [Fact]
    public void ASecondChecksumIsRefusedSoASupersededDocumentCanStillBeProved()
    {
        var estimate = OrdersTestData.IssuedEstimate();
        estimate.RecordArtefact("sha256:0f1e2d", Later).IsSuccess.ShouldBeTrue();

        var refused = estimate.RecordArtefact("sha256:cafe01", Later.AddMinutes(5));

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.estimate-artefact-already-recorded");
        estimate.ArtefactChecksum.ShouldBe("sha256:0f1e2d");
        estimate.ArtefactRecordedAt.ShouldBe(Later);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AChecksumNobodyComputedIsRefused(string? checksum)
    {
        var refused = OrdersTestData.IssuedEstimate().RecordArtefact(checksum, Later);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("checksum");
    }

    [Fact]
    public void AChecksumLongerThanTheColumnIsRefused()
    {
        var tooLong = new string('a', Estimate.MaximumChecksumLength + 1);

        var refused = OrdersTestData.IssuedEstimate().RecordArtefact(tooLong, Later);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-too-long");
        refused.Error.Target.ShouldBe("checksum");
    }

    [Fact]
    public void ARenderThatFinishesAfterAReissueStillRecordsWhatItProduced()
    {
        // Rendering happens outside the issuing transaction, so the checksum arrives later and the record
        // is deliberately not guarded by status: otherwise the superseded document could never be
        // verified against the one the customer was actually given.
        var estimate = OrdersTestData.IssuedEstimate();
        estimate.Supersede(OrdersTestData.Id("estimate-2"), Later, OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        var recorded = estimate.RecordArtefact("sha256:0f1e2d", Later.AddMinutes(1));

        recorded.IsSuccess.ShouldBeTrue();
        estimate.ArtefactChecksum.ShouldBe("sha256:0f1e2d");
        estimate.Status.ShouldBe(EstimateStatus.Superseded);
    }

    [Fact]
    public void ASupersededEstimateKeepsTheChecksumItsDocumentWasStoredWith()
    {
        var estimate = OrdersTestData.IssuedEstimate();
        estimate.RecordArtefact("sha256:0f1e2d", Later).IsSuccess.ShouldBeTrue();

        estimate.Supersede(OrdersTestData.Id("estimate-2"), Later.AddMinutes(5), OrdersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        estimate.ArtefactChecksum.ShouldBe("sha256:0f1e2d");
    }

    /* What an estimate is not -------------------------------------------------------------------- */

    [Fact]
    public void AnEstimateHasNoWayToEditWhatItQuoted()
    {
        // A reissue supersedes; there is no command that re-prices this document in place, and no property
        // a caller could write.
        typeof(Estimate)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldAllBe(property => property.GetSetMethod() == null);

        typeof(Estimate)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(new List<string> { "Convert", "IsValidOn", "RecordArtefact", "Supersede" });
    }

    [Fact]
    public void AnEstimateHoldsTheCustomerAsAnIdentifierAndAsksForNoMeasurement()
    {
        // Measurements are a condition of confirming, not of quoting (state-transitions.md section 2.1),
        // and the customer is an identifier and nothing more (security rule 8).
        var personal = new[] { "name", "phone", "telephone", "email", "address", "measurement" };

        typeof(Estimate)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => personal.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ShouldBeEmpty();

        OrdersTestData.IssuedEstimate().CustomerId.ShouldBe(OrdersTestData.Customer);
    }

    private static Result<Estimate> Issue(
        Guid? id = null,
        Guid? organisationId = null,
        Guid? branchId = null,
        Guid? orderDraftId = null,
        Guid? customerId = null,
        DateOnly? validUntil = null)
        => Estimate.Issue(
            id ?? OrdersTestData.Id("estimate-1"),
            organisationId ?? OrdersTestData.Organisation,
            branchId ?? OrdersTestData.Branch,
            orderDraftId ?? OrdersTestData.Id("draft"),
            customerId ?? OrdersTestData.Customer,
            OrdersTestData.QuoteNumber(),
            OrdersTestData.Price(),
            OrdersTestData.Today,
            validUntil ?? OrdersTestData.Today.AddDays(14),
            OrdersTestData.Now,
            OrdersTestData.Actor);
}
