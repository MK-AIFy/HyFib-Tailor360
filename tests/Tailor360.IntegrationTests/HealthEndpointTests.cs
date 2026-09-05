using System.Net;
using System.Text.Json;
using Shouldly;

namespace Tailor360.IntegrationTests;

/// <summary>
/// The three probes, exercised over HTTP. Their exact semantics matter operationally: readiness removes
/// an instance from rotation, so it must answer for the instance and not for a shared dependency.
/// </summary>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class HealthEndpointTests(WebApplicationFixture fixture)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health/startup")]
    public async Task ProbesAnswerWithoutASession(string path)
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProbesReportComponentStatusAndNothingElse()
    {
        using var client = fixture.CreateClient();

        var body = await client.GetStringAsync(
            new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.GetProperty("Status").GetString().ShouldBe("Healthy");
        root.TryGetProperty("Components", out var components).ShouldBeTrue();

        foreach (var component in components.EnumerateArray())
        {
            // A probe is reachable from the internal network without a session, so it must disclose a
            // name and a status and never a message, an exception or a configuration value.
            component.EnumerateObject().Select(p => p.Name)
                .ShouldBe(["Name", "Status"], ignoreOrder: true);
        }
    }

    [Fact]
    public async Task ProbeResponsesAreNotCached()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.CacheControl?.NoStore.ShouldBe(true);
    }

    [Fact]
    public async Task LivenessDoesNotDependOnAnyExternalComponent()
    {
        // Restarting an instance cannot fix a shared dependency, so liveness must stay green while a
        // dependency is down; a liveness check that called the database would turn one outage into a
        // restart loop across every instance.
        using var client = fixture.CreateClient();

        var body = await client.GetStringAsync(
            new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        body.ShouldNotContain("database", Case.Insensitive);
        body.ShouldNotContain("storage", Case.Insensitive);
    }
}
