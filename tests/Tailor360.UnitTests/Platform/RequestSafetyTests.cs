using System.Text;
using Shouldly;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Idempotency;
using Tailor360.Platform.Persistence.Idempotency;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The two values the retry and concurrency contracts are decided by: the fingerprint that says whether
/// two requests are the same request, and the entity tag that says whether an update was made against
/// the version that is stored.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RequestSafetyTests
{
    [Fact]
    public void TwoIdenticalRequestsFingerprintTheSame()
    {
        var first = RequestFingerprint.Of("POST", "/api/v1/orders/7/confirm", string.Empty, Body("""{"note":"a"}"""));
        var second = RequestFingerprint.Of("POST", "/api/v1/orders/7/confirm", string.Empty, Body("""{"note":"a"}"""));

        second.ShouldBe(first);
    }

    [Fact]
    public void TheSameKeyOnTwoDifferentResourcesIsNotTheSameRequest()
    {
        // Records are keyed by the route template, so both of these are "POST /api/v1/orders/{id}/confirm".
        // If the path were not in the fingerprint, confirming order 8 under the key already used for
        // order 7 would be answered with order 7's response and order 8 would never be confirmed.
        var seven = RequestFingerprint.Of("POST", "/api/v1/orders/7/confirm", string.Empty, []);
        var eight = RequestFingerprint.Of("POST", "/api/v1/orders/8/confirm", string.Empty, []);

        eight.ShouldNotBe(seven);
    }

    [Fact]
    public void TheQueryTheMethodAndTheBodyAllChangeTheFingerprint()
    {
        var baseline = RequestFingerprint.Of("POST", "/api/v1/payments", "?branch=1", Body("""{"amount":100}"""));

        RequestFingerprint.Of("PUT", "/api/v1/payments", "?branch=1", Body("""{"amount":100}"""))
            .ShouldNotBe(baseline);
        RequestFingerprint.Of("POST", "/api/v1/payments", "?branch=2", Body("""{"amount":100}"""))
            .ShouldNotBe(baseline);
        RequestFingerprint.Of("POST", "/api/v1/payments", "?branch=1", Body("""{"amount":200}"""))
            .ShouldNotBe(baseline);
    }

    [Fact]
    public void MovingCharactersAcrossThePartsDoesNotProduceTheSameFingerprint()
    {
        // Concatenating the parts without a separator would make these two the same request, and a client
        // that reused a key across them would have the second one silently swallowed.
        RequestFingerprint.Of("POST", "/a/b", string.Empty, [])
            .ShouldNotBe(RequestFingerprint.Of("POST", "/a", "/b", []));
    }

    [Fact]
    public void TheFingerprintIsAHashAndNeverTheRequest()
    {
        var fingerprint = RequestFingerprint.Of(
            "POST", "/api/v1/customers", string.Empty, Body("""{"phone":"9876500000"}"""));

        // The record lives for a week. A body kept in it verbatim would be a week of personal data in a
        // platform table nobody classified.
        fingerprint.Length.ShouldBe(64);
        fingerprint.ShouldNotContain("9876500000");
        fingerprint.ShouldAllBe(character => Uri.IsHexDigit(character) && !char.IsUpper(character));
    }

    [Fact]
    public void TheStoreAndTheFilterAgreeOnHowABodyIsHashed()
        => IdempotencyStore.Fingerprint("""{"amount":100}""")
            .ShouldBe(RequestFingerprint.OfBody("""{"amount":100}"""));

    [Fact]
    public void AnEntityTagIsTheRowVersionInQuotes()
    {
        var tag = EntityTag.From(48213);

        tag.Version.ShouldBe("48213");
        tag.Value.ShouldBe("\"48213\"");
    }

    [Fact]
    public void ATagSurvivesBeingSentAndReadBack()
    {
        var sent = EntityTag.From(48213);

        EntityTag.TryParse(sent.Value, out var received).ShouldBeTrue();
        received.ShouldBe(sent);
        received.Matches(sent).ShouldBeTrue();
    }

    [Fact]
    public void AStaleTagDoesNotMatch()
    {
        EntityTag.From(48213).Matches(EntityTag.From(48219)).ShouldBeFalse();
    }

    [Fact]
    public void TheWildcardMatchesAnyVersion()
    {
        EntityTag.TryParse("*", out var wildcard).ShouldBeTrue();

        wildcard.IsAny.ShouldBeTrue();
        wildcard.Matches(EntityTag.From(48213)).ShouldBeTrue();
        wildcard.Value.ShouldBe("*");
    }

    [Theory]
    [InlineData("W/\"48213\"")]
    [InlineData("48213")]
    [InlineData("\"48213\", \"48219\"")]
    [InlineData("\"\"")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnythingThatIsNotOneStrongTagIsNotATag(string header)
    {
        // A weak validator means "semantically equivalent", which is not a basis on which to overwrite
        // somebody else's edit; the rest are not entity tags at all. Accepting any of them loosely would
        // turn the precondition into a formality.
        EntityTag.TryParse(header, out _).ShouldBeFalse();
    }

    /// <summary>
    /// The lease outlives the timeout of the commands it is paired with — which is the command timeout,
    /// not the longest timeout in the catalogue.
    /// </summary>
    /// <remarks>
    /// This test used to be named for the longest timeout while comparing against the command timeout,
    /// and the two are not the same: the lease is 45 seconds and the catalogue publishes a 60-second
    /// report timeout and a 120-second export timeout. Nothing stopped an endpoint pairing idempotency
    /// with either, and a claim taken over while its first holder was still working runs one command
    /// twice at once — which the fencing on the store does not prevent, because both executions are
    /// real.
    ///
    /// Raising the lease above 120 seconds would be the wrong fix: the lease is also how long a crashed
    /// process holds a key, and a customer at a counter whose payment died should not wait two minutes
    /// to retry. The pairing is bounded per endpoint instead, by
    /// <c>EndpointRequestSafetyTests.AnIdempotentEndpointFinishesInsideItsClaimLease</c>.
    /// </remarks>
    [Fact]
    public void TheClaimLeaseOutlivesACommandsTimeout()
    {
        // A claim taken over while its first holder is still working would run one command twice at once.
        new IdempotencyOptions().InFlightLease.ShouldBeGreaterThan(RequestTimeoutPolicies.CommandTimeout);

        // And the honest statement of what is *not* covered here, so the gap is visible rather than
        // implied by a name: the catalogue holds timeouts this lease does not outlive.
        RequestTimeoutPolicies.ExportTimeout.ShouldBeGreaterThan(new IdempotencyOptions().InFlightLease);
    }

    [Fact]
    public void TheDuplicateWaitFitsInsideACommandsTimeoutBudget()
    {
        // The wait is spent inside the duplicate's own request. Longer than the timeout and the duplicate
        // would be answered "that took too long" instead of "that is already being carried out".
        new IdempotencyRequestOptions().DuplicateWaitBudget.ShouldBeLessThan(RequestTimeoutPolicies.CommandTimeout);
    }

    [Fact]
    public void RecordsOutliveTheLongestOfflineQueue()
    {
        var options = new IdempotencyOptions { MaximumOfflineQueueAge = TimeSpan.FromDays(5) };

        // A record deleted before its replay could arrive would let the command run a second time.
        options.Retention.ShouldBeGreaterThan(options.MaximumOfflineQueueAge);
    }

    private static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);
}
