using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Contracts.Measurements;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// Measurement-template administration over HTTP: drafting, reviewing, publishing and what is refused.
/// </summary>
/// <remarks>
/// These run against a real PostgreSQL because most of what is under test is a property of the database rather
/// than of the application: the immutability of a published version is a trigger, "at most one published version"
/// is a partial unique index, and the separation of duties is decided by a count of administrators the query has
/// to actually run.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class MeasurementTemplateEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    private static readonly string[] Sleeveless = ["SLEEVELESS"];

    [Fact]
    public async Task AnAdministratorDraftsReviewsAndPublishesATemplateWithoutADeployment()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync("mt-publish", "203.0.113.220");
        using var reviewer = await ReviewerAsync("mt-publish-rev", "203.0.113.230");

        var template = await CreateAsync(drafter, Code("MT_BLOUSE"));
        var version = await DraftAsync(drafter, template, "Version 1");

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/fields", Field("chest_bust"), await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var validation = await drafter.GetAsync($"{Root}/{template}/versions/{version}/validation");

        validation.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var report = JsonDocument.Parse(await validation.Content.ReadAsStringAsync(Token)))
        {
            report.RootElement.GetProperty("isReadyToPublish").GetBoolean().ShouldBeTrue();
        }

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/submit", new { }, await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync(
                $"{Root}/{template}/versions/{version}/approve", new { }, await TagAsync(reviewer, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var published = await reviewer.PostAsync(
            $"{Root}/{template}/versions/{version}/publish",
            new { reason = "Reviewed with the Tailor Master." },
            await TagAsync(reviewer, template));

        published.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await published.Content.ReadAsStringAsync(Token));

        body.RootElement.GetProperty("publishedVersionId").GetGuid().ShouldBe(version);
        body.RootElement.GetProperty("versions").EnumerateArray().Single()
            .GetProperty("status").GetString().ShouldBe("Published");
    }

    [Fact]
    public async Task RefusesEveryEditOnceAVersionLeavesDraft()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync("mt-immutable", "203.0.113.221");
        using var reviewer = await ReviewerAsync("mt-immutable-rev", "203.0.113.231");

        var template = await CreateAsync(drafter, Code("MT_SALWAR"));
        var version = await DraftAsync(drafter, template, "Version 1");
        var field = await AddFieldAsync(drafter, template, version, "waist");

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/submit", new { }, await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Submitted is already too late: a reviewer reads a version that cannot change under them.
        var edited = await drafter.PutAsync(
            $"{Root}/{template}/versions/{version}/fields/{field}", Field("waist"),
            await TagAsync(drafter, template));

        edited.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(edited)).ShouldBe("measurements.version-not-editable");

        (await reviewer.PostAsync(
                $"{Root}/{template}/versions/{version}/approve", new { }, await TagAsync(reviewer, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync(
                $"{Root}/{template}/versions/{version}/publish", new { reason = "Live." },
                await TagAsync(reviewer, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterPublication = await drafter.PostAsync(
            $"{Root}/{template}/versions/{version}/fields", Field("hip"),
            await TagAsync(drafter, template));

        afterPublication.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(afterPublication)).ShouldBe("measurements.version-not-editable");
    }

    [Fact]
    public async Task RefusesToPublishAVersionValidationRefuses()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync("mt-invalid", "203.0.113.222");
        using var reviewer = await ReviewerAsync("mt-invalid-rev", "203.0.113.232");

        var template = await CreateAsync(drafter, Code("MT_GOWN"));
        var version = await DraftAsync(drafter, template, "Version 1");

        // Required, and hidden by a rule that always matches: a capture could never be completed.
        var alwaysHidden = Field("sleeve_length") with
        {
            Rule = new { effect = "HiddenWhen", anyOf = Array.Empty<object>() },
        };

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/fields", alwaysHidden, await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var validation = await drafter.GetAsync($"{Root}/{template}/versions/{version}/validation");

        using (var report = JsonDocument.Parse(await validation.Content.ReadAsStringAsync(Token)))
        {
            report.RootElement.GetProperty("isReadyToPublish").GetBoolean().ShouldBeFalse();
            report.RootElement.GetProperty("findings").EnumerateArray()
                .ShouldContain(finding => finding.GetProperty("code").GetString()
                    == "required-field-never-shown");
        }

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/submit", new { }, await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync(
                $"{Root}/{template}/versions/{version}/approve", new { }, await TagAsync(reviewer, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await reviewer.PostAsync(
            $"{Root}/{template}/versions/{version}/publish", new { reason = "Trying it on." },
            await TagAsync(reviewer, template));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("measurements.publish-validation-failed");
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("retire")]
    public async Task RefusesAPublishLevelActWithoutAReason(string verb)
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync($"mt-reason-{verb}", "203.0.113.223");
        using var reviewer = await ReviewerAsync($"mt-reason-{verb}-rev", "203.0.113.233");

        var template = await CreateAsync(drafter, Code($"MT_REASON_{verb.ToUpperInvariant()}"));
        var version = await DraftAsync(drafter, template, "Version 1");

        await AddFieldAsync(drafter, template, version, "chest_bust");

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/submit", new { }, await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync(
                $"{Root}/{template}/versions/{version}/approve", new { }, await TagAsync(reviewer, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await reviewer.PostAsync(
            $"{Root}/{template}/versions/{version}/{verb}", new { reason = "   " },
            await TagAsync(reviewer, template));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("measurements.value-required");
    }

    [Fact]
    public async Task TheSubmitterCannotApproveTheirOwnWork()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The separation of duties, over the wire. The account holds both permissions, so this is not a
        // permission refusal: it is the rule that the person who wrote a template is not, by that alone, the
        // person who decides it is right.
        using var both = await AdministrationHarness.AdministratorAsync(
            fixture, "mt-selfapprove", "203.0.113.226", CustomersPermissions.EditTemplates,
            CustomersPermissions.PublishTemplates);
        using var other = await ReviewerAsync("mt-selfapprove-rev", "203.0.113.236");

        var template = await CreateAsync(both, Code("MT_SELF"));
        var version = await DraftAsync(both, template, "Version 1");

        await AddFieldAsync(both, template, version, "chest_bust");

        (await both.PostAsync(
                $"{Root}/{template}/versions/{version}/submit", new { }, await TagAsync(both, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await both.PostAsync(
            $"{Root}/{template}/versions/{version}/approve", new { }, await TagAsync(both, template));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(refused)).ShouldBe("measurements.submitter-cannot-publish");

        // Anybody else holding the permission may.
        (await other.PostAsync(
                $"{Root}/{template}/versions/{version}/approve", new { }, await TagAsync(other, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RefusesEveryTemplateRouteToACallerHoldingNothing()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var nobody = await AdministrationHarness.AdministratorAsync(
            fixture, "mt-nothing", "203.0.113.224", CustomersPermissions.Read);

        (await nobody.GetAsync(Root)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.PostAsync(Root, new { code = Code("MT_NO"), name = "No" }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RefusesToPublishWithOnlyTheDraftingPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await AdministrationHarness.AdministratorAsync(
            fixture, "mt-drafter", "203.0.113.225", CustomersPermissions.EditTemplates);

        var template = await CreateAsync(drafter, Code("MT_DRAFTER"));
        var version = await DraftAsync(drafter, template, "Version 1");

        await AddFieldAsync(drafter, template, version, "chest_bust");

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/submit", new { }, await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK, "drafting includes submitting for review");

        (await drafter.PostAsync($"{Root}/{template}/versions/{version}/approve", new { }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden, "approving is a reviewer's act");
        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/publish", new { reason = "Mine." }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SeedsTheSixTemplatesOnceAndLeavesThemUnpublished()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var scope = fixture.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IMeasurementTemplateReferenceDataSeeder>();
        var organisationId = Guid.CreateVersion7();

        var first = await seeder.SeedTemplatesAsync(organisationId, Token);

        first.Created.ShouldBeTrue();
        first.TemplateCount.ShouldBe(6);
        first.FieldCount.ShouldBeGreaterThan(80, "the six seeded field sets together");

        // Nothing is published, and that is the point: every field set in the specification is marked "proposed
        // and to be confirmed", and business review of each is an acceptance criterion of #27.
        first.AwaitingPublication.ShouldBe(6);

        var second = await seeder.SeedTemplatesAsync(organisationId, Token);

        second.Created.ShouldBeFalse("a second run changes nothing");
        second.TemplateCount.ShouldBe(first.TemplateCount);
        second.FieldCount.ShouldBe(first.FieldCount);
    }

    [Fact]
    public async Task EverySeededTemplateWouldPassItsOwnPublishValidation()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The seed is transcribed from a document by hand, so the risk is a number or a key that the domain
        // refuses. Seeding proves the fields were accepted; this proves the version would actually publish, which
        // is what an Owner will try to do after reviewing it.
        using var scope = fixture.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IMeasurementTemplateReferenceDataSeeder>();
        var handler = scope.ServiceProvider.GetRequiredService<MeasurementTemplateHandler>();
        var organisationId = Guid.CreateVersion7();

        await seeder.SeedTemplatesAsync(organisationId, Token);

        var templates = await handler.ListAsync(organisationId, Token);

        templates.Count.ShouldBe(6);

        foreach (var administered in templates)
        {
            var draft = administered.Template.Versions.Single();
            var report = await handler.ValidateAsync(
                administered.Template.Id, draft.Id, organisationId, Token);

            report.IsSuccess.ShouldBeTrue();
            report.Value.HasErrors.ShouldBeFalse(
                $"'{administered.Template.Code}' reports: "
                + string.Join(
                    "; ",
                    report.Value.Findings
                        .Where(finding => finding.Severity.ToString() == "Error")
                        .Select(finding => $"{finding.Code} at {finding.Target}")));
        }
    }

    [Fact]
    public async Task ThePublishedReadAnswersWithEverythingACaptureNeeds()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // IMeasurementTemplateQuery is what #28's capture wizard and every job card will read, so what it carries
        // is a contract rather than an implementation detail. The bands come back in millimetres on purpose: the
        // caller renders them in whatever unit the reader chose, and a pre-rounded bound would refuse a value that
        // is actually inside it.
        using var drafter = await DrafterAsync("mt-query", "203.0.113.227");
        using var reviewer = await ReviewerAsync("mt-query-rev", "203.0.113.237");

        var template = await CreateAsync(drafter, Code("MT_QUERY"));
        var version = await DraftAsync(drafter, template, "Version 1");

        await AddFieldAsync(drafter, template, version, "chest_bust");

        // A conditional choice field, because those are the two things a caller cannot recover from anywhere
        // else: which fields to show, and what to call the choices.
        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/fields",
                Field("closure_style") with
                {
                    CanonicalUnit = "None",
                    InchFraction = 0,
                    CentimetreDecimals = 0,
                    MinimumMillimetres = 0,
                    MaximumMillimetres = 0,
                    WarnBelowMillimetres = null,
                    WarnAboveMillimetres = null,
                    DisplayOrder = 1,
                    Rule = new
                    {
                        effect = "HiddenWhen",
                        anyOf = new[]
                        {
                            new
                            {
                                scope = "DesignSelection",
                                name = "sleeve_style",
                                @operator = "IsAnyOf",
                                values = Sleeveless,
                            },
                        },
                    },
                    Options =
                    [
                        new { code = "HOOK", label = "Hook and eye", labelTamil = "கொக்கி", displayOrder = 1 },
                        new { code = "ZIP", label = "Zip", labelTamil = (string?)null, displayOrder = 0 },
                    ],
                },
                await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = fixture.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IMeasurementTemplateQuery>();
        var organisationId = SessionTestData.OrganisationId;

        (await query.GetPublishedAsync(template, Token))
            .ShouldBeNull("a draft is not something measurements are captured against");
        (await query.WithPublishedVersionAsync([template], organisationId, Token))
            .ShouldNotContain(template);

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/submit", new { }, await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync(
                $"{Root}/{template}/versions/{version}/approve", new { }, await TagAsync(reviewer, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsync(
                $"{Root}/{template}/versions/{version}/publish", new { reason = "Live." },
                await TagAsync(reviewer, template)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var published = await query.GetPublishedAsync(template, Token);

        published.ShouldNotBeNull();
        published.TemplateId.ShouldBe(template);
        published.VersionId.ShouldBe(version);
        published.VersionNumber.ShouldBe(1);
        published.IsPublished.ShouldBeTrue();
        published.DefaultDisplayUnit.ShouldBe("Inch");

        published.Fields.Count.ShouldBe(2);

        var field = published.Fields.Single(candidate => candidate.Key == "chest_bust");

        field.Key.ShouldBe("chest_bust");
        field.CanonicalUnit.ShouldBe("Millimetre");
        field.DisplayUnits.ShouldBe(["Inch", "Centimetre"]);
        field.InchFraction.ShouldBe(8);
        field.CentimetreDecimals.ShouldBe(1);
        field.MinimumMillimetres.ShouldBe(100m);
        field.MaximumMillimetres.ShouldBe(2000m);
        field.WarnBelowMillimetres.ShouldBe(200m);
        field.DiagramReference.ShouldBe("blouse_front_v1#chest_bust", "the sheet key anchored by the field key");
        field.DiagramAlt.ShouldNotBeNullOrWhiteSpace();

        field.Rule.ShouldBeNull("a field with no rule is always shown");
        field.Options.ShouldBeEmpty();

        // The rule travels as clauses, because the module that has to obey it is not the one that stores it: a
        // design-selection operand can only be read inside an order, so Orders evaluates it, and prose cannot be
        // evaluated. Without this, #28 could not decide which fields to show at all.
        var conditional = published.Fields.Single(candidate => candidate.Key == "closure_style");

        conditional.Rule.ShouldNotBeNull();
        conditional.Rule.Effect.ShouldBe("HiddenWhen");

        var clause = conditional.Rule.AnyOf.ShouldHaveSingleItem();

        clause.Scope.ShouldBe("DesignSelection");
        clause.Name.ShouldBe("sleeve_style");
        clause.Operator.ShouldBe("IsAnyOf");
        clause.Values.ShouldBe(["SLEEVELESS"]);

        // And the choices keep their labels, which nothing else publishes.
        conditional.Options.Select(option => option.Code).ShouldBe(["ZIP", "HOOK"]);
        conditional.Options.Single(option => option.Code == "HOOK").Label.ShouldBe("Hook and eye");
        conditional.Options.Single(option => option.Code == "ZIP").LabelTamil.ShouldBeNull();

        (await query.WithPublishedVersionAsync([template], organisationId, Token)).ShouldContain(template);

        // Asked by its own identity, a version answers whatever state it is in — which is how a measurement taken
        // under a since-retired version still renders.
        var byVersion = await query.GetVersionAsync(version, Token);

        byVersion.ShouldNotBeNull();
        byVersion.VersionId.ShouldBe(version);

        (await query.GetVersionAsync(Guid.CreateVersion7(), Token)).ShouldBeNull();
        (await query.WithPublishedVersionAsync([], organisationId, Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task RefusesAMutationThatNamesNoVersionItWasMadeAgainst()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // COD-05 in docs/architecture/conventions.md: a command on an editable aggregate must say which version
        // it was made against, and 428 rather than 400 tells a client that re-reading and resending will work.
        // Treating a missing header as "no precondition to check" is the whole hole this closes: an administrator
        // working from a screen somebody else has already changed would overwrite them and never be told.
        using var drafter = await DrafterAsync("mt-precondition", "203.0.113.228");
        using var reviewer = await ReviewerAsync("mt-precondition-rev", "203.0.113.241");

        var template = await CreateAsync(drafter, Code("MT_PRECONDITION"));
        var version = await DraftAsync(drafter, template, "Version 1");
        var field = await AddFieldAsync(drafter, template, version, "chest_bust");

        // Each route is asked by a caller who holds its permission: authorisation is middleware and answers
        // before any endpoint filter, so a 403 here would hide the 428 this test is about.
        var withoutPrecondition = new (string Verb, string Path, object Body, bool Reviewer)[]
        {
            ("POST", $"{Root}/{template}/versions/{version}/fields", Field("waist"), false),
            ("PUT", $"{Root}/{template}/versions/{version}/fields/{field}", Field("chest_bust"), false),
            ("POST", $"{Root}/{template}/versions/{version}/fields/{field}/delete", new { reason = "No." }, false),
            ("POST", $"{Root}/{template}/versions/{version}/submit", new { }, false),
            ("POST", $"{Root}/{template}/versions/{version}/return", new { reason = "Back." }, true),
            ("POST", $"{Root}/{template}/versions/{version}/approve", new { }, true),
            ("POST", $"{Root}/{template}/versions/{version}/publish", new { reason = "Live." }, true),
            ("POST", $"{Root}/{template}/versions/{version}/retire", new { reason = "Done." }, true),
        };

        foreach (var (verb, path, body, asReviewer) in withoutPrecondition)
        {
            var caller = asReviewer ? reviewer : drafter;
            var response = verb == "PUT"
                ? await caller.PutAsync(path, body, Key())
                : await caller.PostAsync(path, body, Key());

            response.StatusCode.ShouldBe(
                HttpStatusCode.PreconditionRequired, $"{verb} {path} changes a version somebody else may hold");
            (await CodeOfAsync(response)).ShouldBe("concurrency.if-match-required");
        }

        // The two routes that create rather than change carry no precondition, because there is nothing yet to
        // be stale about — a retry is settled by the idempotency key instead (conventions.md section 4.2).
        (await drafter.PostAsync(Root, new { code = Code("MT_PRECONDITION_2"), name = "Second" }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await drafter.PostAsync(
                $"{Root}/{template}/versions",
                new { name = "Version 2", notes = (string?)null, defaultDisplayUnit = "Inch" },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task RefusesAMutationMadeAgainstAVersionSomebodyElseHasAlreadyChanged()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync("mt-stale", "203.0.113.229");

        var template = await CreateAsync(drafter, Code("MT_STALE"));
        var version = await DraftAsync(drafter, template, "Version 1");

        // What one administrator read before a second one saved.
        var stale = await TagAsync(drafter, template);

        await AddFieldAsync(drafter, template, version, "chest_bust");

        var refused = await drafter.PostAsync(
            $"{Root}/{template}/versions/{version}/fields", Field("waist"), stale);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(refused)).ShouldBe("measurements.version-changed");

        // Re-read and it goes through, which is the whole point of answering with a version rather than a no.
        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/fields", Field("waist"),
                await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AFieldReadsBackInTheShapeTheUpdateAccepts()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // An administration screen reads a field, changes one thing and sends it back. If the read answered with
        // a rendered sentence and a list of bare codes, that screen would have to invent the clauses and the
        // labels it never received, and would drop them on every save. So the read is asserted to be replayable:
        // what comes out goes straight back in, and the second read equals the first.
        using var drafter = await DrafterAsync("mt-roundtrip", "203.0.113.238");

        var template = await CreateAsync(drafter, Code("MT_ROUNDTRIP"));
        var version = await DraftAsync(drafter, template, "Version 1");

        await AddFieldAsync(drafter, template, version, "waist_finish");

        var choice = Field("closure_style") with
        {
            CanonicalUnit = "None",
            InchFraction = 0,
            CentimetreDecimals = 0,
            MinimumMillimetres = 0,
            MaximumMillimetres = 0,
            WarnBelowMillimetres = null,
            WarnAboveMillimetres = null,
            Rule = new
            {
                effect = "ShownWhen",
                anyOf = new[]
                {
                    new { scope = "Field", name = "waist_finish", @operator = "IsAnyOf", values = new[] { "ELASTIC" } },
                    new { scope = "DesignSelection", name = "leg_style", @operator = "Excludes", values = new[] { "CHURIDAR" } },
                },
            },
            Options =
            [
                new { code = "HOOK", label = "Hook and eye", labelTamil = "கொக்கி", displayOrder = 1 },
                new { code = "ZIP", label = "Zip", labelTamil = (string?)null, displayOrder = 0 },
            ],
        };

        (await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/fields", choice, await TagAsync(drafter, template)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var read = await drafter.GetAsync($"{Root}/{template}");

        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));

        var stored = body.RootElement.GetProperty("versions").EnumerateArray().First()
            .GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("key").GetString() == "closure_style");

        // The rule comes back as clauses, not only as prose.
        var rule = stored.GetProperty("ruleDefinition");

        rule.GetProperty("effect").GetString().ShouldBe("ShownWhen");

        var clauses = rule.GetProperty("anyOf").EnumerateArray().ToArray();

        clauses.Length.ShouldBe(2);
        clauses[0].GetProperty("scope").GetString().ShouldBe("Field");
        clauses[0].GetProperty("name").GetString().ShouldBe("waist_finish");
        clauses[0].GetProperty("operator").GetString().ShouldBe("IsAnyOf");
        clauses[0].GetProperty("values").EnumerateArray().Single().GetString().ShouldBe("ELASTIC");
        clauses[1].GetProperty("scope").GetString().ShouldBe("DesignSelection");
        clauses[1].GetProperty("operator").GetString().ShouldBe("Excludes");

        // The sentence is still there for a screen to show, beside the clauses rather than instead of them.
        // It keeps its old name because renaming a field breaks every client reading it inside a major version.
        stored.GetProperty("rule").GetString().ShouldNotBeNullOrWhiteSpace();

        // Likewise the bare codes: superseded by `options`, still served, still correct.
        stored.GetProperty("optionCodes").EnumerateArray()
            .Select(code => code.GetString()).ShouldBe(["ZIP", "HOOK"]);

        // Every option keeps its label, its Tamil label and its order.
        var options = stored.GetProperty("options").EnumerateArray().ToArray();

        options.Length.ShouldBe(2);
        options[0].GetProperty("code").GetString().ShouldBe("ZIP", "options come back in display order");
        options[0].GetProperty("label").GetString().ShouldBe("Zip");
        options[0].GetProperty("labelTamil").ValueKind.ShouldBe(JsonValueKind.Null);
        options[1].GetProperty("code").GetString().ShouldBe("HOOK");
        options[1].GetProperty("labelTamil").GetString().ShouldBe("கொக்கி");

        // The diagram comes back in the halves the update accepts, not only as the joined reference.
        stored.GetProperty("diagramKey").GetString().ShouldBe("blouse_front_v1");
        stored.GetProperty("diagramReference").GetString().ShouldBe("blouse_front_v1#closure_style");

        // And now the proof: rebuild the update from what was read — which is the only mapping a client has to
        // make, `ruleDefinition` into `rule` and `options` into `options` — send it back, and lose nothing.
        // A byte-identical replay is not possible while the deprecated `rule` string still occupies that name,
        // and that is the cost of keeping the old shape served rather than breaking every reader of it.
        var fieldId = stored.GetProperty("templateFieldId").GetGuid();
        var replay = JsonSerializer.Deserialize<JsonElement>(stored.GetRawText());
        var replayBody = replay.EnumerateObject()
            .Where(property => property.Name is not ("rule" or "ruleDefinition" or "optionCodes"))
            .ToDictionary(property => property.Name, property => (object?)property.Value);

        replayBody["rule"] = rule;

        var replayed = await drafter.PutAsync(
            $"{Root}/{template}/versions/{version}/fields/{fieldId}", replayBody,
            await TagAsync(drafter, template));

        replayed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var after = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync(Token));

        var again = after.RootElement.GetProperty("versions").EnumerateArray().First()
            .GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("key").GetString() == "closure_style");

        again.GetProperty("ruleDefinition").GetRawText().ShouldBe(rule.GetRawText());
        again.GetProperty("options").GetRawText().ShouldBe(stored.GetProperty("options").GetRawText());
        again.GetProperty("optionCodes").GetRawText().ShouldBe(stored.GetProperty("optionCodes").GetRawText());
    }

    [Theory]
    [InlineData(1, "99")]
    [InlineData(2, "-1")]
    [InlineData(3, "Millimetre, Count")]
    [InlineData(4, "")]
    public async Task RefusesAnEnumerationThatIsNotOneOfItsNames(int number, string value)
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // A number is refused rather than resolved. "99" is not a member at all, and even "0" would mean
        // something different the day a member is inserted above it — a silent reinterpretation of every field
        // already stored. The names are the contract.
        using var drafter = await DrafterAsync($"mt-enum-{number}", "203.0.113.239");

        var template = await CreateAsync(drafter, Code($"MT_ENUM_{number}"));
        var version = await DraftAsync(drafter, template, "Version 1");

        var refused = await drafter.PostAsync(
            $"{Root}/{template}/versions/{version}/fields",
            Field("chest_bust") with { CanonicalUnit = value },
            await TagAsync(drafter, template));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("measurements.not-a-valid-value");
    }

    [Fact]
    public async Task RefusesARuleWhoseEffectScopeOrOperatorIsANumber()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await DrafterAsync("mt-enum-rule", "203.0.113.240");

        var template = await CreateAsync(drafter, Code("MT_ENUM_RULE"));
        var version = await DraftAsync(drafter, template, "Version 1");

        object Rule(string effect, string scope, string comparison) => new
        {
            effect,
            anyOf = new[] { new { scope, name = "waist_finish", @operator = comparison, values = new[] { "ELASTIC" } } },
        };

        var refusals = new[]
        {
            Rule("1", "Field", "IsAnyOf"),
            Rule("ShownWhen", "0", "IsAnyOf"),
            Rule("ShownWhen", "Field", "2"),
        };

        foreach (var rule in refusals)
        {
            var refused = await drafter.PostAsync(
                $"{Root}/{template}/versions/{version}/fields",
                Field("sleeve_length") with { Rule = rule },
                await TagAsync(drafter, template));

            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeOfAsync(refused)).ShouldBe("measurements.not-a-valid-value");
        }
    }

    private const string Root = "/api/v1/customers/measurement-templates";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static string Code(string stem) => $"{stem}_{RunToken}";

    /// <summary>An administrator who drafts and submits, and cannot approve their own work.</summary>
    private Task<AdministrationHarness.AdministratorClient> DrafterAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CustomersPermissions.EditTemplates);

    /// <summary>A second administrator, who reviews, approves, publishes and retires.</summary>
    /// <remarks>
    /// <para>
    /// Separate from the drafter on purpose. "The submitter does not also approve" is the rule under test in
    /// <see cref="TheSubmitterCannotApproveTheirOwnWork"/> and a precondition of every other lifecycle test here;
    /// using one client for both would make them pass or fail depending on how many administrators the shared test
    /// database happened to hold.
    /// </para>
    /// <para>
    /// Separate by <em>identity</em>, though, not by permission: they hold both template permissions, because the
    /// matrix grants <c>catalog.templates.edit</c> and <c>catalog.templates.publish</c> to <c>owner</c> and
    /// <c>admin</c> and to nobody else, so a reviewer who cannot also draft is a role this product does not have.
    /// A reviewer holding only the publish half could not even read the template they were approving, and the
    /// separation this file exists to prove is enforced on the submitting user's identity, not on what they hold.
    /// </para>
    /// </remarks>
    private Task<AdministrationHarness.AdministratorClient> ReviewerAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CustomersPermissions.EditTemplates,
            CustomersPermissions.PublishTemplates);

    private static async Task<Guid> CreateAsync(
        AdministrationHarness.AdministratorClient client, string code)
    {
        var response = await client.PostAsync(
            Root, new { code, name = code, description = "Written by a test." }, Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.GetProperty("measurementTemplateId").GetGuid();
    }

    private static async Task<Guid> DraftAsync(
        AdministrationHarness.AdministratorClient client, Guid template, string name)
    {
        var response = await client.PostAsync(
            $"{Root}/{template}/versions",
            new { name, notes = (string?)null, defaultDisplayUnit = "Inch", cloneFromVersionId = (Guid?)null },
            Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.GetProperty("versions").EnumerateArray().First()
            .GetProperty("templateVersionId").GetGuid();
    }

    private static async Task<Guid> AddFieldAsync(
        AdministrationHarness.AdministratorClient client, Guid template, Guid version, string key)
    {
        var response = await client.PostAsync(
            $"{Root}/{template}/versions/{version}/fields", Field(key), await TagAsync(client, template));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.GetProperty("versions").EnumerateArray().First()
            .GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("key").GetString() == key)
            .GetProperty("templateFieldId").GetGuid();
    }

    private static FieldBody Field(string key) => new(
        key,
        key.Replace('_', ' '),
        null,
        "Bodice",
        0,
        "Millimetre",
        8,
        1,
        true,
        100,
        2000,
        200,
        1800,
        "Body measurement. Round the fullest part, tape level.",
        "blouse_front_v1",
        null,
        "From one point to the other, tape level.",
        null,
        []);

    /// <summary>An idempotency key and the precondition every template mutation now demands.</summary>
    /// <remarks>
    /// The tag is the template's, read back the way a real client reads it — from the <c>ETag</c> of a GET.
    /// Threading it through every mutation is the point: a test that could still write without one would not
    /// notice the day the route stopped asking.
    /// </remarks>
    private static async Task<(string Name, string Value)[]> TagAsync(
        AdministrationHarness.AdministratorClient client, Guid template)
    {
        var read = await client.GetAsync($"{Root}/{template}");

        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull("a read of an editable aggregate publishes the tag its writes demand");

        return
        [
            ("Idempotency-Key", Guid.CreateVersion7().ToString()),
            ("If-Match", read.Headers.ETag.ToString()),
        ];
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The field request as the wire carries it.</summary>
    internal sealed record FieldBody(
        string Key,
        string Label,
        string? LabelTamil,
        string GroupName,
        int DisplayOrder,
        string CanonicalUnit,
        int InchFraction,
        int CentimetreDecimals,
        bool IsRequired,
        decimal MinimumMillimetres,
        decimal MaximumMillimetres,
        decimal? WarnBelowMillimetres,
        decimal? WarnAboveMillimetres,
        string HelpText,
        string? DiagramKey,
        Guid? DiagramMediaId,
        string? DiagramAlt,
        object? Rule,
        object[] Options);
}
