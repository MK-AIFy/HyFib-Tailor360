using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Tailor360.Platform.Observability.Health;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// <see cref="HealthDetailResponseWriter"/> is the one place a health check's own words reach an
/// unauthenticated-adjacent response, so these tests are about what it refuses to carry as much as
/// about what it reports.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HealthDetailResponseWriterTests
{
    [Fact]
    public async Task ReportsEveryEntryWithItsStatusDurationDescriptionAndTags()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal)
            {
                ["database"] = new(
                    HealthStatus.Healthy,
                    description: null,
                    duration: TimeSpan.FromMilliseconds(12),
                    exception: null,
                    data: null,
                    tags: ["ready"]),
                ["outbox"] = new(
                    HealthStatus.Degraded,
                    description: "The oldest undelivered outbox message is older than the agreed bound.",
                    duration: TimeSpan.FromMilliseconds(34),
                    exception: null,
                    data: null,
                    tags: ["non-essential"]),
            },
            totalDuration: TimeSpan.FromMilliseconds(50));

        var payload = await WriteAsync(report, "2026.9.1+abc123");

        payload.GetProperty("Status").GetString().ShouldBe("Degraded");
        payload.GetProperty("DurationMs").GetInt32().ShouldBe(50);
        payload.GetProperty("Version").GetString().ShouldBe("2026.9.1+abc123");

        var components = payload.GetProperty("Components").EnumerateArray().ToList();
        components.Count.ShouldBe(2);

        var database = components.Single(c => c.GetProperty("Name").GetString() == "database");
        database.GetProperty("Status").GetString().ShouldBe("Healthy");
        database.GetProperty("DurationMs").GetInt32().ShouldBe(12);
        database.GetProperty("Description").ValueKind.ShouldBe(JsonValueKind.Null);
        database.GetProperty("Tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["ready"]);

        var outbox = components.Single(c => c.GetProperty("Name").GetString() == "outbox");
        outbox.GetProperty("Status").GetString().ShouldBe("Degraded");
        outbox.GetProperty("Description").GetString().ShouldBe(
            "The oldest undelivered outbox message is older than the agreed bound.");
        outbox.GetProperty("Tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["non-essential"]);
    }

    /// <summary>
    /// The failure scenario this whole type exists to prevent: a check that failed with an exception
    /// carrying a connection string, a hostname or a caller-supplied value must not put a byte of it on
    /// the wire. Only a check's own hand-written <c>Description</c> is ever read.
    /// </summary>
    [Fact]
    public async Task NeverWritesAThrownExceptionsMessage()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal)
            {
                ["database"] = new(
                    HealthStatus.Unhealthy,
                    description: "The database did not accept a connection.",
                    duration: TimeSpan.FromMilliseconds(5),
                    exception: new InvalidOperationException(
                        "Host=db-primary.internal;Username=tailor360;Password=super-secret-value"),
                    data: null,
                    tags: ["ready"]),
            },
            totalDuration: TimeSpan.FromMilliseconds(5));

        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        await HealthDetailResponseWriter.WriteAsync(context, report, "1.0.0");

        body.Position = 0;
        var raw = Encoding.UTF8.GetString(body.ToArray());

        raw.ShouldNotContain("super-secret-value");
        raw.ShouldNotContain("db-primary.internal");
        raw.ShouldNotContain("InvalidOperationException");
        raw.ShouldContain("The database did not accept a connection.");
    }

    [Fact]
    public async Task ANullDescriptionIsWrittenAsNullRatherThanOmitted()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal)
            {
                ["self"] = new(
                    HealthStatus.Healthy,
                    description: null,
                    duration: TimeSpan.Zero,
                    exception: null,
                    data: null,
                    tags: ["live"]),
            },
            totalDuration: TimeSpan.Zero);

        var payload = await WriteAsync(report, "1.0.0");

        payload.GetProperty("Components")[0].GetProperty("Description").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task SetsNoStoreAndJsonContentType()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal), TimeSpan.Zero);

        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        await HealthDetailResponseWriter.WriteAsync(context, report, "1.0.0");

        context.Response.ContentType.ShouldBe("application/json; charset=utf-8");
        context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
    }

    private static async Task<JsonElement> WriteAsync(HealthReport report, string buildVersion)
    {
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        await HealthDetailResponseWriter.WriteAsync(context, report, buildVersion);

        body.Position = 0;
        return await JsonSerializer.DeserializeAsync<JsonElement>(body);
    }
}
