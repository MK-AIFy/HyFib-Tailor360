using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// The retry contract, exercised as HTTP requests against a real database.
/// </summary>
/// <remarks>
/// The bar every one of these is written against is the same sentence: a client that retries a payment
/// because the network dropped must never create a second payment. Each test names the way that could go
/// wrong — a repeat, a duplicate arriving at once, a key reused for something else, a request that died
/// halfway — and asserts the number of payments as well as the status code, because a correct status over
/// a double charge would be the worst of both.
/// </remarks>
/// <param name="application">The probe application.</param>
[Collection(CommandSafetyCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IdempotentCommandTests(CommandSafetyApplication application)
{
    /// <summary>Gate used by the skip conditions on every test in this class.</summary>
    public static bool Available => CommandSafetyApplication.IsAvailable;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task ARetriedPaymentReturnsTheFirstReceiptAndTakesNoSecondPayment()
    {
        var customer = Guid.CreateVersion7();
        var key = NewKey();
        using var client = application.ClientFor("cashier-1");

        using var first = await PayAsync(client, key, customer, 1500, Token);
        using var retry = await PayAsync(client, key, customer, 1500, Token);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The same receipt, down to the payment identifier: a second identifier would mean a second
        // payment, whatever the status code said.
        var firstReceipt = await first.Content.ReadFromJsonAsync<PaymentReceipt>(Token);
        var retriedReceipt = await retry.Content.ReadFromJsonAsync<PaymentReceipt>(Token);
        retriedReceipt.ShouldBe(firstReceipt);

        retry.Headers.GetValues(IdempotencyHeaders.Replayed).ShouldBe(["true"]);
        first.Headers.Contains(IdempotencyHeaders.Replayed).ShouldBeFalse();

        (await application.PaymentCountAsync(customer)).ShouldBe(1);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task TheSameKeyForADifferentRequestIsRefusedRatherThanAnsweredWithTheFirstResult()
    {
        var customer = Guid.CreateVersion7();
        var key = NewKey();
        using var client = application.ClientFor("cashier-2");

        (await PayAsync(client, key, customer, 1500, Token)).Dispose();
        using var reused = await PayAsync(client, key, customer, 2500, Token);

        // Answering with the first receipt would silently swallow a second, different payment. Running it
        // would break the promise the key made. Neither is acceptable, so the client is told it is wrong.
        reused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(reused)).ShouldBe(IdempotencyProblems.KeyReused);
        (await application.PaymentCountAsync(customer)).ShouldBe(1);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task ADuplicateArrivingWhileTheFirstIsStillRunningWaitsForItAndReplaysIt()
    {
        var customer = Guid.CreateVersion7();
        var key = NewKey();
        using var client = application.ClientFor("cashier-3");

        application.ResetGate();
        SetWait(TimeSpan.FromSeconds(5));

        var first = PayAsync(client, key, customer, 900, Token, "/probe/payments/gated");
        await application.HandlerReachedGate.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        // The cashier taps twice. The second tap must not start a second payment, and must not be told
        // "already in progress" when the first is about to answer.
        var duplicate = PayAsync(client, key, customer, 900, Token, "/probe/payments/gated");
        application.GateOpened.TrySetResult();

        using var firstResponse = await first;
        using var duplicateResponse = await duplicate;

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        duplicateResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        duplicateResponse.Headers.GetValues(IdempotencyHeaders.Replayed).ShouldBe(["true"]);
        (await application.PaymentCountAsync(customer)).ShouldBe(1);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task ADuplicateStillWaitingWhenTheBudgetRunsOutIsToldToComeBack()
    {
        var customer = Guid.CreateVersion7();
        var key = NewKey();
        using var client = application.ClientFor("cashier-4");

        application.ResetGate();
        SetWait(TimeSpan.FromMilliseconds(200));

        var first = PayAsync(client, key, customer, 700, Token, "/probe/payments/gated");
        await application.HandlerReachedGate.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        using var duplicate = await PayAsync(client, key, customer, 700, Token, "/probe/payments/gated");

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(duplicate)).ShouldBe(IdempotencyProblems.InProgress);

        // A refusal a client can act on: it says when to come back rather than leaving it to guess.
        duplicate.Headers.RetryAfter.ShouldNotBeNull();

        application.GateOpened.TrySetResult();
        (await first).Dispose();

        // One payment, taken by the first attempt. The duplicate never ran.
        (await application.PaymentCountAsync(customer)).ShouldBe(1);
        SetWait(TimeSpan.FromSeconds(5));
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task APaymentWhoseConnectionDroppedIsNeverTakenTwice()
    {
        var customer = Guid.CreateVersion7();
        var key = NewKey();
        using var client = application.ClientFor("cashier-5");

        application.ResetGate();
        SetWait(TimeSpan.FromMilliseconds(200));

        // The shop's connection drops with the request in flight, before the payment was written.
        using var dropped = new CancellationTokenSource();
        var abandoned = PayAsync(client, key, customer, 4000, dropped.Token, "/probe/payments/gated");
        await application.HandlerReachedGate.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await dropped.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () => (await abandoned).Dispose());

        // The cashier presses the button again straight away. The first attempt's outcome is unknown, so
        // the honest answer is "wait" — never "run it again and hope".
        using (var immediate = await PayAsync(client, key, customer, 4000, Token, "/probe/payments/gated"))
        {
            immediate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeOf(immediate)).ShouldBe(IdempotencyProblems.InProgress);
        }

        (await application.PaymentCountAsync(customer)).ShouldBe(0);

        // Once the abandoned claim's lease has run out, the retry takes it over and the payment is taken.
        application.ResetGate();
        application.GateOpened.TrySetResult();
        application.Clock.Advance(application.StoreOptions.InFlightLease + TimeSpan.FromSeconds(1));

        using (var recovered = await PayAsync(client, key, customer, 4000, Token, "/probe/payments/gated"))
        {
            recovered.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await application.PaymentCountAsync(customer)).ShouldBe(1);
        SetWait(TimeSpan.FromSeconds(5));
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task ARefusedCommandLeavesItsKeyFreeToCorrectAndSendAgain()
    {
        var customer = Guid.CreateVersion7();
        var key = NewKey();
        using var client = application.ClientFor("cashier-6");

        using (var refused = await PayAsync(client, key, customer, 0, Token))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // The design system's answer to a refusal is "fix it and send it again with the same key". A
        // stored refusal would replay itself forever and make that impossible.
        using var corrected = await PayAsync(client, key, customer, 1200, Token);

        corrected.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await application.PaymentCountAsync(customer)).ShouldBe(1);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task ACommandThatFailedForUnknownReasonsHoldsItsKeyUntilTheLeaseRunsOut()
    {
        var key = NewKey();
        using var client = application.ClientFor("cashier-7");

        SetWait(TimeSpan.FromMilliseconds(200));

        using (var failed = await SendAsync(client, "/probe/payments/failing", key, content: null, Token))
        {
            failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        // Whether the command took effect before it fell over is unknown, and running it again on a guess
        // is the one thing that must not happen.
        using (var immediate = await SendAsync(client, "/probe/payments/failing", key, content: null, Token))
        {
            immediate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeOf(immediate)).ShouldBe(IdempotencyProblems.InProgress);
        }

        application.Clock.Advance(application.StoreOptions.InFlightLease + TimeSpan.FromSeconds(1));

        using (var afterLease = await SendAsync(client, "/probe/payments/failing", key, content: null, Token))
        {
            // It ran again, which is what the lease is for: a key held by a process that will never come
            // back has to become usable eventually.
            afterLease.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        SetWait(TimeSpan.FromSeconds(5));
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task OnePersonsKeyIsNotAnotherPersonsReplay()
    {
        var customer = Guid.CreateVersion7();
        var key = NewKey();
        using var cashier = application.ClientFor("cashier-8");
        using var manager = application.ClientFor("manager-8");

        using var theirs = await PayAsync(cashier, key, customer, 300, Token);
        using var mine = await PayAsync(manager, key, customer, 300, Token);

        theirs.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Keying by the client value alone would have answered the manager with the cashier's receipt and
        // lost a payment.
        mine.StatusCode.ShouldBe(HttpStatusCode.Created);
        mine.Headers.Contains(IdempotencyHeaders.Replayed).ShouldBeFalse();
        (await application.PaymentCountAsync(customer)).ShouldBe(2);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task ACommandWithNoKeyIsRefused()
    {
        using var client = application.ClientFor("cashier-9");

        using var response = await SendAsync(
            client, "/probe/payments", key: null, PaymentBody(100), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(response)).ShouldBe(IdempotencyProblems.KeyRequired);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task AKeyThatIsNotAUuidIsRefused()
    {
        using var client = application.ClientFor("cashier-10");

        using var response = await SendAsync(client, "/probe/payments", "till-3", PaymentBody(100), Token);

        // A key a client made up from its own state — a till number, a customer name — would collide with
        // itself the next time that till took a payment.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(response)).ShouldBe(IdempotencyProblems.KeyInvalid);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(IdempotentCommandTests))]
    public async Task AnUnauthenticatedCallerIsRefusedRatherThanRecordedAgainstNobody()
    {
        using var response = await SendAsync(
            application.AnonymousClient, "/probe/payments", NewKey(), PaymentBody(100), Token);

        // Records are keyed by the caller. An anonymous claim would let one stranger's key replay
        // another's, so the request is refused rather than recorded against nobody.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static string NewKey() => Guid.CreateVersion7().ToString("D");

    private static JsonContent PaymentBody(int amount)
        => JsonContent.Create(new PaymentRequest(Guid.CreateVersion7(), amount));

    private void SetWait(TimeSpan budget)
    {
        application.RequestOptions.DuplicateWaitBudget = budget;
        application.RequestOptions.DuplicatePollInterval = TimeSpan.FromMilliseconds(25);
    }

    private static Task<HttpResponseMessage> PayAsync(
        HttpClient client,
        string key,
        Guid customerId,
        int amount,
        CancellationToken cancellationToken,
        string path = "/probe/payments")
        => SendAsync(
            client,
            path,
            key,
            JsonContent.Create(new PaymentRequest(customerId, amount)),
            cancellationToken);

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string path,
        string? key,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };

        if (key is not null)
        {
            request.Headers.Add(IdempotencyHeaders.Key, key);
        }

        return client.SendAsync(request, cancellationToken);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
