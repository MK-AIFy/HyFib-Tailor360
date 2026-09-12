using Shouldly;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Domain.Payments;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// A payment mode (#161): defined once under an upper-case code, renamed and re-flagged, restricted to
/// branches or offered everywhere, deactivated for new use, and the seeded set is one the domain accepts.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PaymentModeTests
{
    private static readonly PaymentModeDetails Card = new("Card", RequiresReference: true, RequiresProvider: false, AllowedForRefund: false);

    [Fact]
    public void DefinesUnderAWellFormedCodeAndATrimmedName()
    {
        var mode = PaymentMode.Define(BillingTestData.Id("card"), BillingTestData.Organisation, " CARD ", Card with { Name = "  Card  " }, BillingTestData.Now).Value;

        mode.Code.ShouldBe("CARD");
        mode.Name.ShouldBe("Card");
        mode.IsActive.ShouldBeTrue();
        mode.Branches.ShouldBeEmpty();
        mode.IsAvailableAt(BillingTestData.MainBranch).ShouldBeTrue("no branches listed means every branch");

        PaymentMode.Define(BillingTestData.Id("x"), BillingTestData.Organisation, "card", Card, BillingTestData.Now).Error.Code.ShouldBe("billing.code-not-well-formed");
        PaymentMode.Define(BillingTestData.Id("x"), BillingTestData.Organisation, " ", Card, BillingTestData.Now).Error.Code.ShouldBe("billing.value-required");
        PaymentMode.Define(BillingTestData.Id("x"), BillingTestData.Organisation, "CARD", Card with { Name = " " }, BillingTestData.Now).Error.Code.ShouldBe("billing.value-required");
        PaymentMode.Define(BillingTestData.Id("x"), BillingTestData.Organisation, "CARD", Card with { Name = new string('n', PaymentMode.MaximumNameLength + 1) }, BillingTestData.Now).Error.Code.ShouldBe("billing.value-too-long");
        PaymentMode.Define(BillingTestData.Id("x"), BillingTestData.Organisation, "CARD", null, BillingTestData.Now).Error.Code.ShouldBe("billing.value-required");
    }

    [Fact]
    public void RestrictsToBranchesDeactivatesAndReportsWhetherAnythingMoved()
    {
        var mode = PaymentMode.Define(BillingTestData.Id("card"), BillingTestData.Organisation, "CARD", Card, BillingTestData.Now).Value;
        var later = BillingTestData.Now.AddMinutes(5);

        mode.Describe(Card, later, null).Value.ShouldBeFalse("the same details move nothing");
        mode.UpdatedAt.ShouldBe(BillingTestData.Now);
        mode.Describe(Card with { AllowedForRefund = true }, later, null).Value.ShouldBeTrue();
        mode.AllowedForRefund.ShouldBeTrue();
        mode.UpdatedAt.ShouldBe(later);

        mode.SetBranches([BillingTestData.SecondBranch, BillingTestData.SecondBranch], later, null).ShouldBeTrue();
        mode.Branches.Select(branch => branch.BranchId).ShouldBe([BillingTestData.SecondBranch]);
        mode.IsAvailableAt(BillingTestData.SecondBranch).ShouldBeTrue();
        mode.IsAvailableAt(BillingTestData.MainBranch).ShouldBeFalse("restricted to the second branch");
        mode.SetBranches([BillingTestData.SecondBranch], later, null).ShouldBeFalse();
        mode.SetBranches([], later, null).ShouldBeTrue();
        mode.IsAvailableAt(BillingTestData.MainBranch).ShouldBeTrue();

        mode.SetActive(false, later, null).ShouldBeTrue();
        mode.IsAvailableAt(BillingTestData.MainBranch).ShouldBeFalse("deactivated stops new use everywhere");
        mode.SetActive(false, later, null).ShouldBeFalse();
    }

    [Fact]
    public void TheSeededSetIsOneTheDomainAcceptsWithCashTheOnlyRefundableMode()
    {
        SeededPaymentModes.All.Select(seeded => seeded.Code).ShouldBe(["CASH", "CARD", "UPI", "BANK_TRANSFER", "OTHER"]);
        foreach (var seeded in SeededPaymentModes.All)
        {
            PaymentMode.Define(BillingTestData.Id(seeded.Code), BillingTestData.Organisation, seeded.Code, seeded.Details, BillingTestData.Now).IsSuccess.ShouldBeTrue(seeded.Code);
        }

        SeededPaymentModes.All.Where(seeded => seeded.AllowedForRefund).Select(seeded => seeded.Code).ShouldBe([PaymentModeCodes.Cash]);
        SeededPaymentModes.All.ShouldAllBe(seeded => !seeded.RequiresProvider, "no gateway is chosen (OD-03)");
    }
}
