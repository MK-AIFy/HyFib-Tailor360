using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// The workflow definition drafting surface's own HTTP behaviour (#243): the create-draft-put-read
/// cycle, cloning, the authorisation matrix, a stale tag, a replayed <c>Idempotency-Key</c>, a `PUT`
/// against a published version, and a `PUT` whose graph fails the six checks.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class WorkflowAdministrationEndpointTests(WebApplicationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static int _addressCounter;

    [Fact]
    public async Task AnOwnerCreatesADraftPutsTheBlousePhaseGraphAndReadsItBack()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("wf-happy");
        var definitionId = await CreateDefinitionAsync(owner, "happy");
        var version = await CreateVersionAsync(owner, definitionId, cloneFromVersionId: null);

        // docs/prd/workflows/blouse.md section 2.1: the six job phases E06-F02-1 settles on — Cutting,
        // Specialist work (Aari only, so optional), Stitching, Finishing, QC, Ready.
        var put = await owner.PutAsync(
            VersionRoute(definitionId, version.Id), BlouseWorkflowGraph(), [.. Key(), ("If-Match", version.ETag)]);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync(Token));
        put.Headers.ETag.ShouldNotBeNull();

        using var putBody = JsonDocument.Parse(await put.Content.ReadAsStringAsync(Token));
        putBody.RootElement.GetProperty("status").GetString().ShouldBe("Draft");
        putBody.RootElement.GetProperty("phases").GetArrayLength().ShouldBe(6);
        putBody.RootElement.GetProperty("transitions").GetArrayLength().ShouldBe(6);
        putBody.RootElement.GetProperty("categoryKeys")[0].GetString().ShouldBe("blouse");

        var read = await owner.GetAsync(VersionRoute(definitionId, version.Id));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();
        using var readBody = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));
        // The same graph, end to end through the API: same phase codes in the same relative order, the
        // optional specialist-work phase marked as such, and Ready alone terminal.
        var codes = readBody.RootElement.GetProperty("phases").EnumerateArray()
            .OrderBy(phase => phase.GetProperty("ordinal").GetInt32())
            .Select(phase => phase.GetProperty("code").GetString())
            .ToArray();
        codes.ShouldBe(["CUTTING", "SPECIALIST_WORK", "STITCHING", "FINISHING", "QC", "READY"]);

        var specialistWork = readBody.RootElement.GetProperty("phases").EnumerateArray()
            .First(phase => phase.GetProperty("code").GetString() == "SPECIALIST_WORK");
        specialistWork.GetProperty("isOptional").GetBoolean().ShouldBeTrue();

        var readyPhase = readBody.RootElement.GetProperty("phases").EnumerateArray()
            .First(phase => phase.GetProperty("code").GetString() == "READY");
        readyPhase.GetProperty("isTerminal").GetBoolean().ShouldBeTrue();

        var list = await owner.GetAsync("/api/v1/orders/workflow-definitions");
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync(Token));
        var listed = listBody.RootElement.EnumerateArray()
            .First(element => element.GetProperty("id").GetGuid() == definitionId);
        listed.GetProperty("versions").GetArrayLength().ShouldBe(1);
        listed.GetProperty("versions")[0].GetProperty("tag").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ANewDraftClonesAnExistingVersionsWholeGraph()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("wf-clone");
        var definitionId = await CreateDefinitionAsync(owner, "clone");
        var first = await CreateVersionAsync(owner, definitionId, cloneFromVersionId: null);

        (await owner.PutAsync(
                VersionRoute(definitionId, first.Id), ThreePhaseGraph(), [.. Key(), ("If-Match", first.ETag)]))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var cloned = await CreateVersionAsync(owner, definitionId, cloneFromVersionId: first.Id);
        cloned.Id.ShouldNotBe(first.Id);

        var read = await owner.GetAsync(VersionRoute(definitionId, cloned.Id));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));
        body.RootElement.GetProperty("versionNumber").GetInt32().ShouldBe(2);
        body.RootElement.GetProperty("phases").GetArrayLength().ShouldBe(3);
        body.RootElement.GetProperty("transitions").GetArrayLength().ShouldBe(2);
        body.RootElement.GetProperty("categoryKeys")[0].GetString().ShouldBe("blouse");
    }

    [Fact]
    public async Task AReplayedIdempotencyKeyOnCreateVersionAnswersTheFirstDraftAgain()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("wf-idem");
        var definitionId = await CreateDefinitionAsync(owner, "idem");
        var key = Key();

        var first = await owner.PostAsync(
            $"/api/v1/orders/workflow-definitions/{definitionId}/versions",
            new { cloneFromVersionId = (Guid?)null },
            key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(Token));
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync(Token));
        var firstId = firstBody.RootElement.GetProperty("id").GetGuid();

        var replay = await owner.PostAsync(
            $"/api/v1/orders/workflow-definitions/{definitionId}/versions",
            new { cloneFromVersionId = (Guid?)null },
            key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created, await replay.Content.ReadAsStringAsync(Token));
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync(Token));
        replayBody.RootElement.GetProperty("id").GetGuid().ShouldBe(firstId);

        var list = await owner.GetAsync("/api/v1/orders/workflow-definitions");
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync(Token));
        listBody.RootElement.EnumerateArray()
            .First(element => element.GetProperty("id").GetGuid() == definitionId)
            .GetProperty("versions").GetArrayLength().ShouldBe(1, "the replay must not have drafted a second version");
    }

    [Fact]
    public async Task AStalePutIsRefusedAndTheStoredDraftIsUntouched()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("wf-stale");
        var definitionId = await CreateDefinitionAsync(owner, "stale");
        var version = await CreateVersionAsync(owner, definitionId, cloneFromVersionId: null);

        (await owner.PutAsync(
                VersionRoute(definitionId, version.Id), ThreePhaseGraph(), [.. Key(), ("If-Match", version.ETag)]))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // version.ETag is now stale: the successful PUT above moved the row's own tag.
        var stale = await owner.PutAsync(
            VersionRoute(definitionId, version.Id),
            ThreePhaseGraph(categoryKey: "sari"),
            [.. Key(), ("If-Match", version.ETag)]);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(stale)).ShouldBe("orders.concurrent-change");
        stale.Headers.ETag.ShouldNotBeNull();

        var read = await owner.GetAsync(VersionRoute(definitionId, version.Id));
        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));
        body.RootElement.GetProperty("categoryKeys")[0].GetString().ShouldBe(
            "blouse", "the stale write must not have reached the stored row");
    }

    [Fact]
    public async Task APutWhoseGraphHasAnUnreachablePhaseAndAPhaseWithNoRoleReportsBothAndSavesNothing()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("wf-invalid");
        var definitionId = await CreateDefinitionAsync(owner, "invalid");
        var version = await CreateVersionAsync(owner, definitionId, cloneFromVersionId: null);

        // START is the graph's only phase with no incoming transition and has no outbound edge of its
        // own; ORPHAN_A and ORPHAN_B transition only to each other, so neither is reachable from START.
        // ORPHAN_A also names no role that may work it — the two findings the acceptance criterion asks
        // for, deliberately produced together rather than one at a time.
        var invalid = new
        {
            phases = new object[]
            {
                new
                {
                    code = "START", displayName = "Start", ordinal = 0,
                    requiredRoleKeys = new[] { "cutter" }, requiresEvidence = false,
                    expectedDuration = (string?)null, sla = (string?)null,
                    isOptional = false, isSkippable = false, isTerminal = true,
                },
                new
                {
                    code = "ORPHAN_A", displayName = "Orphan A", ordinal = 1,
                    requiredRoleKeys = Array.Empty<string>(), requiresEvidence = false,
                    expectedDuration = (string?)null, sla = (string?)null,
                    isOptional = false, isSkippable = false, isTerminal = false,
                },
                new
                {
                    code = "ORPHAN_B", displayName = "Orphan B", ordinal = 2,
                    requiredRoleKeys = new[] { "tailor" }, requiresEvidence = false,
                    expectedDuration = (string?)null, sla = (string?)null,
                    isOptional = false, isSkippable = false, isTerminal = false,
                },
            },
            transitions = new object[]
            {
                new { fromPhaseCode = "ORPHAN_A", toPhaseCode = "ORPHAN_B" },
                new { fromPhaseCode = "ORPHAN_B", toPhaseCode = "ORPHAN_A" },
            },
            categoryKeys = new[] { "blouse" },
        };

        var put = await owner.PutAsync(
            VersionRoute(definitionId, version.Id), invalid, [.. Key(), ("If-Match", version.ETag)]);
        put.StatusCode.ShouldBe(HttpStatusCode.Conflict, await put.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(put)).ShouldBe("orders.workflow-graph-invalid");

        using var body = JsonDocument.Parse(await put.Content.ReadAsStringAsync(Token));
        var findingCodes = body.RootElement.GetProperty("findings").EnumerateArray()
            .Select(finding => finding.GetProperty("code").GetString())
            .ToArray();
        findingCodes.ShouldContain("orders.workflow-graph-unreachable-phase");
        findingCodes.ShouldContain("orders.workflow-graph-phase-without-required-role");

        var errors = body.RootElement.GetProperty("errors");
        errors.TryGetProperty("ORPHAN_A", out _).ShouldBeTrue();
        errors.TryGetProperty("ORPHAN_B", out _).ShouldBeTrue();

        var read = await owner.GetAsync(VersionRoute(definitionId, version.Id));
        using var readBody = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));
        readBody.RootElement.GetProperty("phases").GetArrayLength().ShouldBe(0, "the invalid graph must not have been saved");
    }

    [Fact]
    public async Task APutAgainstAPublishedVersionIsRefusedWithAConflictNamingTheStatus()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("wf-published");
        var definitionId = await CreateDefinitionAsync(owner, "published");
        var version = await CreateVersionAsync(owner, definitionId, cloneFromVersionId: null);

        (await owner.PutAsync(
                VersionRoute(definitionId, version.Id), ThreePhaseGraph(), [.. Key(), ("If-Match", version.ETag)]))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await PublishDirectlyAsync(definitionId, version.Id);

        var currentTag = (await owner.GetAsync(VersionRoute(definitionId, version.Id))).Headers.ETag!.ToString();

        var refused = await owner.PutAsync(
            VersionRoute(definitionId, version.Id), ThreePhaseGraph(), [.. Key(), ("If-Match", currentTag)]);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(refused)).ShouldBe("orders.workflow-version-not-editable");

        var read = await owner.GetAsync(VersionRoute(definitionId, version.Id));
        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));
        body.RootElement.GetProperty("status").GetString().ShouldBe("Published");
        body.RootElement.GetProperty("phases").GetArrayLength().ShouldBe(3, "the published row must be byte-identical");
    }

    [Fact]
    public async Task ACallerWithNoPermissionIsRefusedOnEveryWorkflowRoute()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("wf-perm-owner");
        var definitionId = await CreateDefinitionAsync(owner, "perm");
        var version = await CreateVersionAsync(owner, definitionId, cloneFromVersionId: null);

        using var stranger = await AdministrationHarness.AdministratorAsync(
            fixture, "wf-perm-none", NextAddress(), grantPermission: null);

        (await stranger.GetAsync("/api/v1/orders/workflow-definitions"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.PostAsync(
                "/api/v1/orders/workflow-definitions",
                new { code = "X_UNAUTHORISED", name = "Unauthorised", description = (string?)null },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.GetAsync(VersionRoute(definitionId, version.Id)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.PutAsync(
                VersionRoute(definitionId, version.Id), ThreePhaseGraph(), [.. Key(), ("If-Match", version.ETag)]))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.PostAsync(
                $"/api/v1/orders/workflow-definitions/{definitionId}/versions",
                new { cloneFromVersionId = (Guid?)null },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task PublishDirectlyAsync(Guid definitionId, Guid versionId)
    {
        // Publishing is E06-F02-3's own route; this slice models the aggregate's Publish but exposes no
        // HTTP path to it yet, so the fixture reaches it the way PublishedTemplateAsync-style fixtures
        // elsewhere in this suite reach a handler directly rather than over HTTP.
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var definition = await store.FindAsync(definitionId, SessionTestData.OrganisationId, Token);
        definition.ShouldNotBeNull();

        var published = definition!.Publish(
            versionId, clock.UtcNow, null, "Fixture publish for an integration test.");
        published.IsSuccess.ShouldBeTrue(published.IsFailure ? published.Error.Message : string.Empty);

        var saved = await store.SaveAsync(Token);
        saved.IsSuccess.ShouldBeTrue(saved.IsFailure ? saved.Error.Message : string.Empty);
    }

    private Task<AdministrationHarness.AdministratorClient> OwnerAsync(string prefix)
        => AdministrationHarness.AdministratorAsync(fixture, prefix, NextAddress(), CatalogPermissions.EditWorkflows);

    private static async Task<Guid> CreateDefinitionAsync(AdministrationHarness.AdministratorClient owner, string stem)
    {
        var code = $"WF_{stem.Replace('-', '_').ToUpperInvariant()}_{AdministrationHarness.UniqueToken(6).ToUpperInvariant()}";

        var response = await owner.PostAsync(
            "/api/v1/orders/workflow-definitions",
            new { code, name = $"Synthetic process {stem}", description = "Written by an integration test." },
            Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, string ETag)> CreateVersionAsync(
        AdministrationHarness.AdministratorClient owner, Guid definitionId, Guid? cloneFromVersionId)
    {
        var response = await owner.PostAsync(
            $"/api/v1/orders/workflow-definitions/{definitionId}/versions",
            new { cloneFromVersionId },
            Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        response.Headers.ETag.ShouldNotBeNull();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return (body.RootElement.GetProperty("id").GetGuid(), response.Headers.ETag!.ToString());
    }

    private static string VersionRoute(Guid definitionId, Guid versionId)
        => $"/api/v1/orders/workflow-definitions/{definitionId}/versions/{versionId}";

    /// <summary>A valid, publishable three-phase graph: cut, stitch, inspect.</summary>
    private static object ThreePhaseGraph(string categoryKey = "blouse") => new
    {
        phases = new object[]
        {
            new
            {
                code = "CUTTING", displayName = "Cutting", ordinal = 0,
                requiredRoleKeys = new[] { "cutter" }, requiresEvidence = false,
                expectedDuration = "02:00:00", sla = (string?)null,
                isOptional = false, isSkippable = false, isTerminal = false,
            },
            new
            {
                code = "STITCHING", displayName = "Stitching", ordinal = 1,
                requiredRoleKeys = new[] { "tailor" }, requiresEvidence = false,
                expectedDuration = "1.00:00:00", sla = (string?)null,
                isOptional = false, isSkippable = false, isTerminal = false,
            },
            new
            {
                code = "QC", displayName = "Quality check", ordinal = 2,
                requiredRoleKeys = new[] { "qc_inspector" }, requiresEvidence = true,
                expectedDuration = "00:30:00", sla = "04:00:00",
                isOptional = false, isSkippable = false, isTerminal = true,
            },
        },
        transitions = new object[]
        {
            new { fromPhaseCode = "CUTTING", toPhaseCode = "STITCHING" },
            new { fromPhaseCode = "STITCHING", toPhaseCode = "QC" },
        },
        categoryKeys = new[] { categoryKey },
    };

    /// <summary>
    /// The six job phases <c>docs/prd/workflows/blouse.md</c> section 2.1 settles on — Cutting,
    /// Specialist work (Aari only, hence optional), Stitching, Finishing, QC and Ready, the last of
    /// which is the ready-for-delivery gate and this graph's only terminal phase.
    /// </summary>
    private static object BlouseWorkflowGraph() => new
    {
        phases = new object[]
        {
            new
            {
                code = "CUTTING", displayName = "Cutting", ordinal = 0,
                requiredRoleKeys = new[] { "tailor" }, requiresEvidence = false,
                expectedDuration = "02:00:00", sla = (string?)null,
                isOptional = false, isSkippable = false, isTerminal = false,
            },
            new
            {
                code = "SPECIALIST_WORK", displayName = "Specialist work (Aari)", ordinal = 1,
                requiredRoleKeys = new[] { "aari_specialist" }, requiresEvidence = true,
                expectedDuration = "10.00:00:00", sla = (string?)null,
                isOptional = true, isSkippable = false, isTerminal = false,
            },
            new
            {
                code = "STITCHING", displayName = "Stitching", ordinal = 2,
                requiredRoleKeys = new[] { "tailor" }, requiresEvidence = false,
                expectedDuration = "1.00:00:00", sla = (string?)null,
                isOptional = false, isSkippable = false, isTerminal = false,
            },
            new
            {
                code = "FINISHING", displayName = "Finishing", ordinal = 3,
                requiredRoleKeys = new[] { "tailor" }, requiresEvidence = false,
                expectedDuration = "01:00:00", sla = (string?)null,
                isOptional = false, isSkippable = false, isTerminal = false,
            },
            new
            {
                code = "QC", displayName = "Quality check", ordinal = 4,
                requiredRoleKeys = new[] { "tailor_master" }, requiresEvidence = true,
                expectedDuration = "00:30:00", sla = "04:00:00",
                isOptional = false, isSkippable = false, isTerminal = false,
            },
            new
            {
                code = "READY", displayName = "Ready", ordinal = 5,
                requiredRoleKeys = new[] { "tailor_master" }, requiresEvidence = false,
                expectedDuration = (string?)null, sla = (string?)null,
                isOptional = false, isSkippable = false, isTerminal = true,
            },
        },
        transitions = new object[]
        {
            // Pattern work skips specialist work directly to stitching; Aari work routes through it.
            new { fromPhaseCode = "CUTTING", toPhaseCode = "SPECIALIST_WORK" },
            new { fromPhaseCode = "CUTTING", toPhaseCode = "STITCHING" },
            new { fromPhaseCode = "SPECIALIST_WORK", toPhaseCode = "STITCHING" },
            new { fromPhaseCode = "STITCHING", toPhaseCode = "FINISHING" },
            new { fromPhaseCode = "FINISHING", toPhaseCode = "QC" },
            new { fromPhaseCode = "QC", toPhaseCode = "READY" },
        },
        categoryKeys = new[] { "blouse" },
    };

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>
    /// A synthetic client address distinct from every other login this file makes — every login is rate
    /// limited per client IP, and this file signs several accounts in within one run.
    /// </summary>
    private static string NextAddress()
    {
        var next = Interlocked.Increment(ref _addressCounter);

        return $"203.0.{118 + (next / 254)}.{1 + (next % 254)}";
    }
}
