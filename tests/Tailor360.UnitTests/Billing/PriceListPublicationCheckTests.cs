using Shouldly;
using Tailor360.Modules.Billing.Application;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Pricing;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Modules.Catalog.Contracts.Catalogue;

namespace Tailor360.UnitTests.Billing;

/// <summary>The price-list publication checks (#146): each with a minimal failing version, and a clean one they pass.</summary>
[Trait("Category", "Unit")]
public sealed class PriceListPublicationCheckTests
{
    [Fact]
    public void FindsNothingWrongWithAWellFormedVersion()
    {
        var version = BillingTestData.PriceListDraft();
        Add(version, BillingTestData.StitchingItem());
        AddRule(version, BillingTestData.FestivalRule());

        var report = PriceListPublicationCheck.Run(version, PriceListLedger.Empty, null, PublishedTaxConfiguration(), [], []);

        report.HasErrors.ShouldBeFalse(string.Join("; ", report.Findings.Select(finding => finding.Message)));
        report.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void RefusesAnUnknownOrRetiredTaxCodeAndAMissingTaxConfiguration()
    {
        var version = BillingTestData.PriceListDraft();
        Add(version, BillingTestData.StitchingItem("UNKNOWN") with { TaxCode = "NOBODY_PUBLISHED" });
        Add(version, BillingTestData.StitchingItem("RETIRED") with { TaxCode = "RETIRED_CODE" });
        Add(version, BillingTestData.StitchingItem("RETIRED_ITEM") with { TaxCode = "RETIRED_CODE", Active = false });

        var report = PriceListPublicationCheck.Run(version, PriceListLedger.Empty, null, PublishedTaxConfiguration(), [], []);
        var errors = report.Findings.Where(finding => finding.Severity == BillingFindingSeverity.Error).Select(finding => (finding.Code, finding.Target)).ToArray();
        errors.ShouldBe(
        [
            ("billing.tax-code-retired", "items[RETIRED].taxCode"),
            ("billing.tax-code-unknown", "items[UNKNOWN].taxCode"),
        ]);

        var none = PriceListPublicationCheck.Run(version, PriceListLedger.Empty, null, null, [], []);
        none.Findings.Single(finding => finding.Code == "billing.tax-configuration-missing").Target.ShouldBe("items");
    }

    [Fact]
    public void RefusesABranchAnotherListAlreadyPricesAndNoBranchAtAll()
    {
        var version = BillingTestData.PriceListDraft();
        var other = PriceListVersion.CreateDraft(
            BillingTestData.Id("other-v"), BillingTestData.Id("other-list"), BillingTestData.Organisation, 1,
            BillingTestData.VersionDetails(), BillingTestData.Now, null).Value;
        other.Publish(BillingTestData.Now, null, "Live.").IsSuccess.ShouldBeTrue();

        var clash = PriceListPublicationCheck.Run(version, PriceListLedger.Empty, null, PublishedTaxConfiguration(), [other], []);
        clash.Findings.Single(finding => finding.Code == "billing.branch-priced-twice").Target.ShouldBe("branchIds");

        // The list's own published version is not "another list": a clone supersedes it on the same branches.
        var own = PriceListVersion.CreateDraft(
            BillingTestData.Id("own-v"), version.PriceListId, BillingTestData.Organisation, 1,
            BillingTestData.VersionDetails(), BillingTestData.Now, null).Value;
        own.Publish(BillingTestData.Now, null, "Live.").IsSuccess.ShouldBeTrue();
        PriceListPublicationCheck.Run(version, PriceListLedger.Empty, own, PublishedTaxConfiguration(), [own], [])
            .Findings.ShouldNotContain(finding => finding.Code == "billing.branch-priced-twice");

        var nowhere = PriceListVersion.CreateDraft(
            BillingTestData.Id("nowhere"), BillingTestData.Id("pl"), BillingTestData.Organisation, 3,
            BillingTestData.VersionDetails() with { BranchIds = [] }, BillingTestData.Now, null).Value;
        PriceListPublicationCheck.Run(nowhere, PriceListLedger.Empty, null, PublishedTaxConfiguration(), [], [])
            .Findings.Single(finding => finding.Code == "billing.no-branches").Severity.ShouldBe(BillingFindingSeverity.Error);
    }

    [Fact]
    public void RefusesARespelledOrReusedCodeAndAnEarlierEffectiveDate()
    {
        var version = BillingTestData.PriceListDraft(number: 2, effectiveFrom: new DateOnly(2026, 3, 1));
        var respelled = Add(version, BillingTestData.StitchingItem("STITCHING_NEW"));
        var reused = AddRule(version, BillingTestData.FestivalRule("BLOUSE_PATTERN_STITCHING"));
        var ledger = new PriceListLedger(
            new Dictionary<Guid, string> { [respelled.Key] = "BLOUSE_PATTERN_STITCHING" },
            new Dictionary<string, Guid>(StringComparer.Ordinal) { ["BLOUSE_PATTERN_STITCHING"] = respelled.Key },
            new Dictionary<Guid, string>(),
            new Dictionary<string, Guid>(StringComparer.Ordinal) { ["BLOUSE_PATTERN_STITCHING"] = BillingTestData.Id("some-other-rule") });
        var published = BillingTestData.PriceListDraft(number: 1, effectiveFrom: new DateOnly(2026, 4, 1));
        published.Publish(BillingTestData.Now, null, "Live.").IsSuccess.ShouldBeTrue();

        var report = PriceListPublicationCheck.Run(version, ledger, published, PublishedTaxConfiguration(), [published], []);

        report.Findings.Where(finding => finding.Severity == BillingFindingSeverity.Error)
            .Select(finding => (finding.Code, finding.Target))
            .ShouldBe(
            [
                ("billing.code-reused", $"discountRules[{reused.Code}].code"),
                ("billing.effective-from-before-published", "effectiveFrom"),
                ("billing.published-code-changed", $"items[{respelled.Code}].code"),
            ]);
    }

    [Fact]
    public void RefusesToStrandWhatThePublishedCatalogueOffers()
    {
        // The catalogue offers a stitching service at the main branch against BLOUSE_PATTERN_STITCHING and
        // a lining option at another branch against LINING. The draft prices the main branch only.
        var other = BillingTestData.Id("other-branch");
        IReadOnlyList<CatalogPriceListItemReference> references =
        [
            new("BLOUSE.STITCHING", "BLOUSE_PATTERN_STITCHING", [BillingTestData.MainBranch]),
            new("BLOUSE.LINING.SILK", "LINING", [other]),
        ];

        // Holding the item active: nothing is stranded, and the other branch is not this draft's concern.
        var priced = BillingTestData.PriceListDraft();
        Add(priced, BillingTestData.StitchingItem());
        PriceListPublicationCheck.Run(priced, PriceListLedger.Empty, null, PublishedTaxConfiguration(), [], references)
            .HasErrors.ShouldBeFalse();

        // Dropping it, or retiring it, at a branch the draft prices: refused, naming the item.
        var dropped = BillingTestData.PriceListDraft(number: 2);
        PriceListPublicationCheck.Run(dropped, PriceListLedger.Empty, null, PublishedTaxConfiguration(), [], references)
            .Findings.Single(finding => finding.Code == "billing.catalogue-reference-lost").Target.ShouldBe("items[BLOUSE_PATTERN_STITCHING]");
        var retired = BillingTestData.PriceListDraft(number: 3);
        Add(retired, BillingTestData.StitchingItem() with { Active = false });
        PriceListPublicationCheck.Run(retired, PriceListLedger.Empty, null, PublishedTaxConfiguration(), [], references)
            .Findings.Single(finding => finding.Code == "billing.catalogue-reference-lost").Message.ShouldContain("retires");

        // A draft that no longer prices a branch the published version priced, where the catalogue offers
        // something priced and no other list has stepped in: refused. With another list pricing it: allowed.
        var published = PriceListVersion.CreateDraft(
            BillingTestData.Id("published-both"), BillingTestData.Id("pl"), BillingTestData.Organisation, 4,
            BillingTestData.VersionDetails() with { BranchIds = [BillingTestData.MainBranch, other] }, BillingTestData.Now, null).Value;
        Add(published, BillingTestData.StitchingItem());
        published.Publish(BillingTestData.Now, null, "Live.").IsSuccess.ShouldBeTrue();
        var narrowed = BillingTestData.PriceListDraft(number: 5);
        Add(narrowed, BillingTestData.StitchingItem());
        PriceListPublicationCheck.Run(narrowed, PriceListLedger.Empty, published, PublishedTaxConfiguration(), [published], references)
            .Findings.Single(finding => finding.Code == "billing.branch-left-unpriced").Target.ShouldBe("branchIds");

        var elsewhere = PriceListVersion.CreateDraft(
            BillingTestData.Id("elsewhere"), BillingTestData.Id("other-list"), BillingTestData.Organisation, 1,
            BillingTestData.VersionDetails() with { BranchIds = [other] }, BillingTestData.Now, null).Value;
        elsewhere.Publish(BillingTestData.Now, null, "Live.").IsSuccess.ShouldBeTrue();
        PriceListPublicationCheck.Run(narrowed, PriceListLedger.Empty, published, PublishedTaxConfiguration(), [published, elsewhere], references)
            .Findings.ShouldNotContain(finding => finding.Code == "billing.branch-left-unpriced");

        // A branch the catalogue offers nothing priced at may be dropped freely.
        PriceListPublicationCheck.Run(narrowed, PriceListLedger.Empty, published, PublishedTaxConfiguration(), [published], [references[0]])
            .Findings.ShouldNotContain(finding => finding.Code == "billing.branch-left-unpriced");
    }

    private static TaxConfigurationVersion PublishedTaxConfiguration()
    {
        var configuration = BillingTestData.Draft();
        configuration.AddTaxCode(BillingTestData.Id("tc1"), BillingTestData.Id("tck1"), BillingTestData.ServiceCode(), BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        configuration.AddTaxCode(BillingTestData.Id("tc2"), BillingTestData.Id("tck2"), BillingTestData.ServiceCode("RETIRED_CODE", active: false), BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        configuration.Publish(BillingTestData.Now, null, "Live.").IsSuccess.ShouldBeTrue();
        return configuration;
    }

    private static PriceListItem Add(PriceListVersion version, PriceListItemDetails details)
    {
        var added = version.AddItem(BillingTestData.Id($"item-{details.Code}"), BillingTestData.Id($"item-key-{details.Code}"), details, BillingTestData.Now, null);
        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Message : string.Empty);
        return added.Value;
    }

    private static DiscountRule AddRule(PriceListVersion version, DiscountRuleDetails details)
    {
        var added = version.AddDiscountRule(BillingTestData.Id($"rule-{details.Code}"), BillingTestData.Id($"rule-key-{details.Code}"), details, BillingTestData.Now, null);
        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Message : string.Empty);
        return added.Value;
    }
}
