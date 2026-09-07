using Shouldly;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Customers;

namespace Tailor360.UnitTests.Customers;

/// <summary>The invariants of a customer record.</summary>
[Trait("Category", "Unit")]
public sealed class CustomerTests
{
    [Fact]
    public void ARegisteredCustomerIsActiveAndVisibleAtTheBranchThatCreatedIt()
    {
        var customer = CustomersTestData.Registered();

        customer.Status.ShouldBe(CustomerStatus.Active);
        customer.OwningBranchId.ShouldBe(CustomersTestData.Branch);
        customer.IsVisibleTo(CustomersTestData.Branch).ShouldBeTrue();
        customer.IsVisibleTo(CustomersTestData.OtherBranch).ShouldBeFalse();
    }

    [Fact]
    public void RegistrationStoresTheNameAsGivenAndTheFoldedKeyBesideIt()
    {
        // The name is displayed exactly as the customer gave it. The folded key is never displayed and
        // exists only so that a search and a duplicate score have something to compare.
        var customer = CustomersTestData.Registered("Bhuvaneshwari Karthik");

        customer.DisplayName.ShouldBe("Bhuvaneshwari Karthik");
        customer.NormalisedName.ShouldBe("buvanesvari kartik");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ACustomerCannotBeRegisteredWithoutANumber(string? number)
    {
        var result = Customer.Register(
            CustomersTestData.Id("nameless"),
            CustomersTestData.Organisation,
            number,
            CustomersTestData.Branch,
            CustomersTestData.Details(),
            CustomersTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("customers.value-required");
    }

    [Fact]
    public void ACustomerCannotBeRegisteredOutsideABranch()
    {
        // The customer number comes from a branch sequence, so there is no record without one.
        var result = Customer.Register(
            CustomersTestData.Id("branchless"),
            CustomersTestData.Organisation,
            "C-CBE01-000001",
            Guid.Empty,
            CustomersTestData.Details(),
            CustomersTestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CustomersErrors.NoBranchInContext);
    }

    [Fact]
    public void CorrectingTheNameKeepsTheOldOneAsASearchableAlias()
    {
        // The reason a marriage does not create a second record: a search for what she used to be
        // called still finds her.
        var customer = CustomersTestData.Registered("Kavitha Raman");
        var aliasId = CustomersTestData.Id("alias");

        var result = customer.Correct(
            CustomersTestData.Details("Kavitha Murugan"),
            CustomersTestData.Now.AddDays(1),
            CustomersTestData.Actor,
            aliasId);

        result.IsSuccess.ShouldBeTrue();
        customer.DisplayName.ShouldBe("Kavitha Murugan");
        var alias = customer.Aliases.ShouldHaveSingleItem();
        alias.Kind.ShouldBe(CustomerAliasKind.PreviousName);
        alias.Value.ShouldBe("Kavitha Raman");
        alias.NormalisedValue.ShouldBe("kavita raman");
    }

    [Fact]
    public void CorrectingSomethingOtherThanTheNameRecordsNoAlias()
    {
        var customer = CustomersTestData.Registered();

        customer.Correct(
            CustomersTestData.Details(phone: "94420 66315"),
            CustomersTestData.Now.AddHours(1),
            CustomersTestData.Actor,
            CustomersTestData.Id("unused-alias"));

        customer.Aliases.ShouldBeEmpty();
        customer.PhoneE164.ShouldBe("+919442066315");
    }

    [Fact]
    public void ACorrectionNeverChangesWhichRecordThisIs()
    {
        var customer = CustomersTestData.Registered();
        var id = customer.Id;
        var number = customer.CustomerNumber;
        var owner = customer.OwningBranchId;

        customer.Correct(
            CustomersTestData.Details("Kavitha Murugan", "94420 66315"),
            CustomersTestData.Now.AddDays(2),
            CustomersTestData.Actor,
            CustomersTestData.Id("alias-2"));

        customer.Id.ShouldBe(id);
        customer.CustomerNumber.ShouldBe(number);
        customer.OwningBranchId.ShouldBe(owner);
    }

    [Fact]
    public void ADeactivatedRecordIsNotCorrected()
    {
        // Correcting a withdrawn record would quietly bring it back into use without anybody deciding.
        var customer = CustomersTestData.Registered();
        customer.Deactivate(CustomersTestData.Now, CustomersTestData.Actor);

        var result = customer.Correct(
            CustomersTestData.Details("Kavitha Murugan"),
            CustomersTestData.Now,
            CustomersTestData.Actor,
            CustomersTestData.Id("alias-3"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("customers.status-transition-not-allowed");
    }

    [Fact]
    public void DeactivationAndReactivationAreEachRefusedTwice()
    {
        var customer = CustomersTestData.Registered();

        customer.Deactivate(CustomersTestData.Now, CustomersTestData.Actor).IsSuccess.ShouldBeTrue();
        customer.Deactivate(CustomersTestData.Now, CustomersTestData.Actor).IsFailure.ShouldBeTrue();
        customer.DeactivatedAt.ShouldBe(CustomersTestData.Now);

        customer.Reactivate(CustomersTestData.Now, CustomersTestData.Actor).IsSuccess.ShouldBeTrue();
        customer.Reactivate(CustomersTestData.Now, CustomersTestData.Actor).IsFailure.ShouldBeTrue();
        customer.DeactivatedAt.ShouldBeNull();
        customer.Status.ShouldBe(CustomerStatus.Active);
    }

    [Fact]
    public void ServingTheCustomerAtASecondBranchAddsThatBranchToVisibilityOnce()
    {
        // branch-scenarios.md section 3: the record is organisation-wide and its visibility is
        // branch-scoped. Opening it at a second branch adds that branch; doing so again changes
        // nothing, and the caller is told so it knows there is nothing to audit.
        var customer = CustomersTestData.Registered();

        customer.MakeVisibleTo(CustomersTestData.OtherBranch, CustomersTestData.Now, CustomersTestData.Actor)
            .ShouldBeTrue();
        customer.MakeVisibleTo(CustomersTestData.OtherBranch, CustomersTestData.Now, CustomersTestData.Actor)
            .ShouldBeFalse();

        customer.Visibility.Count.ShouldBe(2);
        customer.IsVisibleTo(CustomersTestData.OtherBranch).ShouldBeTrue();
    }

    [Fact]
    public void AnEmptyBranchIsNeverAddedToVisibility()
    {
        var customer = CustomersTestData.Registered();

        customer.MakeVisibleTo(Guid.Empty, CustomersTestData.Now, CustomersTestData.Actor).ShouldBeFalse();
        customer.Visibility.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("hi-IN")]
    [InlineData("en-GB")]
    [InlineData("klingon")]
    public void ALanguageTheProductDoesNotServeIsRefused(string language)
    {
        var result = CustomerDetails.Create(
            "Kavitha Raman", null, "98430 21174", null, null, null, null, null, language);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("customers.language-not-supported");
    }

    [Fact]
    public void TheDefaultLanguageIsUsedWhenNobodyChoseOne()
    {
        CustomersTestData.Details().Language.ShouldBe(CustomerDetails.DefaultLanguage);
    }

    [Fact]
    public void AnEmailWithoutAnAtSignIsRefused()
    {
        var result = CustomerDetails.Create(
            "Kavitha Raman", null, "98430 21174", null, "not-an-address", null, null, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Target.ShouldBe("email");
    }
}
