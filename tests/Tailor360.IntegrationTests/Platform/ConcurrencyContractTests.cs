using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// The <c>ETag</c> and <c>If-Match</c> contract of <c>docs/architecture/conventions.md</c> section 4.2,
/// over a real row whose concurrency token is PostgreSQL's <c>xmin</c>.
/// </summary>
/// <remarks>
/// The rule being protected is that two people editing one record from two counters must not have one of
/// them silently overwrite the other. The conflict answer is therefore tested for what it <em>carries</em>
/// as much as for its status: a 409 that does not name the current version leaves the client with nothing
/// to show the person except "no".
/// </remarks>
/// <param name="application">The probe application.</param>
[Collection(CommandSafetyCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ConcurrencyContractTests(CommandSafetyApplication application)
{
    /// <summary>Gate used by the skip conditions on every test in this class.</summary>
    public static bool Available => CommandSafetyApplication.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(ConcurrencyContractTests))]
    public async Task AReadPublishesTheVersionAndAnUpdateMadeAgainstItSucceeds()
    {
        var key = await SeedAsync("agreed");
        using var client = application.ClientFor("supervisor-1");

        var tag = await ReadTagAsync(client, key);
        tag.ShouldNotBeNullOrEmpty();

        using var update = await PutAsync(client, key, tag, enabled: true);

        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The response carries the new version, so a client that saves twice in a row does not have to
        // re-read between the two.
        update.Headers.ETag.ShouldNotBeNull();
        update.Headers.ETag!.ToString().ShouldNotBe(tag);
        (await application.ReadFlagAsync(key)).Enabled.ShouldBeTrue();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(ConcurrencyContractTests))]
    public async Task AnUpdateThatNamesNoVersionIsRefused()
    {
        var key = await SeedAsync("noprecondition");
        using var client = application.ClientFor("supervisor-2");

        using var update = await PutAsync(client, key, ifMatch: null, enabled: true);

        // 428 rather than 400 is recorded decision COD-05: the client can tell "you forgot the
        // precondition", which it fixes by re-reading, from "your payload is wrong", which it cannot.
        update.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await CodeOf(update)).ShouldBe(ConcurrencyProblems.PreconditionRequired);
        (await application.ReadFlagAsync(key)).Enabled.ShouldBeFalse();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(ConcurrencyContractTests))]
    public async Task AWeakOrMalformedPreconditionIsRefusedAsAMalformedRequest()
    {
        var key = await SeedAsync("weakvalidator");
        using var client = application.ClientFor("supervisor-3");

        // A weak validator says "semantically equivalent", which is not a basis on which to overwrite
        // somebody else's edit, so it never matches and is not treated as one that could.
        using var weak = await PutAsync(client, key, "W/\"1\"", enabled: true);

        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(weak)).ShouldBe(ConcurrencyProblems.PreconditionMalformed);
        (await application.ReadFlagAsync(key)).Enabled.ShouldBeFalse();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(ConcurrencyContractTests))]
    public async Task AnUpdateMadeAgainstAStaleVersionIsRefusedAndToldTheCurrentOne()
    {
        var key = await SeedAsync("stale");
        using var first = application.ClientFor("supervisor-4");
        using var second = application.ClientFor("supervisor-5");

        // Both people open the record.
        var openedTag = await ReadTagAsync(first, key);

        // The first saves.
        using var saved = await PutAsync(first, key, openedTag, enabled: true, reason: "Turned on at the counter.");
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The second saves against what they opened, which is no longer what is stored.
        using var conflicted = await PutAsync(second, key, openedTag, enabled: false, reason: "Turned off upstairs.");

        conflicted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(conflicted)).ShouldBe("platform.version-conflict");

        var body = await BodyOf(conflicted);
        var currentVersion = body.GetProperty("currentVersion").GetString();
        var currentEtag = body.GetProperty("currentEtag").GetString();

        // Without the current version the client can only say "no". With it, it can show both values and
        // let the person choose, which is what the design system requires of a conflict.
        currentVersion.ShouldNotBeNullOrEmpty();
        currentEtag.ShouldBe($"\"{currentVersion}\"");
        currentEtag.ShouldBe(saved.Headers.ETag!.ToString());

        // The first person's change stands; the second's was refused, not merged and not lost silently.
        var stored = await application.ReadFlagAsync(key);
        stored.Enabled.ShouldBeTrue();
        stored.Reason.ShouldBe("Turned on at the counter.");

        // Re-reading and saving again is all it takes to recover.
        using var retried = await PutAsync(second, key, currentEtag, enabled: false, reason: "Turned off upstairs.");
        retried.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await application.ReadFlagAsync(key)).Enabled.ShouldBeFalse();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(ConcurrencyContractTests))]
    public async Task TheWildcardPreconditionMeansAnyVersionOfARecordThatExists()
    {
        var key = await SeedAsync("wildcard");
        using var client = application.ClientFor("supervisor-6");

        using var update = await PutAsync(client, key, "*", enabled: true);

        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await application.ReadFlagAsync(key)).Enabled.ShouldBeTrue();
    }

    private async Task<string> SeedAsync(string suffix)
    {
        var key = $"probe.{suffix}.{Guid.CreateVersion7():N}";
        await application.SeedFlagAsync(key);
        return key;
    }

    private static async Task<string> ReadTagAsync(HttpClient client, string key)
    {
        using var response = await client.GetAsync(
            new Uri($"/probe/flags/{key}", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return response.Headers.ETag!.ToString();
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient client,
        string key,
        string? ifMatch,
        bool enabled,
        string reason = "Changed by the concurrency probe.")
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/probe/flags/{key}")
        {
            Content = JsonContent.Create(new FlagUpdate(enabled, reason)),
        };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await BodyOf(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
