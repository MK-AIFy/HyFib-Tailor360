using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.UnitTests.Identity;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// The reconciliation of INV-MTV-06 and every other cross-module reference (issue #91).
/// </summary>
/// <remarks>
/// What these are about is not "does it notice a broken reference" — the validators do that, and they have their
/// own tests. It is the four things only the reconciliation decides: what it opens, what it leaves alone on a
/// redelivery, what it closes, and what it refuses to conclude when it could not check.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class CatalogReconcilerTests
{
    private const string Retired = "customers.measurement-template-version-retired.v1";
    private const string Published = "customers.measurement-template-version-published.v1";

    private static readonly DateTimeOffset Yesterday = CatalogTestData.Now.AddDays(-1);

    [Fact]
    public async Task Opens_a_breach_for_a_reference_that_has_stopped_being_valid()
    {
        var breaches = new RecordingBreachStore();
        var reconciler = Reconciler(breaches, Broken());

        var result = await reconciler.ReconcileAsync(
            CatalogTestData.Organisation, Retired, TestContext.Current.CancellationToken);

        result.Opened.ShouldBe(1);
        result.Resolved.ShouldBe(0);

        var opened = breaches.Added.ShouldHaveSingleItem();
        opened.Code.ShouldBe("catalog.measurement-template-not-published");
        opened.Target.ShouldBe("serviceTypes[BLOUSE.STITCHING].measurementTemplateId");
        opened.Validator.ShouldBe("measurement-templates");
        opened.IsOpen.ShouldBeTrue();

        // Why it was noticed now is the second question anybody asks about a breach that opened overnight.
        opened.DetectedBecauseOf.ShouldBe(Retired);
    }

    [Fact]
    public async Task Does_not_open_a_second_breach_when_the_same_event_is_delivered_twice()
    {
        // Delivery is at least once, so this is the ordinary case rather than the unlucky one. A second row
        // would also move the detection time, which is what an administrator reads as how long the shop was
        // exposed.
        var standing = OpenBreach("serviceTypes[BLOUSE.STITCHING].measurementTemplateId");
        var breaches = new RecordingBreachStore(standing);
        var reconciler = Reconciler(breaches, Broken());

        var result = await reconciler.ReconcileAsync(
            CatalogTestData.Organisation, Retired, TestContext.Current.CancellationToken);

        result.Opened.ShouldBe(0);
        result.Resolved.ShouldBe(0);
        breaches.Added.ShouldBeEmpty();
        standing.DetectedAt.ShouldBe(Yesterday);
        standing.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Closes_a_breach_the_check_no_longer_finds()
    {
        // The shop fixed it by publishing a template version. Nothing else would tell the catalogue so, which
        // is why the publication event is consumed at all.
        var standing = OpenBreach("serviceTypes[BLOUSE.STITCHING].measurementTemplateId");
        var breaches = new RecordingBreachStore(standing);
        var reconciler = Reconciler(breaches, Healthy());

        var result = await reconciler.ReconcileAsync(
            CatalogTestData.Organisation, Published, TestContext.Current.CancellationToken);

        result.Opened.ShouldBe(0);
        result.Resolved.ShouldBe(1);
        standing.IsOpen.ShouldBeFalse();
        standing.ResolvedAt.ShouldBe(CatalogTestData.Now);
        standing.ResolvedBecauseOf.ShouldBe(Published);
    }

    [Fact]
    public async Task Closes_one_breach_and_opens_another_in_the_same_pass()
    {
        // A reconciliation is a whole re-check rather than a diff of the event, so a fix and a new break that
        // land together are both accounted for — the case a handler written per event would get half right.
        var fixedOne = OpenBreach("serviceTypes[BLOUSE.STITCHING].measurementTemplateId");
        var breaches = new RecordingBreachStore(fixedOne);
        var reconciler = Reconciler(
            breaches, Broken("serviceTypes[SALWAR.STITCHING].measurementTemplateId"));

        var result = await reconciler.ReconcileAsync(
            CatalogTestData.Organisation, Retired, TestContext.Current.CancellationToken);

        result.Opened.ShouldBe(1);
        result.Resolved.ShouldBe(1);
        fixedOne.IsOpen.ShouldBeFalse();
        breaches.Added.ShouldHaveSingleItem().Target
            .ShouldBe("serviceTypes[SALWAR.STITCHING].measurementTemplateId");
    }

    [Fact]
    public async Task Opens_nothing_for_a_warning()
    {
        // A warning is something worth saying that publication proceeds through. Recording one as a breach
        // would fill the record with things nobody is expected to act on, and the ones that matter would be
        // read past.
        var breaches = new RecordingBreachStore();
        var reconciler = Reconciler(breaches, new StubValidator([
            CatalogFinding.Warning("catalog.category-empty", "'AARI' offers no service type.", "categories[AARI]"),
        ]));

        var result = await reconciler.ReconcileAsync(
            CatalogTestData.Organisation, Retired, TestContext.Current.CancellationToken);

        result.Opened.ShouldBe(0);
        breaches.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concludes_nothing_when_nothing_is_published()
    {
        // A shop between catalogues is a real state, and nothing is orderable in it, so nothing can be
        // stranded. Reporting a breach here would be reporting one against a version no counter can reach.
        var breaches = new RecordingBreachStore();
        var reconciler = new CatalogReconciler(
            new StubCatalogStore(null),
            breaches,
            Check(Broken()),
            new MovableClock(CatalogTestData.Now),
            new CountingCatalogIds(),
            NullLogger<CatalogReconciler>.Instance);

        var result = await reconciler.ReconcileAsync(
            CatalogTestData.Organisation, Retired, TestContext.Current.CancellationToken);

        result.ShouldBe(CatalogReconciliation.Nothing);
        breaches.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concludes_nothing_and_closes_nothing_when_a_validator_cannot_answer()
    {
        // "We could not check" is not "we checked and it is fine". Closing a standing breach on a failed check
        // would tell an administrator the shop was fixed by an outage. It throws so the delivery retries with
        // backoff and eventually dead-letters, rather than leaving a silent gap.
        var standing = OpenBreach("serviceTypes[BLOUSE.STITCHING].measurementTemplateId");
        var breaches = new RecordingBreachStore(standing);
        var reconciler = Reconciler(breaches, new ThrowingValidator());

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await reconciler.ReconcileAsync(
                CatalogTestData.Organisation, Retired, TestContext.Current.CancellationToken));

        standing.IsOpen.ShouldBeTrue();
        breaches.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Keeps_a_breach_whose_message_changed_rather_than_reopening_it()
    {
        // Matched on the code and the target, never the sentence. A validator that reworded its message would
        // otherwise close every breach it had open and reopen all of them with a new detection time, wiping
        // the one fact the record exists to carry.
        var standing = OpenBreach("serviceTypes[BLOUSE.STITCHING].measurementTemplateId");
        var breaches = new RecordingBreachStore(standing);
        var reconciler = Reconciler(breaches, new StubValidator([
            CatalogFinding.Error(
                "catalog.measurement-template-not-published",
                "Reworded entirely, for a shop that reads more carefully.",
                "serviceTypes[BLOUSE.STITCHING].measurementTemplateId"),
        ]));

        var result = await reconciler.ReconcileAsync(
            CatalogTestData.Organisation, Retired, TestContext.Current.CancellationToken);

        result.Opened.ShouldBe(0);
        result.Resolved.ShouldBe(0);
        standing.DetectedAt.ShouldBe(Yesterday);
    }

    private static CatalogReconciler Reconciler(
        ICatalogReferenceBreachStore breaches,
        ICatalogDependencyValidator validator)
        => new(
            new StubCatalogStore(PublishedVersion()),
            breaches,
            Check(validator),
            new MovableClock(CatalogTestData.Now),
            new CountingCatalogIds(),
            NullLogger<CatalogReconciler>.Instance);

    private static CatalogPublicationCheck Check(ICatalogDependencyValidator validator)
        => new(
            new StubCatalogStore(PublishedVersion()),
            [validator],
            NullLogger<CatalogPublicationCheck>.Instance);

    private static StubValidator Healthy() => new([]);

    private static StubValidator Broken(
        string target = "serviceTypes[BLOUSE.STITCHING].measurementTemplateId")
        => new([
            CatalogFinding.Error(
                "catalog.measurement-template-not-published",
                "The measurement template linked to this service has no published version.",
                target),
        ]);

    private static CatalogReferenceBreach OpenBreach(string target)
        => new(
            CatalogTestData.Id($"breach-{target}"),
            CatalogTestData.Organisation,
            PublishedVersion().Id,
            "catalog.measurement-template-not-published",
            target,
            "The measurement template linked to this service has no published version.",
            "measurement-templates",
            Retired,
            Yesterday);

    private static CatalogVersion PublishedVersion()
    {
        var version = CatalogTestData.Draft();
        var category = version.AddCategory(
            CatalogTestData.Id("category-blouse"),
            CatalogTestData.Id("category-blouse-key"),
            null,
            CatalogTestData.CategoryOf("BLOUSE"),
            CatalogTestData.Now,
            null).Value;

        version.AddServiceType(
            CatalogTestData.Id("service-stitching"),
            CatalogTestData.Id("service-stitching-key"),
            category.Id,
            CatalogTestData.ServiceOf("STITCHING"),
            CatalogTestData.Now,
            null);

        version.Publish(CatalogTestData.Now, null, "Reconciliation fixture.");

        return version;
    }
}

/// <summary>A store that answers with one published version, or with none.</summary>
internal sealed class StubCatalogStore(CatalogVersion? published) : ICatalogStore
{
    public Task<CatalogVersion?> FindAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(published);

    public Task<CatalogVersion?> FindPublishedAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(published);

    public Task<IReadOnlyList<CatalogVersion>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CatalogVersion>>(published is null ? [] : [published]);

    public Task<int> NextVersionNumberAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(1);

    public Task<CatalogCodeLedger> ReadCodeHistoryAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CatalogCodeLedger.Empty);

    public void Add(CatalogVersion version)
    {
        // The reconciliation reads and never adds a version, so a store that pretended to would hide a
        // reconciliation that had started writing catalogue rows.
        throw new NotSupportedException("The reconciliation never adds a catalogue version.");
    }

    public EntityTag EntityTagOf(CatalogVersion version) => EntityTag.From(1);

    public Task<int> SaveAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<Tailor360.Platform.Abstractions.Results.Result> SaveDraftAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(Tailor360.Platform.Abstractions.Results.Result.Success());

    public Task<Tailor360.Platform.Abstractions.Results.Result> SavePublicationAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(Tailor360.Platform.Abstractions.Results.Result.Success());
}

/// <summary>Collects what the reconciliation opened, and hands back what was already standing.</summary>
internal sealed class RecordingBreachStore(params CatalogReferenceBreach[] standing)
    : ICatalogReferenceBreachStore
{
    private readonly List<CatalogReferenceBreach> _added = [];

    /// <summary>Everything staged as newly opened.</summary>
    public IReadOnlyList<CatalogReferenceBreach> Added => _added;

    public Task<IReadOnlyList<CatalogReferenceBreach>> ListOpenAsync(
        Guid catalogVersionId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CatalogReferenceBreach>>(
            [.. standing.Where(breach => breach.IsOpen)]);

    public Task<IReadOnlyList<CatalogReferenceBreach>> ListOpenForOrganisationAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CatalogReferenceBreach>>(
            [.. standing.Where(breach => breach.IsOpen)]);

    public void Add(CatalogReferenceBreach breach) => _added.Add(breach);
}

/// <summary>A validator that always says the same thing.</summary>
internal sealed class StubValidator(IReadOnlyList<CatalogFinding> findings) : ICatalogDependencyValidator
{
    public string Name => "measurement-templates";

    public ValueTask<IReadOnlyList<CatalogFinding>> ValidatePublicationAsync(
        CatalogPublicationCandidate candidate,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(findings);
}

/// <summary>A validator that is down.</summary>
internal sealed class ThrowingValidator : ICatalogDependencyValidator
{
    public string Name => "measurement-templates";

    public ValueTask<IReadOnlyList<CatalogFinding>> ValidatePublicationAsync(
        CatalogPublicationCandidate candidate,
        CancellationToken cancellationToken = default)
        => throw new TimeoutException("The templates module did not answer.");
}
