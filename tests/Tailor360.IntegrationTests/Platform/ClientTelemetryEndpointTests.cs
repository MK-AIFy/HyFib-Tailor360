using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shouldly;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Web.Configuration;
using Tailor360.Web.Telemetry;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// <c>POST /api/v1/telemetry/client</c> (#52): a same-origin, anonymous, rate-limited route posted to
/// before a session may exist, whose only defence against carrying anything it should not is the
/// server-side allowlist. The negative cases carry the weight here — the positive path is the easy part
/// of an endpoint whose entire reason to exist is refusing what it does not recognise.
/// </summary>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClientTelemetryEndpointTests(WebApplicationFixture fixture)
{
    private const string Path = "/api/v1/telemetry/client";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AValidBatchOfAllowlistedEventsIsAccepted()
    {
        using var client = NewClient("203.0.113.10");

        var response = await PostAsync(client, ValidBatch());

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEventWithAnUnallowlistedAttributeIsAcceptedWithTheAttributeSilentlyDropped()
    {
        using var client = NewClient("203.0.113.11");

        // "personalData" is not a name ClientTelemetryAllowlist declares for web_vital. A batch carrying
        // it alongside an otherwise-valid event must still be accepted — the attribute is dropped, not
        // the event, and certainly not the batch.
        var batch = new
        {
            batchId = Guid.CreateVersion7(),
            clientVersion = "1.0.0",
            routeName = "orders/draft",
            deviceClass = "shop-floor-phone",
            engine = "blink",
            operatingSystemFamily = "android",
            events = new[]
            {
                new
                {
                    type = ClientTelemetryAllowlist.WebVital,
                    timestamp = DateTimeOffset.UtcNow,
                    attributes = new Dictionary<string, object>
                    {
                        ["metric"] = "LCP",
                        ["value"] = 120.5,
                        ["personalData"] = "radhika@example.in",
                    },
                },
            },
        };

        var response = await PostAsync(client, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ARequestWithNoDeclaredOriginSignalIsRefused()
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "203.0.113.12");

        // Neither Origin nor Sec-Fetch-Site: the gap OriginValidationMiddleware leaves open by design
        // (RequestOriginOptions.RequireDeclaredOrigin is false) and DeclaredOriginRequiredFilter closes
        // for this one anti-forgery-exempt route.
        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = JsonBody(ValidBatch()),
        };

        var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe("security.cross-site-request");
    }

    [Fact]
    public async Task ARequestDeclaringAnotherSiteAsItsOriginIsRefused()
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "203.0.113.13");

        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = JsonBody(ValidBatch()),
        };
        request.Headers.Add("Origin", "https://evil.example.com");

        var response = await client.SendAsync(request, Token);

        // OriginValidationMiddleware refuses this before the route's own filter is ever reached — the
        // same wire shape either way, which is deliberate.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe("security.cross-site-request");
    }

    [Fact]
    public async Task AnUnsupportedContentTypeIsRefused()
    {
        using var client = NewClient("203.0.113.14");

        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent("batchId=x", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };

        var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await CodeAsync(response)).ShouldBe("observability.telemetry-content-type-not-supported");
    }

    [Fact]
    public async Task ABodyOverTheConfiguredByteLimitIsRefusedBeforeItIsParsed()
    {
        using var client = NewClient("203.0.113.15");

        // Deliberately not valid JSON: the size check runs on the raw body before any parsing is
        // attempted, so an oversized request never reaches the point where its shape would matter.
        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(new string('x', 64_000), Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await CodeAsync(response)).ShouldBe("observability.telemetry-batch-oversize");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"batchId": null, "events": []}""")]
    [InlineData("not json at all")]
    public async Task AMalformedBatchIsRefused(string json)
    {
        using var client = NewClient("203.0.113.16");

        var response = await PostRawAsync(client, json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).ShouldBe("observability.telemetry-malformed-batch");
    }

    [Fact]
    public async Task ABatchOverTheConfiguredEventCountIsRefused()
    {
        using var client = NewClient("203.0.113.17");

        var batch = new
        {
            batchId = Guid.CreateVersion7(),
            events = Enumerable.Range(0, 51)
                .Select(_ => new { type = ClientTelemetryAllowlist.WebVital, attributes = (object?)null })
                .ToArray(),
        };

        var response = await PostAsync(client, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).ShouldBe("observability.telemetry-batch-oversize");
    }

    /// <summary>
    /// One test, deliberately: <see cref="Tailor360.Platform.Security.Audit.AuthorisationDenialCoalescer"/>
    /// is a process-wide singleton keyed on the actor and the refusal reason alone — not the batch — so
    /// two tests that each expected to write the first "unallowlisted" row of the run would be racing
    /// each other's coalescing window. Asserting the whole sequence here keeps it self-contained.
    /// </summary>
    [Fact]
    public async Task AnAcceptedBatchWritesNoRowAndARepeatedRefusalReasonIsCoalescedToOne()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var client = NewClient("203.0.113.18");
        using var scope = fixture.Services.CreateScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var countBefore = await RefusalCountAsync(platform);

        var accepted = await PostAsync(client, ValidBatch());
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await RefusalCountAsync(platform)).ShouldBe(countBefore);

        var firstBatchId = Guid.CreateVersion7();
        var first = await PostAsync(client, UnrecognisedTypeBatch(firstBatchId));
        first.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(first)).ShouldBe("observability.telemetry-no-allowlisted-events");

        var afterFirst = await platform.AuditEvents.AsNoTracking()
            .Where(entry => entry.Action == ClientTelemetryEndpoints.RefusedAction)
            .ToListAsync(Token);
        afterFirst.Count.ShouldBe(countBefore + 1);
        afterFirst.ShouldContain(entry => entry.EntityId == firstBatchId);

        // A second, differently-identified batch refused for the same reason inside the same minute is
        // coalesced: the coalescing key is the actor and the refusal reason, not the batch, exactly as
        // RateLimitPolicies coalesces a flood of 429s. Only the first is ever written.
        var second = await PostAsync(client, UnrecognisedTypeBatch(Guid.CreateVersion7()));
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RefusalCountAsync(platform)).ShouldBe(afterFirst.Count);
    }

    private static Task<int> RefusalCountAsync(PlatformDbContext platform)
        => platform.AuditEvents.AsNoTracking()
            .CountAsync(entry => entry.Action == ClientTelemetryEndpoints.RefusedAction, Token);

    [Fact]
    public async Task TheRateLimitPolicyRefusesAfterItsBudgetAndTheRefusalIsNotAudited()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var client = NewClient("203.0.113.21");

        HttpResponseMessage? refused = null;
        for (var attempt = 0; attempt < 45 && refused is null; attempt++)
        {
            var response = await PostAsync(client, ValidBatch());
            if (response.StatusCode is HttpStatusCode.TooManyRequests)
            {
                refused = response;
            }
        }

        refused.ShouldNotBeNull();
        refused.Headers.RetryAfter?.Delta.ShouldNotBeNull();

        using var scope = fixture.Services.CreateScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        // ClientTelemetryIngest is deliberately absent from RateLimitPolicies.AuditedPolicies: this
        // endpoint's traffic shape is ordinary devices flushing on a timer, not a credential guess, so a
        // rejection here is a capacity fact rather than something worth a durable row.
        var recorded = await platform.AuditEvents.AsNoTracking()
            .AnyAsync(entry => entry.Action == RateLimitPolicies.AuditAction && entry.Summary.Contains(Path), Token);

        recorded.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenIngestionIsDisabledEveryRequestIsAcceptedAndNothingIsEmitted()
    {
        var factory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{ClientTelemetryOptions.SectionName}:Enabled"] = "false",
                })));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "203.0.113.22");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

        // Not even valid JSON: a disabled endpoint answers before the body is read at all, so a client
        // never changes behaviour, and never learns anything, because an operator turned ingestion off.
        var response = await PostRawAsync(client, "not json at all");

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// The redaction test <c>docs/nfr/data-classification.md</c> section 5.18 names: a batch whose event
    /// carries six sentinel values — a customer name, a phone number, a measurement value, a full stack
    /// trace, a URL with a query string and a route parameter value — spread across both attribute names
    /// the allowlist declares (with a shape that does not match) and names it does not declare at all.
    /// The batch is still accepted, because a refused-looking event is silently dropped rather than
    /// failing the whole request; what this test proves is that none of the six sentinels reaches the log
    /// record the handler emits for the surviving event.
    /// </summary>
    /// <remarks>
    /// Captured at the <see cref="ILogger{ClientTelemetryHandler}"/> boundary itself — the same seam
    /// <c>ClientTelemetryHandler.Emit</c> writes through — rather than by re-parsing console output, so
    /// the assertion holds regardless of how Serilog subsequently renders or destructures the call. The
    /// counter <c>ClientTelemetryHandler</c> increments carries only the fixed event-type tag
    /// (<c>EventsAccepted.Add(1, ("type", candidate.Type))</c>) and no attribute data, and the handler
    /// starts no trace activity, so neither channel can carry a sentinel by construction — there is
    /// nothing there for a test to capture.
    /// </remarks>
    [Fact]
    public async Task ABatchCarryingSixSentinelValuesIsAcceptedAndNoneOfThemReachesTheEmittedLogRecord()
    {
        var capturingLogger = new CapturingLogger<ClientTelemetryHandler>();
        var factory = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<ILogger<ClientTelemetryHandler>>(capturingLogger))));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "203.0.113.40");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

        const string customerNameSentinel = "Priya Sharma";
        const string phoneNumberSentinel = "+91-98765-43210";
        const string measurementValueSentinel = "38.5cm chest";
        const string stackTraceSentinel =
            "TypeError: Cannot read properties of undefined (reading 'garmentId')\n" +
            "    at OrderDraftForm.render (OrderDraftForm.tsx:142:18)";
        const string urlWithQuerySentinel = "https://app.tailor360.example/orders/draft?ref=abc123";
        const string routeParameterValueSentinel = "orders/9876543210/draft";

        var batch = new
        {
            batchId = Guid.CreateVersion7(),
            clientVersion = "1.0.0",
            routeName = "orders/draft",
            deviceClass = "shop-floor-phone",
            engine = "blink",
            operatingSystemFamily = "android",
            events = new[]
            {
                new
                {
                    type = ClientTelemetryAllowlist.UnhandledError,
                    timestamp = DateTimeOffset.UtcNow,
                    attributes = new Dictionary<string, object>
                    {
                        // Declared names, wrong shapes: the allowlist's shape constraint is the thing
                        // under test here, not just the closed set of names.
                        ["messageCode"] = $"{customerNameSentinel} could not submit the order",
                        ["stackHash"] = stackTraceSentinel,
                        ["routeName"] = urlWithQuerySentinel,

                        // Names unhandled_error does not declare at all: dropped regardless of shape.
                        ["customerPhone"] = phoneNumberSentinel,
                        ["measurement"] = measurementValueSentinel,
                        ["routeParam"] = routeParameterValueSentinel,
                    },
                },
            },
        };

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonBody(batch) },
            Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(Token)).ShouldBeEmpty();

        var sentinels = new[]
        {
            customerNameSentinel,
            phoneNumberSentinel,
            measurementValueSentinel,
            stackTraceSentinel,
            urlWithQuerySentinel,
            routeParameterValueSentinel,
        };

        var emittedText = capturingLogger.RenderedValues();
        foreach (var sentinel in sentinels)
        {
            emittedText.ShouldNotContain(
                text => text.Contains(sentinel, StringComparison.Ordinal),
                $"sentinel '{sentinel}' must not reach a log record, but was found in: " +
                string.Join(" | ", emittedText));
        }
    }

    /// <summary>
    /// Captures every value passed to <c>ILogger.Log</c>, structured argument by structured argument,
    /// rather than only the rendered message — so a value nested inside the destructured
    /// <c>{@Attributes}</c> object (a <see cref="Dictionary{TKey,TValue}"/>, not a scalar) is inspected on
    /// its own terms and not lost inside a <c>ToString()</c> that would otherwise just print the type
    /// name.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _values = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _values.Add(formatter(state, exception));

            if (state is IEnumerable<KeyValuePair<string, object?>> structured)
            {
                foreach (var (_, value) in structured)
                {
                    Flatten(value);
                }
            }
        }

        public List<string> RenderedValues() => _values;

        private void Flatten(object? value)
        {
            switch (value)
            {
                case null:
                    return;
                case string text:
                    _values.Add(text);
                    return;
                case IEnumerable<KeyValuePair<string, object?>> nested:
                    foreach (var (_, nestedValue) in nested)
                    {
                        Flatten(nestedValue);
                    }

                    return;
                default:
                    _values.Add(value.ToString() ?? string.Empty);
                    return;
            }
        }
    }

    private HttpClient NewClient(string address)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, address);

        // The browser-set signal this route accepts in place of Origin — sufficient on its own to pass
        // both the global cross-site check and this route's own declared-origin filter.
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        return client;
    }

    private static object ValidBatch(Guid? batchId = null) => new
    {
        batchId = batchId ?? Guid.CreateVersion7(),
        clientVersion = "1.0.0",
        routeName = "orders/draft",
        deviceClass = "shop-floor-phone",
        engine = "blink",
        operatingSystemFamily = "android",
        events = new[]
        {
            new
            {
                type = ClientTelemetryAllowlist.WebVital,
                timestamp = DateTimeOffset.UtcNow,
                attributes = new Dictionary<string, object>
                {
                    ["metric"] = "LCP",
                    ["value"] = 120.5,
                },
            },
        },
    };

    private static object UnrecognisedTypeBatch(Guid batchId) => new
    {
        batchId,
        events = new[] { new { type = "not_a_real_event_type", attributes = (object?)null } },
    };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, object batch)
        => await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonBody(batch) },
            Token);

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string json)
        => await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, Path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            },
            Token);

    private static JsonContent JsonBody(object batch) => JsonContent.Create(batch, options: JsonOptions);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return problem.GetProperty("code").GetString();
    }
}
