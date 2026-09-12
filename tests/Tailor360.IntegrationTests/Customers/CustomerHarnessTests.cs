using System.Globalization;
using Shouldly;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// The invariant <see cref="CustomerHarness.UniquePhone"/> depends on: every number it hands out is
/// new on the six digits the counter search actually compares, not merely on the ten-digit number as a
/// whole (issue #125).
/// </summary>
/// <remarks>
/// <c>CustomerHarness.UniquePhone</c>'s own remarks describe why a randomly drawn run prefix could not
/// deliver this — two runs shared tail space whenever their prefixes agreed in their last two digits,
/// one pair in a hundred — and why a counter seeded from the database, rather than a narrower draw,
/// closes it. These tests hold the fixture to the exact property it now claims, structurally: every
/// call strictly increases the tail, which makes a repeat impossible rather than merely unlikely.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class CustomerHarnessTests
{
    /// <summary>The number of consecutive calls the monotonic check draws.</summary>
    private const int Calls = 2_000;

    [Fact]
    public void TwoConsecutiveCallsDifferInTheSearchTail()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var first = CustomerHarness.UniquePhone();
        var second = CustomerHarness.UniquePhone();

        first.ShouldNotBe(second);
        Tail(first).ShouldNotBe(Tail(second));
    }

    /// <summary>
    /// Proves the fix structurally rather than statistically: a scheme that only made collisions rarer
    /// would still, eventually, hand back a tail already seen. A scheme that counts, seeded from what
    /// every earlier run already claimed, cannot — each call's tail is strictly greater than the one
    /// before it, in the one process and against every one that came before.
    /// </summary>
    [Fact]
    public void UniquePhoneTailsIncreaseStrictlyAcrossManyCalls()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var previous = Tail(CustomerHarness.UniquePhone());
        var seen = new HashSet<int> { previous };

        for (var i = 0; i < Calls; i++)
        {
            var current = Tail(CustomerHarness.UniquePhone());

            current.ShouldBeGreaterThan(previous, $"call {i} repeated or went backwards");
            seen.Add(current).ShouldBeTrue($"call {i} reused tail {current:D6}");

            previous = current;
        }

        seen.Count.ShouldBe(Calls + 1);
    }

    private static int Tail(string phone) =>
        int.Parse(phone[^6..], NumberStyles.None, CultureInfo.InvariantCulture);
}
