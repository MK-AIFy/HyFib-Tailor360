using System.Security.Cryptography;
using System.Text;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Modules.Billing.Domain.Tax;

namespace Tailor360.UnitTests.Billing;

/// <summary>Synthetic identifiers and details for the Billing unit tests. No real GSTIN appears here.</summary>
internal static class BillingTestData
{
    public static Guid Organisation { get; } = Id("organisation");

    public static Guid MainBranch { get; } = Id("branch-main");

    public static Guid SecondBranch { get; } = Id("branch-second");

    public static DateTimeOffset Now { get; } = new(2026, 9, 12, 4, 30, 0, TimeSpan.Zero);

    public static DateOnly Today { get; } = new(2026, 9, 12);

    /// <summary>A GSTIN of the right shape whose check character agrees; the PAN inside it is made up.</summary>
    public const string WellFormedGstin = "33AAACH7409R1Z8";

    public static Guid Id(string name)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16));

    public static TaxConfigurationVersion Draft(int number = 1, DateOnly? effectiveFrom = null)
        => TaxConfigurationVersion.CreateDraft(
                Id($"tax-version-{number}"), Organisation, number, $"Version {number}", null,
                effectiveFrom ?? Today, Now, null)
            .Value;

    public static TaxCodeDetails ServiceCode(
        string code = "STITCHING_5",
        decimal half = 2.5m,
        string classification = "998822",
        bool active = true)
        => new(
            code,
            "Tailoring services",
            classification,
            TaxCodeKind.Services,
            active,
            [
                new TaxRate(TaxComponentKind.Cgst, half),
                new TaxRate(TaxComponentKind.Sgst, half),
                new TaxRate(TaxComponentKind.Igst, half * 2),
            ]);

    public static TaxCodeDetails NilRated(string code = "EXEMPT")
        => new(code, "Exempt supply", "9988", TaxCodeKind.Services, true, []);

    public static GstRegistrationDetails Registration(
        Guid? branch = null,
        string gstin = WellFormedGstin,
        string stateCode = "33",
        DateOnly? from = null,
        DateOnly? to = null)
        => new(branch ?? MainBranch, gstin, stateCode, "Example Tailors Private Limited", "Example Tailors", from ?? new DateOnly(2026, 4, 1), to);

    /// <summary>A registration whose first day was never given.</summary>
    public static GstRegistrationDetails RegistrationWithoutAFirstDay()
        => Registration() with { EffectiveFrom = default };

    /// <summary>A version's details, pricing the main branch, effective today unless said otherwise.</summary>
    public static PriceListVersionDetails VersionDetails(DateOnly? effectiveFrom = null)
        => new("Version", null, effectiveFrom ?? Today, false, RoundOffRule.NearestRupee, 10m, [MainBranch]);

    /// <summary>An empty price-list draft of one list; a different number gives a different identifier.</summary>
    public static PriceListVersion PriceListDraft(int number = 1, DateOnly? effectiveFrom = null)
        => PriceListVersion.CreateDraft(
                Id($"price-version-{number}"), Id("pl"), Organisation, number, VersionDetails(effectiveFrom), Now, null)
            .Value;

    /// <summary>A stitching service item on a synthetic tax code.</summary>
    public static PriceListItemDetails StitchingItem(string code = "BLOUSE_PATTERN_STITCHING")
        => new(code, "Blouse stitching, pattern work", PriceItemKind.Service, 450m, "each", "STITCHING_5", true);

    /// <summary>A percentage discount rule with an approval threshold below its maximum.</summary>
    public static DiscountRuleDetails FestivalRule(string code = "FESTIVAL")
        => new(code, "Festival-season discount", DiscountKind.Percentage, 5m, 15m, true);
}
