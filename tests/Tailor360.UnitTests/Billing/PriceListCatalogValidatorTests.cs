using Shouldly;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Pricing;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// Link 4 (#146): a service type's or a design option's price-list item code resolves to an active item
/// of the published version pricing every branch the catalogue offers it at.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PriceListCatalogValidatorTests
{
    private static readonly Guid Blouse = BillingTestData.Id("blouse");

    [Fact]
    public async Task FindsNothingWhenEveryCodeResolvesAtEveryBranch()
    {
        var validator = new PriceListCatalogValidator(new StubStore([PublishedVersion([BillingTestData.MainBranch, BillingTestData.SecondBranch])]));

        var findings = await validator.ValidatePublicationAsync(
            Candidate(
            Service("STITCHING", "BLOUSE_PATTERN_STITCHING", [BillingTestData.MainBranch, BillingTestData.SecondBranch]),
            [Group("lining", [Option("FULL", "LINING_FULL")])]),
            TestContext.Current.CancellationToken);

        findings.ShouldBeEmpty(string.Join("; ", findings.Select(finding => finding.Message)));
    }

    [Fact]
    public async Task NamesTheServiceOrOptionWhoseCodeIsUnknownRetiredOrUnpriced()
    {
        var validator = new PriceListCatalogValidator(new StubStore([PublishedVersion([BillingTestData.MainBranch])]));

        var findings = await validator.ValidatePublicationAsync(
            Candidate(
            Service("STITCHING", "NOT_AN_ITEM", [BillingTestData.MainBranch, BillingTestData.SecondBranch]),
            [Group("lining", [Option("FULL", "LINING_RETIRED"), Option("NONE", null)])]),
            TestContext.Current.CancellationToken);

        findings.Select(finding => (finding.Severity, finding.Code, finding.Target)).ShouldBe(
        [
            // The second branch has no published version yet: a warning, so the catalogue can go first.
            (CatalogFindingSeverity.Warning, "catalog.price-list-not-published", "serviceTypes[BLOUSE_PATTERN.STITCHING].priceListItemCode"),
            (CatalogFindingSeverity.Error, "catalog.price-list-item-unknown", "serviceTypes[BLOUSE_PATTERN.STITCHING].priceListItemCode"),
            (CatalogFindingSeverity.Error, "catalog.price-list-item-retired", "designOptions[BLOUSE_PATTERN.lining.FULL].priceListItemCode"),
        ]);
    }

    [Fact]
    public async Task AsksNothingWhenNoCodeIsLinked()
    {
        var store = new StubStore([]);
        var validator = new PriceListCatalogValidator(store);

        var findings = await validator.ValidatePublicationAsync(
            Candidate(
            Service("STITCHING", null, [BillingTestData.MainBranch]), [Group("lining", [Option("NONE", null)])]),
            TestContext.Current.CancellationToken);

        findings.ShouldBeEmpty();
        store.Reads.ShouldBe(0, "a catalogue with no link asks Billing nothing");
    }

    private static PriceListVersion PublishedVersion(IReadOnlyCollection<Guid> branches)
    {
        var version = PriceListVersion.CreateDraft(
            BillingTestData.Id("plv"), BillingTestData.Id("pl"), BillingTestData.Organisation, 1,
            BillingTestData.VersionDetails() with { BranchIds = branches }, BillingTestData.Now, null).Value;
        version.AddItem(BillingTestData.Id("i1"), BillingTestData.Id("ik1"), BillingTestData.StitchingItem(), BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        version.AddItem(BillingTestData.Id("i2"), BillingTestData.Id("ik2"), BillingTestData.StitchingItem("LINING_FULL") with { Kind = PriceItemKind.Surcharge, BaseRate = 90m }, BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        version.AddItem(BillingTestData.Id("i3"), BillingTestData.Id("ik3"), BillingTestData.StitchingItem("LINING_RETIRED") with { Kind = PriceItemKind.Surcharge, Active = false }, BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        version.Publish(BillingTestData.Now, null, "Live.").IsSuccess.ShouldBeTrue();
        return version;
    }

    private static CatalogPublicationCandidate Candidate(CatalogServiceTypeView service, IReadOnlyList<CatalogDesignGroupView> groups)
        => new(
            BillingTestData.Id("catalogue"),
            BillingTestData.Organisation,
            1,
            [new CatalogCategoryView(Blouse, BillingTestData.Id("blouse-key"), "BLOUSE_PATTERN", "Blouse", null, null, null, null, [BillingTestData.MainBranch, BillingTestData.SecondBranch])],
            [service],
            CatalogCodeHistory.Empty,
            CatalogCodeHistory.Empty,
            groups,
            [],
            CatalogCodeHistory.Empty,
            CatalogCodeHistory.Empty);

    private static CatalogServiceTypeView Service(string code, string? itemCode, IReadOnlyCollection<Guid> branches)
        => new(BillingTestData.Id($"svc-{code}"), BillingTestData.Id($"svck-{code}"), Blouse, code, code, null, null, [], itemCode, null, false, null, null, branches);

    private static CatalogDesignGroupView Group(string code, IReadOnlyList<CatalogDesignOptionView> options)
        => new(BillingTestData.Id($"g-{code}"), BillingTestData.Id($"gk-{code}"), Blouse, code, code, "SingleChoice", false, 0, null, null, [BillingTestData.MainBranch], options);

    private static CatalogDesignOptionView Option(string code, string? itemCode)
        => new(BillingTestData.Id($"o-{code}"), BillingTestData.Id($"ok-{code}"), code, code, true, true, true, true, itemCode, 0, 0);

    private sealed class StubStore(IReadOnlyList<PriceListVersion> published) : IPriceListStore
    {
        public int Reads { get; private set; }

        public Task<PriceListVersion?> FindPublishedForBranchAsync(Guid branchId, Guid organisationId, CancellationToken cancellationToken = default)
            => Task.FromResult(published.FirstOrDefault(version => version.Covers(branchId)));

        public Task<IReadOnlyList<PriceListVersion>> PublishedVersionsAsync(Guid organisationId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(published);
        }

        public Task<PriceList?> FindListAsync(Guid priceListId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PriceList?> FindListByCodeAsync(string code, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PriceList>> ListListsAsync(Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void AddList(PriceList priceList) => throw new NotSupportedException();

        public EntityTag EntityTagOf(PriceList priceList) => throw new NotSupportedException();

        public Task<PriceListVersion?> FindVersionAsync(Guid versionId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PriceListVersion?> FindPublishedAsync(Guid priceListId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PriceListVersion>> ListVersionsAsync(Guid priceListId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> NextVersionNumberAsync(Guid priceListId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PriceListLedger> ReadCodeHistoryAsync(Guid priceListId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void AddVersion(PriceListVersion version) => throw new NotSupportedException();

        public EntityTag EntityTagOf(PriceListVersion version) => throw new NotSupportedException();

        public Task<Result> SaveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result> SaveDraftAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result> SavePublicationAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
