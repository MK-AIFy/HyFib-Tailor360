using Shouldly;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The dispatch eligibility rule (#164) as a pure function of Billing's own figures and the branch's
/// configured policy: fails closed with no posted invoice, answers Paid once nothing is outstanding, and
/// otherwise defers to the branch's rule — Full or PartialThreshold — for what remains. A live approved
/// exception always wins.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DispatchEligibilityRuleTests
{
    [Fact]
    public void ALiveApprovedExceptionWinsOverEveryOtherFigure()
    {
        DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: false, hasLiveApprovedException: true,
            charges: Money.Zero, outstanding: Money.Zero, unappliedAdvances: Money.Zero,
            rule: DispatchPolicyRule.Full, partialThreshold: 0m, allowOnAdvance: false, advanceThreshold: Money.Zero)
            .ShouldBe(DispatchEligibilityReason.ApprovedException);
    }

    [Fact]
    public void NoPostedInvoiceFailsClosedWhenAdvancesAreNotAllowedToCoverIt()
    {
        DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: false, hasLiveApprovedException: false,
            charges: Money.Zero, outstanding: Money.Zero, unappliedAdvances: Money.Rupees(10_000m),
            rule: DispatchPolicyRule.Full, partialThreshold: 0m, allowOnAdvance: false, advanceThreshold: Money.Zero)
            .ShouldBe(DispatchEligibilityReason.NotEvaluated);
    }

    [Fact]
    public void NoPostedInvoiceFailsClosedWhenTheAdvanceHeldIsBelowTheConfiguredFloor()
    {
        DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: false, hasLiveApprovedException: false,
            charges: Money.Zero, outstanding: Money.Zero, unappliedAdvances: Money.Rupees(499.99m),
            rule: DispatchPolicyRule.Full, partialThreshold: 0m, allowOnAdvance: true, advanceThreshold: Money.Rupees(500m))
            .ShouldBe(DispatchEligibilityReason.NotEvaluated);
    }

    [Fact]
    public void NoPostedInvoiceAnswersPaidWhenTheAdvanceHeldClearsTheConfiguredFloor()
    {
        DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: false, hasLiveApprovedException: false,
            charges: Money.Zero, outstanding: Money.Zero, unappliedAdvances: Money.Rupees(500m),
            rule: DispatchPolicyRule.Full, partialThreshold: 0m, allowOnAdvance: true, advanceThreshold: Money.Rupees(500m))
            .ShouldBe(DispatchEligibilityReason.Paid);
    }

    [Fact]
    public void AZeroFloorNeverLetsAllowOnAdvanceTakeEffectEvenWhenTurnedOn()
    {
        // OD-04 has not sourced a number; the interim default is a floor of zero, which must mean "no
        // effect" rather than "any advance at all clears it" — a floor of zero read the other way would
        // invent a number nobody decided.
        DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: false, hasLiveApprovedException: false,
            charges: Money.Zero, outstanding: Money.Zero, unappliedAdvances: Money.Rupees(1_000_000m),
            rule: DispatchPolicyRule.Full, partialThreshold: 0m, allowOnAdvance: true, advanceThreshold: Money.Zero)
            .ShouldBe(DispatchEligibilityReason.NotEvaluated);
    }

    [Fact]
    public void APostedInvoiceWithNothingOutstandingAnswersPaid()
    {
        DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: true, hasLiveApprovedException: false,
            charges: Money.Rupees(2000m), outstanding: Money.Zero, unappliedAdvances: Money.Zero,
            rule: DispatchPolicyRule.Full, partialThreshold: 0.5m, allowOnAdvance: false, advanceThreshold: Money.Zero)
            .ShouldBe(DispatchEligibilityReason.Paid);
    }

    [Fact]
    public void TheFullRuleAnswersUnpaidForAnyOutstandingBalanceHoweverSmall()
    {
        DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: true, hasLiveApprovedException: false,
            charges: Money.Rupees(2000m), outstanding: Money.Rupees(0.01m), unappliedAdvances: Money.Zero,
            rule: DispatchPolicyRule.Full, partialThreshold: 0m, allowOnAdvance: false, advanceThreshold: Money.Zero)
            .ShouldBe(DispatchEligibilityReason.Unpaid);
    }

    [Theory]
    [InlineData(1500, 500, 0.5, DispatchEligibilityReason.PartialAboveThreshold)] // paid share 0.75
    [InlineData(1500, 800, 0.5, DispatchEligibilityReason.PartialBelowThreshold)] // paid share ~0.467
    [InlineData(1000, 500, 0.5, DispatchEligibilityReason.PartialAboveThreshold)] // paid share exactly 0.5, threshold met
    public void ThePartialThresholdRuleComparesThePaidShareAgainstTheConfiguredThreshold(
        decimal charges, decimal outstanding, decimal threshold, DispatchEligibilityReason expected)
    {
        DispatchEligibilityRule.Evaluate(
            hasPostedInvoice: true, hasLiveApprovedException: false,
            charges: Money.Rupees(charges), outstanding: Money.Rupees(outstanding), unappliedAdvances: Money.Zero,
            rule: DispatchPolicyRule.PartialThreshold, partialThreshold: threshold, allowOnAdvance: false, advanceThreshold: Money.Zero)
            .ShouldBe(expected);
    }
}
