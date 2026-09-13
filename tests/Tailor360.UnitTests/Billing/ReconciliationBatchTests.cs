using Shouldly;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The reconciliation batch a close opens (#172): no variance beyond the threshold needs no approval and
/// refuses one, a variance beyond it is approved once by someone other than the cashier who closed the
/// session, and the batch's own arithmetic — <c>Variance = RecordedTotal - ExpectedTotal</c>, and the
/// mode lines summing to it — holds over random closes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReconciliationBatchTests
{
    private static readonly Guid Cashier = BillingTestData.Id("cashier");
    private static readonly Guid BranchManager = BillingTestData.Id("branch-manager");
    private static readonly DateTimeOffset Later = BillingTestData.Now.AddHours(9);

    private static readonly IReadOnlyDictionary<string, Money> ExpectedCashOnly = new Dictionary<string, Money>(StringComparer.Ordinal)
    {
        [PaymentModeCodes.Cash] = Money.Rupees(2000m),
        ["CARD"] = Money.Zero,
    };

    private static readonly IReadOnlyDictionary<string, Money> ExpectedWithCardBaseline = new Dictionary<string, Money>(StringComparer.Ordinal)
    {
        [PaymentModeCodes.Cash] = Money.Rupees(2000m),
        ["CARD"] = Money.Rupees(300m),
    };

    [Fact]
    public void OpensNotRequiredWhenTheCountAgreesAndRefusesAnApproval()
    {
        var session = ClosedExactly();
        var batch = ReconciliationBatch.OpenForClose(BillingTestData.Id("batch"), session, Money.Zero, Later);

        batch.CashierSessionId.ShouldBe(session.Id);
        batch.ClosedBy.ShouldBe(Cashier);
        batch.ExpectedTotal.ShouldBe(Money.Rupees(2000m));
        batch.RecordedTotal.ShouldBe(Money.Rupees(2000m));
        batch.Variance.ShouldBe(Money.Zero);
        batch.Status.ShouldBe(ReconciliationBatchStatus.NotRequired);
        batch.ApprovalRequired.ShouldBeFalse();
        batch.IsApproved.ShouldBeFalse();

        batch.Approve(BranchManager, "Looks fine.", Later).Error.Code.ShouldBe("billing.reconciliation-approval-not-required");
    }

    [Fact]
    public void OpensPendingWhenAVarianceIsBeyondTheThresholdAndApprovesOnceByADifferentUser()
    {
        var session = ClosedShortBy100();
        var batch = ReconciliationBatch.OpenForClose(BillingTestData.Id("batch"), session, Money.Rupees(50m), Later);

        batch.Status.ShouldBe(ReconciliationBatchStatus.Pending);
        batch.ApprovalRequired.ShouldBeTrue();
        batch.Variance.ShouldBe(Money.Rupees(-100m));
        batch.CloseReason.ShouldBe("Change given from the wrong tray.");

        batch.Approve(Cashier, "I'll allow it.", Later).Error.Code.ShouldBe("billing.reconciliation-approval-by-same-user", "the cashier who closed it may not approve their own variance");
        batch.Approve(BranchManager, null, Later).Error.Code.ShouldBe("billing.reason-required");
        batch.Status.ShouldBe(ReconciliationBatchStatus.Pending, "a refused approval leaves the batch pending");

        var approved = batch.Approve(BranchManager, " Counted twice; the float was short. ", Later.AddMinutes(5));
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Error.Code : string.Empty);
        batch.IsApproved.ShouldBeTrue();
        batch.ApprovedBy.ShouldBe(BranchManager);
        batch.ApprovedAt.ShouldBe(Later.AddMinutes(5));
        batch.ApprovalReason.ShouldBe("Counted twice; the float was short.");

        batch.Approve(BranchManager, "Again.", Later).Error.Code.ShouldBe("billing.reconciliation-batch-already-approved");
    }

    [Fact]
    public void OpensPendingOnAThresholdOfZeroWheneverAnyModeIsOverOrShort()
    {
        var session = ClosedWithCardVariance(350.50m);
        var batch = ReconciliationBatch.OpenForClose(BillingTestData.Id("batch"), session, Money.Zero, Later);

        batch.Status.ShouldBe(ReconciliationBatchStatus.Pending);
        batch.ModeLines.Single(line => line.ModeCode == "CARD").Variance.ShouldBe(350.50m);
        batch.ModeLines.Single(line => line.ModeCode == PaymentModeCodes.Cash).Variance.ShouldBe(0m);
    }

    [Fact]
    public void TheModeLinesSumToTheTotalVarianceOverRandomClosesInEitherDirection()
    {
        var random = new Random(172_041);
        for (var run = 0; run < 300; run++)
        {
            var variance = random.Next(-30_000, 30_001) / 100m;
            var session = ClosedWithCardVariance(variance);
            var threshold = Money.Rupees(random.Next(0, 200));
            var batch = ReconciliationBatch.OpenForClose(BillingTestData.Id($"batch-{run}"), session, threshold, Later);

            batch.Variance.ShouldBe(batch.RecordedTotal - batch.ExpectedTotal);
            batch.ModeLines.Sum(line => line.Variance).ShouldBe(batch.Variance.Amount);

            var beyond = Math.Abs(variance) > threshold.Amount;
            batch.ApprovalRequired.ShouldBe(beyond, $"variance {variance} against threshold {threshold.Amount}");
        }
    }

    private static CashierSession ClosedExactly()
    {
        var session = CashierSession.Open(BillingTestData.Id("session"), BillingTestData.Organisation, BillingTestData.MainBranch, Cashier, Money.Rupees(2000m), BillingTestData.Now).Value;
        session.Close([new DenominationCount(2000m, 1)], [], ExpectedCashOnly, null, Money.Zero, Later, Cashier).IsSuccess.ShouldBeTrue();
        return session;
    }

    private static CashierSession ClosedShortBy100()
    {
        var session = CashierSession.Open(BillingTestData.Id("session"), BillingTestData.Organisation, BillingTestData.MainBranch, Cashier, Money.Rupees(2000m), BillingTestData.Now).Value;
        session.Close(
            [new DenominationCount(500m, 3), new DenominationCount(200m, 2)], [], ExpectedCashOnly,
            "Change given from the wrong tray.", Money.Zero, Later, Cashier).IsSuccess.ShouldBeTrue();
        return session;
    }

    /// <summary>A session closed with the cash drawer exactly agreeing with the float, and CARD carrying
    /// the whole of <paramref name="variance"/> against its 300 baseline — so the arithmetic under test
    /// is isolated to one line, in either direction, without a denomination sheet that cannot express
    /// a fractional overage.</summary>
    private static CashierSession ClosedWithCardVariance(decimal variance)
    {
        var session = CashierSession.Open(BillingTestData.Id("session"), BillingTestData.Organisation, BillingTestData.MainBranch, Cashier, Money.Rupees(2000m), BillingTestData.Now).Value;
        session.Close(
            [new DenominationCount(2000m, 1)], [new ModeCount("CARD", 300m + variance)], ExpectedWithCardBaseline,
            "Reconciled.", Money.Zero, Later, Cashier).IsSuccess.ShouldBeTrue();
        return session;
    }
}
