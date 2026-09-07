using Shouldly;
using Tailor360.Modules.Identity.Domain.Lockout;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The progressive lockout. The shape of the curve is the whole point: it has to make online guessing
/// hopeless without handing anyone a way to lock a colleague out of their till.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LockoutPolicyTests
{
    private static readonly DateTimeOffset Now = IdentityTestData.Now;

    [Fact]
    public void NoLockoutAppliesBelowTheThreshold()
    {
        var policy = LockoutPolicy.Default;

        for (var failures = 0; failures < policy.Threshold; failures++)
        {
            policy.ComputeLockoutEnd(failures, Now).ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(7, 4)]
    [InlineData(8, 8)]
    [InlineData(9, 16)]
    public void TheLockoutDoublesWithEachFurtherFailure(int failures, int expectedMinutes)
        => LockoutPolicy.Default.ComputeLockoutEnd(failures, Now)
            .ShouldBe(Now.AddMinutes(expectedMinutes));

    [Fact]
    public void TheLockoutStopsGrowingAtTheCeiling()
    {
        var policy = LockoutPolicy.Default;

        policy.ComputeLockoutEnd(10, Now).ShouldBe(Now + policy.MaximumDuration);
        policy.ComputeLockoutEnd(500, Now).ShouldBe(Now + policy.MaximumDuration);
    }

    [Fact]
    public void APolicyThatWouldDisableTheControlIsRefused()
    {
        LockoutPolicy.Create(0, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30)).IsFailure.ShouldBeTrue();
        LockoutPolicy.Create(5, TimeSpan.Zero, TimeSpan.FromMinutes(30)).IsFailure.ShouldBeTrue();
        LockoutPolicy.Create(5, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1)).IsFailure.ShouldBeTrue();
    }
}
