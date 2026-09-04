using Shouldly;
using Tailor360.Cli.Commands;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The scope rules of <c>flags set</c>. Each case here writes a different row, and the difference
/// between them is the difference between a feature being on for one branch and on for the whole shop.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FlagScopeSelectionTests
{
    private static readonly Guid BranchId = Guid.Parse("0199a000-0000-7000-8000-00000000000a");

    [Fact]
    public void OrganisationScopeIsAlwaysKeyedByTheEmptyIdentity()
    {
        FlagScopeSelection.TryResolve(FeatureFlagScopes.Organisation, null, out var scopeId, out var error)
            .ShouldBeTrue();

        scopeId.ShouldBe(Guid.Empty);
        error.ShouldBeNull();
    }

    [Fact]
    public void BranchScopeStoresTheBranch()
    {
        FlagScopeSelection.TryResolve(FeatureFlagScopes.Branch, BranchId, out var scopeId, out var error)
            .ShouldBeTrue();

        scopeId.ShouldBe(BranchId);
        error.ShouldBeNull();
    }

    [Fact]
    public void ABranchScopeWithoutABranchIsRejected()
    {
        FlagScopeSelection.TryResolve(FeatureFlagScopes.Branch, null, out _, out var error).ShouldBeFalse();

        error.ShouldNotBeNull().ShouldContain("--branch");
    }

    [Fact]
    public void AnOrganisationScopeWithABranchIsRejected()
    {
        // Silently storing the branch would write a row the evaluator reads as organisation-wide,
        // turning the feature on everywhere the operator did not ask for, and a later organisation row
        // would then collide with it.
        FlagScopeSelection.TryResolve(FeatureFlagScopes.Organisation, BranchId, out _, out var error)
            .ShouldBeFalse();

        error.ShouldNotBeNull();
        error.ShouldContain("not accepted with --scope organisation");
    }

    [Theory]
    [InlineData("Organisation")]
    [InlineData("global")]
    [InlineData("")]
    public void AnUnknownScopeIsRejected(string scopeType)
    {
        FlagScopeSelection.TryResolve(scopeType, null, out _, out var error).ShouldBeFalse();

        error.ShouldNotBeNull().ShouldContain("must be");
    }
}
