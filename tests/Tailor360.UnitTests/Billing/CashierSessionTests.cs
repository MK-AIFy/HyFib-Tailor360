using Shouldly;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The cashier session in the aggregate (#161): it opens with a float, closes once against a sheet the
/// domain sums itself, refuses a stranger's close, a sheet with a note that does not exist and a cash line
/// that disagrees with the sheet, and asks for a reason only where the count is out beyond the threshold.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CashierSessionTests
{
    private static readonly Guid Cashier = BillingTestData.Id("cashier");
    private static readonly Guid Other = BillingTestData.Id("other-cashier");
    private static readonly DateTimeOffset Later = BillingTestData.Now.AddHours(9);

    private static readonly IReadOnlyDictionary<string, Money> Expected = new Dictionary<string, Money>(StringComparer.Ordinal)
    {
        [PaymentModeCodes.Cash] = Money.Rupees(2000m),
        ["CARD"] = Money.Zero,
        ["UPI"] = Money.Zero,
    };

    [Fact]
    public void OpensWithAFloatToThePaisaAndNeverANegativeOne()
    {
        var session = Open(2000m);
        session.IsOpen.ShouldBeTrue();
        session.Status.ShouldBe(CashierSessionStatus.Open);
        session.OpeningFloat.ShouldBe(Money.Rupees(2000m));
        session.ExpectedTotal.ShouldBe(Money.Zero);
        session.ClosedAt.ShouldBeNull();

        CashierSession.Open(BillingTestData.Id("s"), BillingTestData.Organisation, BillingTestData.MainBranch, Cashier, Money.Rupees(-1m), BillingTestData.Now)
            .Error.Code.ShouldBe("billing.amount-not-well-formed");
        CashierSession.Open(BillingTestData.Id("s"), BillingTestData.Organisation, BillingTestData.MainBranch, Cashier, Money.Rupees(0.001m), BillingTestData.Now)
            .Error.Code.ShouldBe("billing.amount-not-well-formed");
        CashierSession.Open(BillingTestData.Id("s"), BillingTestData.Organisation, BillingTestData.MainBranch, Cashier, new Money(10m, "USD"), BillingTestData.Now)
            .Error.Code.ShouldBe("billing.amount-not-well-formed");
        CashierSession.Open(BillingTestData.Id("s"), BillingTestData.Organisation, BillingTestData.MainBranch, Cashier, Money.Rupees(100_000_000_000_000m), BillingTestData.Now)
            .Error.Code.ShouldBe("billing.amount-not-well-formed", "more than the ledger's column holds");
    }

    [Fact]
    public void ClosesAgainstTheSheetSummingTheCashItselfAndCountingAnAbsentModeAsZero()
    {
        var session = Open(2000m);

        // 3 x 500 + 2 x 200 + 1 x 100 = 2000: the float exactly. Card counted, UPI left out.
        var closed = session.Close(
            [new DenominationCount(500m, 3), new DenominationCount(200m, 2), new DenominationCount(100m, 1)],
            [new ModeCount("CARD", 0m)],
            Expected, null, Money.Zero, Later, Cashier);

        closed.IsSuccess.ShouldBeTrue(closed.IsFailure ? closed.Error.Code : string.Empty);
        session.Status.ShouldBe(CashierSessionStatus.Closed);
        session.IsOpen.ShouldBeFalse();
        session.ClosedAt.ShouldBe(Later);
        session.ClosedBy.ShouldBe(Cashier);
        session.ExpectedTotal.ShouldBe(Money.Rupees(2000m));
        session.CountedTotal.ShouldBe(Money.Rupees(2000m));
        session.Variance.ShouldBe(Money.Zero);
        session.VarianceReason.ShouldBeNull();
        session.Counts.Select(count => (count.Denomination, count.Quantity, count.Value)).ShouldBe([(500m, 3, 1500m), (200m, 2, 400m), (100m, 1, 100m)]);
        session.ModeTotals.Select(total => (total.ModeCode, total.Expected, total.Counted, total.Variance))
            .ShouldBe([("CARD", 0m, 0m, 0m), ("CASH", 2000m, 2000m, 0m), ("UPI", 0m, 0m, 0m)]);

        // Once: the second close is a conflict, and nothing about the row moved.
        session.Close([], [], Expected, "Again.", Money.Zero, Later.AddMinutes(1), Cashier).Error.Code.ShouldBe("billing.cashier-session-already-closed");
        session.ClosedAt.ShouldBe(Later);
    }

    [Fact]
    public void AVarianceBeyondTheThresholdNeedsAReasonAndWithinItDoesNot()
    {
        var session = Open(2000m);
        var short100 = new[] { new DenominationCount(500m, 3), new DenominationCount(200m, 2) };

        session.Close(short100, [], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.variance-reason-required");
        session.Close(short100, [], Expected, "   ", Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.variance-reason-required");
        session.IsOpen.ShouldBeTrue("a refused close leaves the session open");

        // A threshold of 100 covers a 100 short exactly; the close records the variance without a reason.
        session.Close(short100, [], Expected, null, Money.Rupees(100m), Later, Cashier).IsSuccess.ShouldBeTrue();
        session.Variance.ShouldBe(Money.Rupees(-100m));
        session.VarianceReason.ShouldBeNull();

        var over = Open(2000m);
        over.Close([new DenominationCount(500m, 4), new DenominationCount(10m, 1)], [new ModeCount("CARD", 350.50m)], Expected, " Change given from the wrong tray. ", Money.Zero, Later, Cashier)
            .IsSuccess.ShouldBeTrue();
        over.CountedTotal.ShouldBe(Money.Rupees(2360.50m));
        over.Variance.ShouldBe(Money.Rupees(360.50m));
        over.VarianceReason.ShouldBe("Change given from the wrong tray.");
        over.ModeTotals.Single(total => total.ModeCode == "CARD").Variance.ShouldBe(350.50m);
    }

    [Fact]
    public void RefusesAStrangerASheetItCannotSumAndACashLineThatDisagreesWithTheSheet()
    {
        var session = Open(2000m);
        var exact = new[] { new DenominationCount(2000m, 1) };

        session.Close(exact, [], Expected, null, Money.Zero, Later, Other).Error.Code.ShouldBe("billing.cashier-session-not-yours");
        session.Close([new DenominationCount(1000m, 2)], [], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.denomination-not-known");
        session.Close([new DenominationCount(500m, -1)], [], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.count-not-well-formed");
        session.Close([new DenominationCount(500m, 2), new DenominationCount(500m, 2)], [], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.line-key-duplicated");
        session.Close(exact, [new ModeCount("CHEQUE", 10m)], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.payment-mode-not-known");
        session.Close(exact, [new ModeCount("CARD", -1m)], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.amount-not-well-formed");
        session.Close(exact, [new ModeCount("CARD", 1.005m)], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.amount-not-well-formed");
        session.Close(exact, [new ModeCount("CARD", CashierSession.MaximumAmount + 0.01m)], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.amount-not-well-formed");
        session.Close(exact, [new ModeCount("CARD", 1m), new ModeCount("CARD", 2m)], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.line-key-duplicated");
        session.Close(exact, [new ModeCount(PaymentModeCodes.Cash, 1999m)], Expected, null, Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.cash-count-mismatch");
        session.Close(exact, [], Expected, new string('x', CashierSession.MaximumReasonLength + 1), Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.value-too-long");
        session.Close(exact, [], Expected, "a\tb", Money.Zero, Later, Cashier).Error.Code.ShouldBe("billing.reason-not-well-formed");
        session.IsOpen.ShouldBeTrue();

        // A cash line that agrees with the sheet is allowed, and redundant.
        session.Close(exact, [new ModeCount(PaymentModeCodes.Cash, 2000m)], Expected, null, Money.Zero, Later, Cashier).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void TheDenominationsAreTheRupeesNotesAndCoinsLargestFirst()
    {
        CashDenominations.All.ShouldBe([2000m, 500m, 200m, 100m, 50m, 20m, 10m, 5m, 2m, 1m]);
        CashDenominations.IsKnown(0.5m).ShouldBeFalse();
    }

    private static CashierSession Open(decimal openingFloat)
        => CashierSession.Open(BillingTestData.Id("session"), BillingTestData.Organisation, BillingTestData.MainBranch, Cashier, Money.Rupees(openingFloat), BillingTestData.Now).Value;
}
