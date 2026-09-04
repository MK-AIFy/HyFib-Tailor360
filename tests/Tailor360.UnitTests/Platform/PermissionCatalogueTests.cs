using Shouldly;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The permission catalogue is the single list of what the application can authorise. Two modules
/// claiming one key would make the effective rule depend on registration order, so that is rejected.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PermissionCatalogueTests
{
    [Fact]
    public void ComposesPermissionsFromEveryModule()
    {
        var catalogue = new PermissionCatalogue([new PlatformPermissions(), new FakeModulePermissions()]);

        catalogue.Contains(PlatformPermissions.OutboxReplay).ShouldBeTrue();
        catalogue.Contains("fake.thing.do").ShouldBeTrue();
        catalogue.All.Count.ShouldBe(5);
    }

    [Fact]
    public void RejectsTwoModulesClaimingTheSameKey()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new PermissionCatalogue([new FakeModulePermissions(), new ConflictingPermissions()]));

        exception.Message.ShouldContain("fake.thing.do");
        exception.Message.ShouldContain("more than one module");
    }

    [Fact]
    public void ReturnsPermissionsInAStableOrder()
    {
        var catalogue = new PermissionCatalogue([new PlatformPermissions()]);

        catalogue.All.Select(p => p.Key).ShouldBe(catalogue.All.Select(p => p.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ReportsAnUnknownKeyAsAbsentRatherThanThrowing()
        => new PermissionCatalogue([]).Find("nothing.here").ShouldBeNull();

    [Fact]
    public void SensitivePlatformPermissionsDemandMultiFactorAndAReason()
    {
        var catalogue = new PermissionCatalogue([new PlatformPermissions()]);

        var replay = catalogue.Find(PlatformPermissions.OutboxReplay).ShouldNotBeNull();
        replay.RequiresMfa.ShouldBeTrue();
        replay.RequiresReason.ShouldBeTrue();

        var flags = catalogue.Find(PlatformPermissions.FeatureFlags).ShouldNotBeNull();
        flags.RequiresMfa.ShouldBeTrue();
        flags.RequiresReason.ShouldBeTrue();
    }

    [Fact]
    public void MapsAKeyToItsPolicyNameAndBack()
    {
        var policy = PermissionPolicy.NameFor("orders.order.confirm");

        policy.ShouldBe("perm:orders.order.confirm");
        PermissionPolicy.IsPermissionPolicy(policy).ShouldBeTrue();
        PermissionPolicy.KeyFrom(policy).ShouldBe("orders.order.confirm");
        PermissionPolicy.IsPermissionPolicy("SomeNamedPolicy").ShouldBeFalse();
    }

    private sealed class FakeModulePermissions : IPermissionSource
    {
        public IReadOnlyCollection<Permission> Permissions =>
            [new("fake.thing.do", "Do the thing.", "Fake")];
    }

    private sealed class ConflictingPermissions : IPermissionSource
    {
        public IReadOnlyCollection<Permission> Permissions =>
            [new("fake.thing.do", "Also do the thing.", "Other")];
    }
}
