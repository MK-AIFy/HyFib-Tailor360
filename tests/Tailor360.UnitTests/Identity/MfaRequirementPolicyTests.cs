using Shouldly;
using Tailor360.Modules.Identity.Application.Mfa;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// Who has to have a second factor. Issue #23 names Owner, Admin and Cashier plus anyone holding
/// <c>billing.*</c> or <c>admin.*</c>, "configurable per role" — so the defaults and the configurability
/// are both worth a test.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MfaRequirementPolicyTests
{
    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    [InlineData("Cashier")]
    [InlineData("owner")]
    public void TheRolesIssue23NamesRequireASecondFactor(string role)
        => Policy().IsRequiredFor([role], []).ShouldBeTrue();

    [Theory]
    [InlineData("Tailor")]
    [InlineData("Reception")]
    [InlineData("Delivery")]
    public void ARoleThatHandlesNeitherMoneyNorAdministrationDoesNot(string role)
        => Policy().IsRequiredFor([role], ["orders.read", "custody.scan"]).ShouldBeFalse();

    [Fact]
    public void ACustomRoleGrantedABillingOrAdminPermissionStillRequiresOne()
    {
        // This is the case a role list alone gets wrong. A shop that invents "Senior cashier" and grants
        // it the permission to post an invoice has created an account that must have a second factor,
        // and nobody had to remember to add the role to a list for that to be true.
        var policy = Policy();

        policy.IsRequiredFor(["Senior cashier"], ["billing.post_invoice"]).ShouldBeTrue();
        policy.IsRequiredFor(["Floor lead"], ["admin.users"]).ShouldBeTrue();
        policy.ReasonFor(["Senior cashier"], ["billing.post_invoice"])
            .ShouldBe("the billing.post_invoice permission");
    }

    [Fact]
    public void APermissionTheCatalogueFlagsRequiresOneEvenWithoutAMatchingPrefix()
    {
        // #24 flags individual permissions such as payments.refund that no prefix would catch. The flag
        // travels on the permission, so the policy reads the catalogue rather than guessing from names.
        var catalogue = new PermissionCatalogue([new FlaggedPermissions()]);
        var policy = new MfaRequirementPolicy(TestOptions.For(new MfaOptions()), catalogue);

        policy.IsRequiredFor(["Tailor"], ["payments.refund"]).ShouldBeTrue();
        policy.IsRequiredFor(["Tailor"], ["payments.record"]).ShouldBeFalse();
    }

    [Fact]
    public void ADeploymentCanWidenTheRuleWithoutACodeChange()
    {
        var options = new MfaOptions();
        options.RequiredRoles.Add("Auditor");

        var policy = new MfaRequirementPolicy(TestOptions.For(options), Empty());

        policy.IsRequiredFor(["Auditor"], []).ShouldBeTrue();
        policy.ReasonFor(["Auditor"], []).ShouldBe("the Auditor role");
    }

    [Fact]
    public void ConfiguringRolesReplacesTheDefaultsRatherThanAddingToThem()
    {
        // The configuration binder appends to a list, so the defaults are applied only when the
        // configured list is empty. A deployment that named one role would otherwise silently get four.
        var options = new MfaOptions();
        options.RequiredRoles.Add("Owner");

        var policy = new MfaRequirementPolicy(TestOptions.For(options), Empty());

        policy.IsRequiredFor(["Owner"], []).ShouldBeTrue();
        policy.IsRequiredFor(["Cashier"], []).ShouldBeFalse();
    }

    [Fact]
    public void ADeploymentCanRequireASecondFactorOfEveryone()
        => new MfaRequirementPolicy(
                TestOptions.For(new MfaOptions { RequiredForEveryone = true }), Empty())
            .ReasonFor(["Tailor"], [])
            .ShouldBe("every account in this deployment");

    [Fact]
    public void AnAccountWithNoRolesAndNoPermissionsNeedsNothing()
        => Policy().ReasonFor([], []).ShouldBeNull();

    private static MfaRequirementPolicy Policy()
        => new(TestOptions.For(new MfaOptions()), Empty());

    private static PermissionCatalogue Empty() => new([]);

    private sealed class FlaggedPermissions : IPermissionSource
    {
        public IReadOnlyCollection<Permission> Permissions =>
        [
            new("payments.refund", "Refund a payment", "Billing", RequiresMfa: true, RequiresStepUp: true),
            new("payments.record", "Record a payment", "Billing"),
        ];
    }
}
