using Shouldly;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Platform.Abstractions.Identifiers;

namespace Tailor360.UnitTests.Billing;

/// <summary>The price list and version aggregates (#146): what a draft accepts, what a published version refuses, what a clone carries.</summary>
[Trait("Category", "Unit")]
public sealed class PriceListTests
{
    [Fact]
    public void CreatesAListAndRenamesItWithoutChangingItsCode()
    {
        var created = PriceList.Create(BillingTestData.Id("pl"), BillingTestData.Organisation, "PL_CBE01", " Coimbatore ", BillingTestData.Now, null);
        created.IsSuccess.ShouldBeTrue();
        created.Value.Name.ShouldBe("Coimbatore");
        created.Value.Rename("Coimbatore and Tiruppur", BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        created.Value.Code.ShouldBe("PL_CBE01");

        PriceList.Create(BillingTestData.Id("pl"), BillingTestData.Organisation, "pl_cbe01", "x", BillingTestData.Now, null)
            .Error.Code.ShouldBe("billing.code-not-well-formed");
        created.Value.Rename(" ", BillingTestData.Now, null).Error.Target.ShouldBe("name");
    }

    [Fact]
    public void AddsEditsAndRemovesItemsAndRulesOnADraft()
    {
        var version = BillingTestData.PriceListDraft();

        var item = version.AddItem(BillingTestData.Id("i1"), BillingTestData.Id("ik1"), BillingTestData.StitchingItem(), BillingTestData.Now, null);
        item.IsSuccess.ShouldBeTrue(item.IsFailure ? item.Error.Message : string.Empty);
        item.Value.BaseRate.ShouldBe(450m);

        var edited = version.EditItem(item.Value.Id, BillingTestData.StitchingItem() with { BaseRate = 472.5m }, BillingTestData.Now, null);
        edited.IsSuccess.ShouldBeTrue();
        edited.Value.BaseRate.ShouldBe(472.5m);

        var rule = version.AddDiscountRule(BillingTestData.Id("r1"), BillingTestData.Id("rk1"), BillingTestData.FestivalRule(), BillingTestData.Now, null);
        rule.IsSuccess.ShouldBeTrue(rule.IsFailure ? rule.Error.Message : string.Empty);
        version.EditDiscountRule(rule.Value.Id, BillingTestData.FestivalRule() with { Maximum = 20m }, BillingTestData.Now, null).Value.Maximum.ShouldBe(20m);

        version.RemoveItem(item.Value.Id, BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        version.RemoveDiscountRule(rule.Value.Id, BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        version.Items.ShouldBeEmpty();
        version.DiscountRules.ShouldBeEmpty();
        version.RemoveItem(item.Value.Id, BillingTestData.Now, null).Error.Code.ShouldBe("billing.item-not-found");
        version.RemoveDiscountRule(rule.Value.Id, BillingTestData.Now, null).Error.Code.ShouldBe("billing.discount-rule-not-found");
    }

    [Fact]
    public void RefusesMalformedItemsAndRules()
    {
        var version = BillingTestData.PriceListDraft();
        version.AddItem(BillingTestData.Id("i1"), BillingTestData.Id("ik1"), BillingTestData.StitchingItem(), BillingTestData.Now, null).IsSuccess.ShouldBeTrue();

        Add(version, BillingTestData.StitchingItem()).Error.Code.ShouldBe("billing.code-not-unique");
        Add(version, BillingTestData.StitchingItem("OTHER") with { BaseRate = -1m }).Error.Code.ShouldBe("billing.rate-not-well-formed");
        Add(version, BillingTestData.StitchingItem("OTHER") with { BaseRate = 1.23456m }).Error.Code.ShouldBe("billing.rate-not-well-formed");
        Add(version, BillingTestData.StitchingItem("OTHER") with { Unit = "Each" }).Error.Code.ShouldBe("billing.unit-not-well-formed");
        Add(version, BillingTestData.StitchingItem("OTHER") with { TaxCode = "stitching" }).Error.Target.ShouldBe("taxCode");
        Add(version, BillingTestData.StitchingItem("OTHER") with { Kind = (PriceItemKind)9 }).Error.Target.ShouldBe("kind");
        Add(version, BillingTestData.StitchingItem("OTHER") with { Description = " " }).Error.Target.ShouldBe("description");

        var rule = BillingTestData.FestivalRule();
        AddRule(version, rule with { MaximumWithoutApproval = 20m, Maximum = 15m }).Error.Code.ShouldBe("billing.discount-bounds-not-ordered");
        AddRule(version, rule with { Maximum = 100.5m }).Error.Code.ShouldBe("billing.rate-out-of-range");
        AddRule(version, rule with { Kind = DiscountKind.Amount, MaximumWithoutApproval = 10.005m, Maximum = 50m }).Error.Code.ShouldBe("billing.amount-not-well-formed");
        AddRule(version, rule with { Kind = DiscountKind.Amount, MaximumWithoutApproval = 10m, Maximum = 50m }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void RefusesAVersionWithoutADateAThresholdOutOfRangeOrAnEmptyBranch()
    {
        var details = BillingTestData.VersionDetails();
        Create(details with { EffectiveFrom = default }).Error.Target.ShouldBe("effectiveFrom");
        Create(details with { OverrideThresholdPercent = 101m }).Error.Target.ShouldBe("overrideThresholdPercent");
        Create(details with { OverrideThresholdPercent = -1m }).Error.Target.ShouldBe("overrideThresholdPercent");
        Create(details with { RoundOff = (RoundOffRule)7 }).Error.Target.ShouldBe("roundOff");
        Create(details with { BranchIds = [Guid.Empty] }).Error.Target.ShouldBe("branchIds");
        Create(details with { Name = "" }).Error.Target.ShouldBe("name");
        Create(details with { BranchIds = [] }).IsSuccess.ShouldBeTrue("no branch is a publication finding, not a shape error");
    }

    [Fact]
    public void RefusesEveryChangeOncePublishedAndAClonesCarryTheContentsWithTheSameKeys()
    {
        var version = BillingTestData.PriceListDraft();
        var item = Add(version, BillingTestData.StitchingItem()).Value;
        var rule = AddRule(version, BillingTestData.FestivalRule()).Value;
        version.Publish(BillingTestData.Now, null, "Approved.").IsSuccess.ShouldBeTrue();

        Add(version, BillingTestData.StitchingItem("LATE")).Error.Code.ShouldBe("billing.version-not-editable");
        version.EditItem(item.Id, item.Details, BillingTestData.Now, null).Error.Code.ShouldBe("billing.version-not-editable");
        version.RemoveItem(item.Id, BillingTestData.Now, null).Error.Code.ShouldBe("billing.version-not-editable");
        AddRule(version, BillingTestData.FestivalRule("LATE")).Error.Code.ShouldBe("billing.version-not-editable");
        version.Describe(version.Details, BillingTestData.Now, null).Error.Code.ShouldBe("billing.version-not-editable");
        version.Publish(BillingTestData.Now, null, "Again.").Error.Code.ShouldBe("billing.version-not-publishable");

        var clone = version.CloneAsDraft(new SequentialIds(), 2, version.Details with { Name = "Version 2" }, BillingTestData.Now, null).Value;
        clone.Status.ShouldBe(PriceListVersionStatus.Draft);
        clone.PriceListId.ShouldBe(version.PriceListId);
        clone.ClonedFromVersionId.ShouldBe(version.Id);
        clone.Items.Single().Key.ShouldBe(item.Key);
        clone.Items.Single().Id.ShouldNotBe(item.Id);
        clone.Items.Single().PriceListVersionId.ShouldBe(clone.Id);
        clone.DiscountRules.Single().Key.ShouldBe(rule.Key);
        clone.BranchIds.ShouldBe(version.BranchIds);
        clone.Covers(BillingTestData.MainBranch).ShouldBeTrue();
        clone.Covers(BillingTestData.SecondBranch).ShouldBeFalse();

        version.Retire(BillingTestData.Now, null, "Superseded.").IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(PriceListVersionStatus.Retired);
    }

    private static Tailor360.Platform.Abstractions.Results.Result<PriceListVersion> Create(PriceListVersionDetails details)
        => PriceListVersion.CreateDraft(BillingTestData.Id("v"), BillingTestData.Id("pl"), BillingTestData.Organisation, 1, details, BillingTestData.Now, null);

    private static Tailor360.Platform.Abstractions.Results.Result<PriceListItem> Add(PriceListVersion version, PriceListItemDetails details)
        => version.AddItem(BillingTestData.Id($"i-{details.Code}-{details.GetHashCode()}"), BillingTestData.Id($"ik-{details.Code}"), details, BillingTestData.Now, null);

    private static Tailor360.Platform.Abstractions.Results.Result<DiscountRule> AddRule(PriceListVersion version, DiscountRuleDetails details)
        => version.AddDiscountRule(BillingTestData.Id($"r-{details.Code}-{details.GetHashCode()}"), BillingTestData.Id($"rk-{details.Code}"), details, BillingTestData.Now, null);

    private sealed class SequentialIds : IIdGenerator
    {
        private int _next;

        public Guid NewId() => BillingTestData.Id($"pl-sequential-{++_next}");
    }
}
