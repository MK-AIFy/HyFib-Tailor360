using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// What happens when the server stops waiting for a request: the answer the caller gets, and the state
/// the database is left in.
/// </summary>
/// <remarks>
/// The requirement is not "a timeout returns 504"; it is that a request the server abandoned changed
/// nothing. Asserting only the status code would pass just as happily over a half-written order.
/// </remarks>
/// <param name="application">The probe application.</param>
[Collection(CommandSafetyCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RequestTimeoutTests(CommandSafetyApplication application)
{
    /// <summary>Gate used by the skip conditions on every test in this class.</summary>
    public static bool Available => CommandSafetyApplication.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(RequestTimeoutTests))]
    public async Task ARequestTheServerGaveUpOnLeavesNothingHalfWritten()
    {
        using var client = application.ClientFor("clerk-1");

        (await application.SlowWriteCountAsync()).ShouldBe(0);

        using var response = await client.PostAsync(
            new Uri("/probe/slow-write", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(RequestProblems.Timeout);

        // A refusal a client can act on rather than a bare status line, and one that says nothing about
        // what the server was doing when it gave up.
        problem.RootElement.GetProperty("retryable").GetBoolean().ShouldBeTrue();
        problem.RootElement.TryGetProperty("detail", out var detail).ShouldBeTrue();
        detail.GetString().ShouldNotBeNull().ShouldNotContain("Exception");

        // The handler had already written its row and was waiting to commit when the timeout cancelled
        // it. Cancellation rolls the transaction back, so nothing survives.
        (await application.SlowWriteCountAsync()).ShouldBe(0);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(RequestTimeoutTests))]
    public async Task ATimedOutCommandDoesNotFreeItsKeyForAnImmediateRetry()
    {
        var key = Guid.CreateVersion7().ToString("D");
        var payment = new PaymentRequest(Guid.CreateVersion7(), 100);
        using var client = application.ClientFor("clerk-2");

        // The duplicate's wait happens inside the endpoint, so it spends the retry's own timeout budget —
        // and so does the database round-trip each poll makes, which is the part that is easy to forget.
        // The margin is asserted rather than assumed: a future change to either number should fail here,
        // naming the reason, instead of surfacing as a 504 on a loaded machine that reads like a flake.
        // Fifty milliseconds against the two seconds this endpoint is given is the same shape as the
        // five-second default against thirty.
        application.RequestOptions.DuplicateWaitBudget = TimeSpan.FromMilliseconds(50);
        application.RequestOptions.DuplicatePollInterval = TimeSpan.FromMilliseconds(25);

        application.RequestOptions.DuplicateWaitBudget
            .ShouldBeLessThan(CommandSafetyApplication.SlowCommandTimeout / 4);

        try
        {
            using var timedOut = await SendAsync(client, key, payment, TestContext.Current.CancellationToken);
            timedOut.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);

            // A timeout tells the caller nothing about whether the command took effect, so the retry is
            // told to wait rather than allowed to run a second time on a guess.
            using var retry = await SendAsync(client, key, payment, TestContext.Current.CancellationToken);
            retry.StatusCode.ShouldBe(HttpStatusCode.Conflict);

            var body = await retry.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var problem = JsonDocument.Parse(body);
            problem.RootElement.GetProperty("code").GetString().ShouldBe(IdempotencyProblems.InProgress);
        }
        finally
        {
            // The options are the shared application's, so a failure here must not shorten the wait for
            // every test that runs after it.
            application.RequestOptions.DuplicateWaitBudget = TimeSpan.FromSeconds(5);
            application.RequestOptions.DuplicatePollInterval = TimeSpan.FromMilliseconds(100);
        }
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(RequestTimeoutTests))]
    public async Task TheIdempotencyLeaseOutlivesTheLongestRequestTimeout()
    {
        // A claim taken over while its first holder was still working would run the command twice at
        // once, which is the failure the lease exists to prevent rather than to cause.
        application.StoreOptions.InFlightLease.ShouldBeGreaterThan(RequestTimeoutPolicies.CommandTimeout);
        await Task.CompletedTask;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string key,
        PaymentRequest payment,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/probe/payments/slow")
        {
            Content = JsonContent.Create(payment),
        };

        request.Headers.Add(IdempotencyHeaders.Key, key);
        return client.SendAsync(request, cancellationToken);
    }
}
