using System.Security.Cryptography;
using System.Text;
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
}
