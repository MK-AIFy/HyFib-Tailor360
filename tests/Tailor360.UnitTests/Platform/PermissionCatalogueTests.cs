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
    public void ComposesPermissionsFromEverySource()
    {
        var catalogue = new PermissionCatalogue([new ApplicationPermissions(), new FakeModulePermissions()]);

        catalogue.Contains(PlatformPermissions.OutboxReplay).ShouldBeTrue();
        catalogue.Contains(OrdersPermissions.Confirm).ShouldBeTrue();
        catalogue.Contains("fake.thing.do").ShouldBeTrue();

        // Asserted as a relationship rather than as a number: a count would have to be edited by every
        // pull request that adds a permission, which trains people to edit it without reading it.
        catalogue.All.Count.ShouldBe(ApplicationPermissions.All.Count + 1);
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
        var catalogue = new PermissionCatalogue([new ApplicationPermissions()]);

        catalogue.All.Select(p => p.Key).ShouldBe(catalogue.All.Select(p => p.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ReportsAnUnknownKeyAsAbsentRatherThanThrowing()
        => new PermissionCatalogue([]).Find("nothing.here").ShouldBeNull();

    [Fact]
    public void SensitivePlatformPermissionsDemandMultiFactorAndAReason()
    {
        var catalogue = new PermissionCatalogue([new ApplicationPermissions()]);

        var replay = catalogue.Find(PlatformPermissions.OutboxReplay).ShouldNotBeNull();
        replay.RequiresMfa.ShouldBeTrue();
        replay.RequiresReason.ShouldBeTrue();

        var flags = catalogue.Find(PlatformPermissions.FeatureFlags).ShouldNotBeNull();
        flags.RequiresMfa.ShouldBeTrue();
        flags.RequiresReason.ShouldBeTrue();
    }

    /// <summary>
    /// Step-up is a demand made of a session that has already answered a second factor, so a permission
    /// that asks for a fresh re-authentication and not for multi-factor at all would be asking for a
    /// factor the account may never have enrolled. The two flags are ordered, and the catalogue is the
    /// only place that can be checked.
    /// </summary>
    [Fact]
    public void EveryStepUpPermissionAlsoDemandsMultiFactor()
        => ApplicationPermissions.All
            .Where(permission => permission.RequiresStepUp && !permission.RequiresMfa)
            .Select(permission => permission.Key)
            .ShouldBeEmpty();

    /// <summary>
    /// A key is <c>&lt;area&gt;.&lt;action&gt;</c> in lower snake case, with an optional middle
    /// segment. The shape is what lets a key sit unquoted in the permission matrix, in a seed
    /// definition and in a test name without an escaping rule.
    /// </summary>
    [Fact]
    public void EveryKeyIsLowerCaseDottedAndDescribed()
    {
        foreach (var permission in ApplicationPermissions.All)
        {
            permission.Key.ShouldNotBeNullOrWhiteSpace();
            permission.Key.ShouldBe(permission.Key.ToLowerInvariant());
            permission.Key.Split('.').Length.ShouldBeInRange(2, 3, permission.Key);
            permission.Key.All(c => char.IsAsciiLetterLower(c) || c is '.' or '_')
                .ShouldBeTrue(permission.Key);
            permission.Description.ShouldNotBeNullOrWhiteSpace();
            permission.Module.ShouldNotBeNullOrWhiteSpace();
        }
    }

    /// <summary>
    /// The organisation-wide reach the branch-scope handler demands has to be a permission somebody can
    /// actually be granted. It was a bare string in the handler and declared nowhere until this issue,
    /// which meant a typo on either side would have been invisible.
    /// </summary>
    [Fact]
    public void DeclaresTheOrganisationWideReachTheBranchScopeHandlerDemands()
        => new PermissionCatalogue([new ApplicationPermissions()])
            .Contains(PlatformPermissions.ReadAllBranches)
            .ShouldBeTrue();

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
