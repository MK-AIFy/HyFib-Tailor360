using System.Text.Json;
using Shouldly;
using Tailor360.Modules.Billing.Application.Pricing;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Modules.Billing.Domain.Tax;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The pricing engine (#147): the accountant-shaped golden master in <c>tests/fixtures/billing/</c>,
/// the properties every result must hold whatever the inputs, and each refusal.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PricingEngineTests
{
    private static readonly Lazy<GoldenMaster> Master = new(GoldenMaster.Load);

    public static TheoryData<string> CaseNames => [.. Master.Value.Cases.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void MatchesTheGoldenMaster(string caseName)
    {
        var master = Master.Value;
        var @case = master.Cases.Single(c => c.Name == caseName);
        var version = master.Version(@case.TaxInclusive, @case.RoundOff);
        var request = new PricingRequest(
            BillingTestData.Organisation, BillingTestData.MainBranch, BillingTestData.Today, @case.PlaceOfSupplyStateCode, null,
            [.. @case.Lines.Select(GoldenMaster.ToLine)]);

        var result = PricingEngine.Calculate(request, version, master.TaxConfiguration, master.Registration, @case.CallerMayOverride, BillingTestData.Now);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code} {result.Error.Target}: {result.Error.Message}" : string.Empty);
        result.Value.Scheme.ToString().ShouldBe(@case.Expected.Scheme);
        result.Value.Lines.Count.ShouldBe(@case.Expected.Lines.Count);
        foreach (var (line, expected) in result.Value.Lines.Zip(@case.Expected.Lines))
        {
            line.LineKey.ShouldBe(expected.LineKey);
            line.Gross.Amount.ShouldBe(expected.Gross, line.LineKey);
            (line.Discount?.Amount.Amount ?? 0m).ShouldBe(expected.Discount, line.LineKey);
            line.TaxableValue.Amount.ShouldBe(expected.TaxableValue, line.LineKey);
            line.Taxes.ToDictionary(tax => tax.Kind, tax => tax.Amount.Amount).ShouldBe(expected.Taxes, ignoreOrder: true);
            line.LineTotal.Amount.ShouldBe(expected.LineTotal, line.LineKey);
            line.Variance.Amount.ShouldBe(expected.Variance, line.LineKey);
            line.ApprovalExercised.ShouldBe(expected.ApprovalExercised, line.LineKey);
        }

        var totals = result.Value.Totals;
        totals.Subtotal.Amount.ShouldBe(@case.Expected.Totals.Subtotal);
        totals.DiscountTotal.Amount.ShouldBe(@case.Expected.Totals.DiscountTotal);
        totals.TaxableValue.Amount.ShouldBe(@case.Expected.Totals.TaxableValue);
        totals.CentralTax.Amount.ShouldBe(@case.Expected.Totals.CentralTax);
        totals.StateTax.Amount.ShouldBe(@case.Expected.Totals.StateTax);
        totals.IntegratedTax.Amount.ShouldBe(@case.Expected.Totals.IntegratedTax);
        totals.Cess.Amount.ShouldBe(@case.Expected.Totals.Cess);
        totals.RoundOff.Amount.ShouldBe(@case.Expected.Totals.RoundOff);
        totals.GrandTotal.Amount.ShouldBe(@case.Expected.Totals.GrandTotal);
    }

    [Fact]
    public void HoldsItsPropertiesOverRandomRequestsAndWhateverOrderTheLinesArrive()
    {
        // A fixed seed: a failure reproduces, and a new case is a new seed rather than a flaky run.
        var random = new Random(147_041);
        var master = Master.Value;
        var itemCodes = master.Items.Where(item => item.Kind == "Service" && item.TaxCode != "NIL_0").Select(item => item.Code).ToArray();
        var seenHalfPaisa = false;
        var surchargeCodes = master.Items.Where(item => item.Kind == "Surcharge").Select(item => item.Code).ToArray();

        for (var iteration = 0; iteration < 250; iteration++)
        {
            var inclusive = random.Next(2) == 0;
            var version = master.Version(inclusive, random.Next(3) == 0 ? "None" : "NearestRupee");
            var lines = Enumerable.Range(0, random.Next(1, 6)).Select(index =>
            {
                var item = itemCodes[random.Next(itemCodes.Length)];
                var sameTax = surchargeCodes.Where(code => master.Items.Single(i => i.Code == code).TaxCode == master.Items.Single(i => i.Code == item).TaxCode).ToArray();
                var surcharges = sameTax.Where(_ => random.Next(2) == 0).ToArray();
                PricingDiscountRequest? discount = random.Next(3) == 0
                    ? new PricingDiscountRequest("FESTIVAL", random.Next(0, 6), null)
                    : random.Next(3) == 0 ? new PricingDiscountRequest("GOODWILL", random.Next(0, 101), null) : null;
                var quantity = random.Next(4) == 0 ? decimal.Round((decimal)random.NextDouble() * 5m + 0.25m, 2) : random.Next(1, 4);
                return new PricingLineRequest($"line-{index}", item, quantity, surcharges, discount, null);
            }).ToArray();
            var placeOfSupply = random.Next(3) == 0 ? "29" : "33";
            var request = new PricingRequest(BillingTestData.Organisation, BillingTestData.MainBranch, BillingTestData.Today, placeOfSupply, null, lines);

            var result = PricingEngine.Calculate(request, version, master.TaxConfiguration, master.Registration, false, BillingTestData.Now);
            if (result.IsFailure)
            {
                // The only refusal the generator can produce: an amount discount larger than a small line.
                result.Error.Code.ShouldBe("billing.discount-exceeds-line", $"iteration {iteration}");
                continue;
            }

            var priced = result.Value;
            foreach (var line in priced.Lines)
            {
                // Line components sum to the line: base and surcharges to gross, gross less discount to the
                // taxable value (exclusive) or to the line total (inclusive), taxable plus tax to the total.
                (line.TaxableValue + line.TaxTotal).ShouldBe(line.LineTotal, $"iteration {iteration} {line.LineKey}");
                line.Taxes.Aggregate(0m, (sum, tax) => sum + tax.Amount.Amount).ShouldBe(line.TaxTotal.Amount);
                (line.Base + line.Surcharges.Aggregate(Tailor360.Platform.Abstractions.Money.Money.Zero, (sum, s) => sum + s.Amount)).Amount
                    .ShouldBe(line.Gross.Amount, $"iteration {iteration} {line.LineKey}");
                var net = line.Gross - (line.Discount?.Amount ?? Tailor360.Platform.Abstractions.Money.Money.Zero);
                (inclusive ? line.LineTotal : line.TaxableValue).ShouldBe(net, $"iteration {iteration} {line.LineKey}");
                seenHalfPaisa |= line.Quantity != decimal.Truncate(line.Quantity);
                // Never both schemes.
                var kinds = line.Taxes.Select(tax => tax.Kind).ToHashSet();
                (kinds.Contains("IGST") && (kinds.Contains("CGST") || kinds.Contains("SGST"))).ShouldBeFalse();
                (placeOfSupply == "33" ? kinds.Contains("IGST") : kinds.Contains("CGST") || kinds.Contains("SGST")).ShouldBeFalse();
                // Every amount is to paise.
                foreach (var amount in new[] { line.TaxableValue, line.TaxTotal, line.LineTotal, line.Gross }.Concat(line.Taxes.Select(tax => tax.Amount)))
                {
                    decimal.Round(amount.Amount, 2).ShouldBe(amount.Amount);
                }
            }

            // Lines sum to the document, and the round-off is exactly the difference the rule made.
            var totals = priced.Totals;
            totals.TaxableValue.Amount.ShouldBe(priced.Lines.Sum(line => line.TaxableValue.Amount));
            totals.CentralTax.Amount.ShouldBe(priced.Lines.SelectMany(line => line.Taxes).Where(tax => tax.Kind == "CGST").Sum(tax => tax.Amount.Amount));
            totals.StateTax.Amount.ShouldBe(priced.Lines.SelectMany(line => line.Taxes).Where(tax => tax.Kind == "SGST").Sum(tax => tax.Amount.Amount));
            totals.IntegratedTax.Amount.ShouldBe(priced.Lines.SelectMany(line => line.Taxes).Where(tax => tax.Kind == "IGST").Sum(tax => tax.Amount.Amount));
            totals.Cess.Amount.ShouldBe(priced.Lines.SelectMany(line => line.Taxes).Where(tax => tax.Kind == "CESS").Sum(tax => tax.Amount.Amount));
            var unrounded = priced.Lines.Sum(line => line.LineTotal.Amount);
            totals.RoundOff.Amount.ShouldBe(totals.GrandTotal.Amount - unrounded);
            if (version.RoundOff == RoundOffRule.NearestRupee)
            {
                decimal.Round(totals.GrandTotal.Amount, 0).ShouldBe(totals.GrandTotal.Amount);
                Math.Abs(totals.RoundOff.Amount).ShouldBeLessThanOrEqualTo(0.5m);
            }
            else
            {
                totals.RoundOff.Amount.ShouldBe(0m);
            }

            // The same lines in another order: the same totals, the same lines under their keys.
            var shuffled = request with { Lines = [.. lines.OrderBy(_ => random.Next())] };
            var again = PricingEngine.Calculate(shuffled, version, master.TaxConfiguration, master.Registration, false, BillingTestData.Now).Value;
            again.Totals.ShouldBe(totals);
            foreach (var line in priced.Lines)
            {
                // Compared as JSON: the record's collections compare by reference, the figures are what matter.
                PricingJson.Write(again with { Lines = [again.Lines.Single(candidate => candidate.LineKey == line.LineKey)] })
                    .ShouldBe(PricingJson.Write(priced with { Lines = [line] }));
            }

            // Re-running from the stored request reproduces the stored result, byte for byte.
            var replayed = PricingEngine.Calculate(PricingJson.ReadRequest(PricingJson.Write(request)), version, master.TaxConfiguration, master.Registration, false, BillingTestData.Now).Value;
            PricingJson.Write(replayed).ShouldBe(PricingJson.Write(priced));
            PricingJson.Write(PricingJson.ReadResult(PricingJson.Write(priced))).ShouldBe(PricingJson.Write(priced));
        }

        seenHalfPaisa.ShouldBeTrue("the generator produced fractional quantities, so half-paisa products were exercised");
    }

    [Fact]
    public void ReadsTheSnapshotShapeOfRecord()
    {
        // A snapshot written by this build, committed as a fixture: a later build that changes the shape
        // must still read it, or bump CalculationSnapshot.CurrentSchemaVersion and read it by version.
        var json = File.ReadAllText(Path.Combine(FixtureDirectory(), "calculation-snapshot-v1.json"));
        using var document = JsonDocument.Parse(json);
        var request = PricingJson.ReadRequest(document.RootElement.GetProperty("request").GetRawText());
        var result = PricingJson.ReadResult(document.RootElement.GetProperty("result").GetRawText());

        document.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(CalculationSnapshot.CurrentSchemaVersion);
        request.Lines.Single().ItemCode.ShouldBe("BLOUSE_PATTERN_STITCHING");
        result.Lines.Single().LineTotal.Amount.ShouldBe(609m);
        result.Totals.GrandTotal.Amount.ShouldBe(609m);
        result.Lines.Single().Taxes.Select(tax => tax.Kind).ShouldBe(["CGST", "SGST"]);

        // And this build reproduces it from the stored request against the golden master's configuration.
        var master = Master.Value;
        var replayed = PricingEngine.Calculate(request, master.Version(false, "NearestRupee"), master.TaxConfiguration, master.Registration, false, result.CalculatedAt).Value;
        PricingJson.Write(replayed with { PriceListVersionId = result.PriceListVersionId, TaxConfigurationVersionId = result.TaxConfigurationVersionId, GstRegistrationId = result.GstRegistrationId })
            .ShouldBe(PricingJson.Write(result));
    }

    private static string FixtureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HyFib.Tailor360.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the repository root holds the fixtures");
        return Path.Combine(directory.FullName, "tests", "fixtures", "billing");
    }

    [Fact]
    public void RefusesWhatItCannotPriceNamingTheField()
    {
        var master = Master.Value;
        var version = master.Version(false, "NearestRupee");

        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING")).ShouldBeNull();
        Refusal(version, master).ShouldBe(("billing.lines-required", "lines"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING"), Line("g1", "ITEM_341")).ShouldBe(("billing.line-key-duplicated", "lines[g1].lineKey"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", quantity: 0m)).ShouldBe(("billing.quantity-not-positive", "lines[g1].quantity"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", quantity: 1.00001m)).ShouldBe(("billing.quantity-not-positive", "lines[g1].quantity"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", quantity: PricingEngine.MaximumQuantity + 1m)).ShouldBe(("billing.quantity-not-positive", "lines[g1].quantity"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", @override: new(470m, "Agreed\0at the counter."))).ShouldBe(("billing.reason-not-well-formed", "lines[g1].override.reason"));
        // 10.004% over a 10% threshold: judged unrounded, so it needs the permission.
        Refusal(version, master, Line("g1", "RATE_92_10", @override: new(101.3137m, "Just over."))).ShouldBe(("billing.approval-required", "lines[g1].override.rate"));
        Refusal(version, master, Line("g1", "NOBODY")).ShouldBe(("billing.item-not-priced", "lines[g1].itemCode"));
        Refusal(version, master, Line("g1", "RETIRED_ITEM")).ShouldBe(("billing.item-not-priced", "lines[g1].itemCode"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", surcharges: ["NOBODY"])).ShouldBe(("billing.item-not-priced", "lines[g1].surchargeItemCodes[0]"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", surcharges: ["BLOUSE_PATTERN_STITCHING"])).ShouldBe(("billing.item-not-a-surcharge", "lines[g1].surchargeItemCodes[0]"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", surcharges: ["MAT_AD_STONE_BEAD_KIT"])).ShouldBe(("billing.item-not-a-surcharge", "lines[g1].surchargeItemCodes[0]"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", surcharges: ["PI_GOODS_SURCHARGE"])).ShouldBe(("billing.surcharge-taxed-differently", "lines[g1].surchargeItemCodes[0]"));
        Refusal(version, master, Line("g1", "ORPHAN_TAX_ITEM")).ShouldBe(("billing.configuration-missing", "lines[g1].itemCode"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", discount: new("NOBODY", 5m, null))).ShouldBe(("billing.discount-rule-not-in-force", "lines[g1].discount.ruleCode"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", discount: new("FESTIVAL", 16m, "Too much."))).ShouldBe(("billing.discount-above-maximum", "lines[g1].discount.value"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", discount: new("FESTIVAL", 10m, "Above the counter's threshold."))).ShouldBe(("billing.approval-required", "lines[g1].discount.value"));
        Refusal(version, master, true, Line("g1", "BLOUSE_PATTERN_STITCHING", discount: new("FESTIVAL", 10m, null))).ShouldBe(("billing.reason-required", "lines[g1].discount.reason"));
        Refusal(version, master, true, Line("g1", "BLOUSE_PATTERN_STITCHING", discount: new("FESTIVAL", 10m, "Manager agreed."))).ShouldBeNull();
        Refusal(version, master, Line("g1", "ITEM_341", discount: new("GOODWILL", 400m, "A lot."))).ShouldBe(("billing.approval-required", "lines[g1].discount.value"));
        Refusal(version, master, true, Line("g1", "ITEM_341", discount: new("GOODWILL", 400m, "A lot."))).ShouldBe(("billing.discount-exceeds-line", "lines[g1].discount.value"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", @override: new(580m, "Sample rate."))).ShouldBe(("billing.approval-required", "lines[g1].override.rate"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", @override: new(470m, ""))).ShouldBe(("billing.reason-required", "lines[g1].override.reason"));
        Refusal(version, master, Line("g1", "BLOUSE_PATTERN_STITCHING", @override: new(-1m, "Free."))).ShouldBe(("billing.override-rate-not-well-formed", "lines[g1].override.rate"));
        Refusal(version, master, true, Line("g1", "BLOUSE_PATTERN_STITCHING", @override: new(0m, "Goodwill: free of charge."))).ShouldBeNull();

        // Not this branch, not a state code.
        var elsewhere = new PricingRequest(BillingTestData.Organisation, BillingTestData.SecondBranch, BillingTestData.Today, "33", null, [Line("g1", "ITEM_341")]);
        PricingEngine.Calculate(elsewhere, version, master.TaxConfiguration, master.Registration, false, BillingTestData.Now)
            .Error.ShouldSatisfyAllConditions(error => error.Code.ShouldBe("billing.configuration-missing"), error => error.Target.ShouldBe("branchId"));
        var nowhere = new PricingRequest(BillingTestData.Organisation, BillingTestData.MainBranch, BillingTestData.Today, "3", null, [Line("g1", "ITEM_341")]);
        PricingEngine.Calculate(nowhere, version, master.TaxConfiguration, master.Registration, false, BillingTestData.Now)
            .Error.Code.ShouldBe("billing.state-code-not-well-formed");
    }

    private static PricingLineRequest Line(
        string key, string item, decimal quantity = 1m, string[]? surcharges = null, PricingDiscountRequest? discount = null, PricingOverrideRequest? @override = null)
        => new(key, item, quantity, surcharges ?? [], discount, @override);

    private static (string Code, string? Target)? Refusal(PriceListVersion version, GoldenMaster master, params PricingLineRequest[] lines)
        => Refusal(version, master, false, lines);

    private static (string Code, string? Target)? Refusal(PriceListVersion version, GoldenMaster master, bool mayOverride, params PricingLineRequest[] lines)
    {
        var request = new PricingRequest(BillingTestData.Organisation, BillingTestData.MainBranch, BillingTestData.Today, "33", null, lines);
        var result = PricingEngine.Calculate(request, version, master.TaxConfiguration, master.Registration, mayOverride, BillingTestData.Now);

        return result.IsSuccess ? null : (result.Error.Code, result.Error.Target);
    }

    /// <summary>The fixture, read from <c>tests/fixtures/billing/pricing-golden-master.json</c> and built into domain objects.</summary>
    private sealed class GoldenMaster
    {
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        public required IReadOnlyList<ItemFixture> Items { get; init; }

        public required IReadOnlyList<TaxCodeFixture> TaxCodes { get; init; }

        public required IReadOnlyList<RuleFixture> DiscountRules { get; init; }

        public required IReadOnlyList<CaseFixture> Cases { get; init; }

        public required string RegistrationStateCode { get; init; }

        public required decimal OverrideThresholdPercent { get; init; }

        public TaxConfigurationVersion TaxConfiguration { get; private set; } = null!;

        public GstRegistration Registration { get; private set; } = null!;

        public static GoldenMaster Load()
        {
            var master = JsonSerializer.Deserialize<GoldenMaster>(File.ReadAllText(FixturePath()), Options)!;
            var configuration = BillingTestData.Draft();
            foreach (var code in master.TaxCodes)
            {
                var rates = code.Rates.Select(rate => new TaxRate(Enum.Parse<TaxComponentKind>(rate.Key), rate.Value)).ToArray();
                configuration.AddTaxCode(
                    BillingTestData.Id($"gm-tc-{code.Code}"), BillingTestData.Id($"gm-tck-{code.Code}"),
                    new TaxCodeDetails(code.Code, code.Description, code.Classification, Enum.Parse<TaxCodeKind>(code.Kind), true, rates),
                    BillingTestData.Now, null).IsSuccess.ShouldBeTrue(code.Code);
            }

            configuration.Publish(BillingTestData.Now, null, "Golden master.").IsSuccess.ShouldBeTrue();
            master.TaxConfiguration = configuration;
            master.Registration = GstRegistration.Create(
                BillingTestData.Id("gm-registration"), BillingTestData.Organisation,
                BillingTestData.Registration(stateCode: master.RegistrationStateCode), BillingTestData.Now, null).Value;

            return master;
        }

        /// <summary>A published version holding every item and rule of the fixture, plus one retired item and one whose tax code nobody published.</summary>
        public PriceListVersion Version(bool taxInclusive, string roundOff)
        {
            var details = BillingTestData.VersionDetails() with
            {
                TaxInclusive = taxInclusive,
                RoundOff = Enum.Parse<RoundOffRule>(roundOff),
                OverrideThresholdPercent = OverrideThresholdPercent,
            };
            var version = PriceListVersion.CreateDraft(BillingTestData.Id($"gm-v-{taxInclusive}-{roundOff}"), BillingTestData.Id("gm-list"), BillingTestData.Organisation, 1, details, BillingTestData.Now, null).Value;
            foreach (var item in Items)
            {
                version.AddItem(
                    BillingTestData.Id($"gm-item-{item.Code}"), BillingTestData.Id($"gm-itemkey-{item.Code}"),
                    new PriceListItemDetails(item.Code, item.Description, Enum.Parse<PriceItemKind>(item.Kind), item.Rate, "each", item.TaxCode, true),
                    BillingTestData.Now, null).IsSuccess.ShouldBeTrue(item.Code);
            }

            version.AddItem(BillingTestData.Id("gm-item-retired"), BillingTestData.Id("gm-itemkey-retired"),
                new PriceListItemDetails("RETIRED_ITEM", "No longer offered", PriceItemKind.Service, 100m, "each", "SAC_998821_5", false), BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
            version.AddItem(BillingTestData.Id("gm-item-goods-surcharge"), BillingTestData.Id("gm-itemkey-goods-surcharge"),
                new PriceListItemDetails("PI_GOODS_SURCHARGE", "A surcharge taxed as goods", PriceItemKind.Surcharge, 50m, "each", "HSN_2402_12_CESS_1", true), BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
            version.AddItem(BillingTestData.Id("gm-item-orphan"), BillingTestData.Id("gm-itemkey-orphan"),
                new PriceListItemDetails("ORPHAN_TAX_ITEM", "Names a tax code nobody published", PriceItemKind.Service, 100m, "each", "NOBODY_PUBLISHED", true), BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
            foreach (var rule in DiscountRules)
            {
                version.AddDiscountRule(
                    BillingTestData.Id($"gm-rule-{rule.Code}"), BillingTestData.Id($"gm-rulekey-{rule.Code}"),
                    new DiscountRuleDetails(rule.Code, rule.Description, Enum.Parse<DiscountKind>(rule.Kind), rule.MaximumWithoutApproval, rule.Maximum, true),
                    BillingTestData.Now, null).IsSuccess.ShouldBeTrue(rule.Code);
            }

            version.Publish(BillingTestData.Now, null, "Golden master.").IsSuccess.ShouldBeTrue();
            return version;
        }

        public static PricingLineRequest ToLine(LineFixture line)
            => new(
                line.LineKey, line.ItemCode, line.Quantity, line.SurchargeItemCodes,
                line.Discount is null ? null : new PricingDiscountRequest(line.Discount.RuleCode, line.Discount.Value, line.Discount.Reason),
                line.Override is null ? null : new PricingOverrideRequest(line.Override.Rate, line.Override.Reason));

        private static string FixturePath() => Path.Combine(FixtureDirectory(), "pricing-golden-master.json");
    }

    private sealed record ItemFixture(string Code, string Description, string Kind, decimal Rate, string TaxCode);

    private sealed record TaxCodeFixture(string Code, string Description, string Classification, string Kind, Dictionary<string, decimal> Rates);

    private sealed record RuleFixture(string Code, string Description, string Kind, decimal MaximumWithoutApproval, decimal Maximum);

    private sealed record CaseFixture(string Name, bool TaxInclusive, string RoundOff, string PlaceOfSupplyStateCode, bool CallerMayOverride, IReadOnlyList<LineFixture> Lines, ExpectedFixture Expected);

    private sealed record LineFixture(string LineKey, string ItemCode, decimal Quantity, IReadOnlyList<string> SurchargeItemCodes, DiscountFixture? Discount, OverrideFixture? Override);

    private sealed record DiscountFixture(string RuleCode, decimal Value, string? Reason);

    private sealed record OverrideFixture(decimal Rate, string Reason);

    private sealed record ExpectedFixture(string Scheme, IReadOnlyList<ExpectedLine> Lines, ExpectedTotals Totals);

    private sealed record ExpectedLine(string LineKey, decimal Gross, decimal Discount, decimal TaxableValue, Dictionary<string, decimal> Taxes, decimal LineTotal, decimal Variance, bool ApprovalExercised);

    private sealed record ExpectedTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxableValue, decimal CentralTax, decimal StateTax, decimal IntegratedTax, decimal Cess, decimal RoundOff, decimal GrandTotal);
}
