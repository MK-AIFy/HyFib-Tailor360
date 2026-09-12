using Shouldly;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The single-use dispatch exception (#164): approved once with the checks the domain itself owns,
/// consumed exactly once with every re-validation the acceptance criteria name, and expired only once
/// it is past due and never touched again.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DispatchExceptionTests
{
    private static readonly Guid Approver = BillingTestData.Id("owner");
    private static readonly Guid Dispatcher = BillingTestData.Id("dispatcher");
    private static readonly Guid Order = BillingTestData.Id("order");
    private static readonly Guid JobA = BillingTestData.Id("job-a");
    private static readonly Guid JobB = BillingTestData.Id("job-b");
    private static readonly DateTimeOffset Now = BillingTestData.Now;

    [Fact]
    public void ApprovesWithEveryFieldSetAndTheJobSetDeduplicated()
    {
        var approved = DispatchException.Approve(
            BillingTestData.Id("exception"), BillingTestData.Organisation, BillingTestData.MainBranch, Order,
            [JobA, JobB, JobA], Money.Rupees(500m), "1", "CUSTOMER_TRAVELLING", "Travelling for a wedding.",
            Approver, Now.AddHours(48), Now);

        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Error.Code : string.Empty);
        var exception = approved.Value;
        exception.Status.ShouldBe(DispatchExceptionStatus.Approved);
        exception.JobIds.Count.ShouldBe(2);
        exception.JobIds.ShouldContain(JobA);
        exception.JobIds.ShouldContain(JobB);
        exception.MaxOutstandingAmount.ShouldBe(Money.Rupees(500m));
        exception.PolicyVersion.ShouldBe("1");
        exception.ReasonCode.ShouldBe("CUSTOMER_TRAVELLING");
        exception.ReasonText.ShouldBe("Travelling for a wedding.");
        exception.ApprovedBy.ShouldBe(Approver);
        exception.ApprovedAt.ShouldBe(Now);
        exception.ExpiresAt.ShouldBe(Now.AddHours(48));
    }

    [Fact]
    public void RefusesAnEmptyJobSet()
        => DispatchException.Approve(
                BillingTestData.Id("exception"), BillingTestData.Organisation, BillingTestData.MainBranch, Order,
                [], Money.Rupees(500m), "1", "CODE", "Reason.", Approver, Now.AddHours(1), Now)
            .Error.Code.ShouldBe("billing.value-required");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefusesAMaximumThatIsNotPositive(decimal amount)
        => DispatchException.Approve(
                BillingTestData.Id("exception"), BillingTestData.Organisation, BillingTestData.MainBranch, Order,
                [JobA], Money.Rupees(amount), "1", "CODE", "Reason.", Approver, Now.AddHours(1), Now)
            .Error.Code.ShouldBe("billing.dispatch-exception-amount-not-positive");

    [Fact]
    public void RefusesAnExpiryThatIsNotInTheFuture()
        => DispatchException.Approve(
                BillingTestData.Id("exception"), BillingTestData.Organisation, BillingTestData.MainBranch, Order,
                [JobA], Money.Rupees(500m), "1", "CODE", "Reason.", Approver, Now, Now)
            .Error.Code.ShouldBe("billing.dispatch-exception-expiry-not-well-formed");

    [Fact]
    public void RefusesAnExpiryMoreThanSeventyTwoHoursAhead()
        => DispatchException.Approve(
                BillingTestData.Id("exception"), BillingTestData.Organisation, BillingTestData.MainBranch, Order,
                [JobA], Money.Rupees(500m), "1", "CODE", "Reason.", Approver, Now.AddHours(72).AddMinutes(1), Now)
            .Error.Code.ShouldBe("billing.dispatch-exception-expiry-not-well-formed");

    [Fact]
    public void AcceptsAnExpiryExactlyAtSeventyTwoHours()
        => DispatchException.Approve(
                BillingTestData.Id("exception"), BillingTestData.Organisation, BillingTestData.MainBranch, Order,
                [JobA], Money.Rupees(500m), "1", "CODE", "Reason.", Approver, Now.AddHours(72), Now)
            .IsSuccess.ShouldBeTrue();

    [Fact]
    public void RefusesAMissingReasonCode()
        => DispatchException.Approve(
                BillingTestData.Id("exception"), BillingTestData.Organisation, BillingTestData.MainBranch, Order,
                [JobA], Money.Rupees(500m), "1", "  ", "Reason.", Approver, Now.AddHours(1), Now)
            .Error.Code.ShouldBe("billing.value-required");

    [Fact]
    public void RefusesAMissingReasonText()
        => DispatchException.Approve(
                BillingTestData.Id("exception"), BillingTestData.Organisation, BillingTestData.MainBranch, Order,
                [JobA], Money.Rupees(500m), "1", "CODE", null, Approver, Now.AddHours(1), Now)
            .Error.Code.ShouldBe("billing.reason-required");

    private static DispatchException Approved(DateTimeOffset? expiresAt = null, Money? maxOutstanding = null, string policyVersion = "1")
        => DispatchException.Approve(
                BillingTestData.Id("exception"), BillingTestData.Organisation, BillingTestData.MainBranch, Order,
                [JobA, JobB], maxOutstanding ?? Money.Rupees(500m), policyVersion, "CODE", "Reason.", Approver, expiresAt ?? Now.AddHours(48), Now)
            .Value;

    [Fact]
    public void ConsumesOnceAndSetsWhoAndWhen()
    {
        var exception = Approved();
        var later = Now.AddHours(1);

        var consumed = exception.Consume(Dispatcher, Money.Rupees(200m), [JobA, JobB], "1", later);

        consumed.IsSuccess.ShouldBeTrue(consumed.IsFailure ? consumed.Error.Code : string.Empty);
        exception.Status.ShouldBe(DispatchExceptionStatus.Consumed);
        exception.ConsumedBy.ShouldBe(Dispatcher);
        exception.ConsumedAt.ShouldBe(later);
    }

    [Fact]
    public void RefusesASecondConsumption()
    {
        var exception = Approved();
        exception.Consume(Dispatcher, Money.Rupees(200m), [JobA, JobB], "1", Now.AddHours(1)).IsSuccess.ShouldBeTrue();

        exception.Consume(Dispatcher, Money.Rupees(200m), [JobA, JobB], "1", Now.AddHours(2))
            .Error.Code.ShouldBe("billing.dispatch-exception-already-consumed");
    }

    [Fact]
    public void RefusesConsumptionOncePastItsExpiryEvenIfNeverMarkedExpired()
    {
        var exception = Approved(expiresAt: Now.AddHours(2));

        exception.Consume(Dispatcher, Money.Rupees(200m), [JobA, JobB], "1", Now.AddHours(2))
            .Error.Code.ShouldBe("billing.dispatch-exception-expired");
    }

    [Fact]
    public void RefusesTheApproverConsumingTheirOwnException()
    {
        var exception = Approved();

        exception.Consume(Approver, Money.Rupees(200m), [JobA, JobB], "1", Now.AddHours(1))
            .Error.Code.ShouldBe("billing.dispatch-exception-approver-is-dispatcher");
    }

    [Fact]
    public void RefusesAJobSetThatNoLongerMatchesExactly()
    {
        var exception = Approved();

        exception.Consume(Dispatcher, Money.Rupees(200m), [JobA], "1", Now.AddHours(1))
            .Error.Code.ShouldBe("billing.dispatch-exception-job-set-changed");
    }

    [Fact]
    public void RefusesAPolicyVersionThatHasMovedOn()
    {
        var exception = Approved(policyVersion: "1");

        exception.Consume(Dispatcher, Money.Rupees(200m), [JobA, JobB], "2", Now.AddHours(1))
            .Error.Code.ShouldBe("billing.dispatch-exception-policy-version-changed");
    }

    [Fact]
    public void RefusesABalanceThatHasGrownBeyondTheApprovedMaximum()
    {
        var exception = Approved(maxOutstanding: Money.Rupees(500m));

        exception.Consume(Dispatcher, Money.Rupees(500.01m), [JobA, JobB], "1", Now.AddHours(1))
            .Error.Code.ShouldBe("billing.dispatch-exception-balance-exceeded");
    }

    [Fact]
    public void AcceptsABalanceExactlyAtTheApprovedMaximum()
    {
        var exception = Approved(maxOutstanding: Money.Rupees(500m));

        exception.Consume(Dispatcher, Money.Rupees(500m), [JobA, JobB], "1", Now.AddHours(1)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ExpiresOnceItIsPastDueAndTheExpiryIsANoOpTheSecondTime()
    {
        var exception = Approved(expiresAt: Now.AddHours(2));
        var due = Now.AddHours(3);

        exception.Expire(due).IsSuccess.ShouldBeTrue();
        exception.Status.ShouldBe(DispatchExceptionStatus.Expired);
        exception.ExpiredAt.ShouldBe(due);

        // A second pass, or a second worker instance racing the same row, finds it already done.
        exception.Expire(due.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        exception.ExpiredAt.ShouldBe(due, "the second call is a no-op; it does not restamp the time");
    }

    [Fact]
    public void RefusesExpiringAnExceptionAlreadyConsumed()
    {
        var exception = Approved(expiresAt: Now.AddHours(2));
        exception.Consume(Dispatcher, Money.Rupees(200m), [JobA, JobB], "1", Now.AddHours(1)).IsSuccess.ShouldBeTrue();

        exception.Expire(Now.AddHours(3)).Error.Code.ShouldBe("billing.dispatch-exception-already-consumed");
    }

    [Fact]
    public void RefusesExpiringAnExceptionBeforeItIsDue()
    {
        var exception = Approved(expiresAt: Now.AddHours(2));

        exception.Expire(Now.AddHours(1)).Error.Code.ShouldBe("billing.dispatch-exception-not-yet-due");
    }
}
