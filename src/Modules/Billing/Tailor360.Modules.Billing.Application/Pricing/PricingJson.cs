using System.Text.Json;
using System.Text.Json.Serialization;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.Modules.Billing.Application.Pricing;

/// <summary>
/// How a request and a result are written into a calculation snapshot and read back. One set of
/// options, fixed here, because a snapshot written today must read identically in ten years: enums as
/// their names, property names as declared, an amount as a bare number in rupees, nothing indented.
/// </summary>
public static class PricingJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(), new MoneyConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>A request as JSON.</summary>
    public static string Write(PricingRequest request) => JsonSerializer.Serialize(request, Options);

    /// <summary>A result as JSON.</summary>
    public static string Write(PricingResult result) => JsonSerializer.Serialize(result, Options);

    /// <summary>A request read back.</summary>
    public static PricingRequest ReadRequest(string json) => JsonSerializer.Deserialize<PricingRequest>(json, Options)!;

    /// <summary>A result read back.</summary>
    public static PricingResult ReadResult(string json) => JsonSerializer.Deserialize<PricingResult>(json, Options)!;

    /// <summary>
    /// An amount is written as its number. Every amount a calculation carries is rupees
    /// (<c>docs/architecture/conventions.md</c> section 1.1), and <see cref="Money"/> is a value the
    /// serialiser cannot rebuild through its constructor on its own.
    /// </summary>
    private sealed class MoneyConverter : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Money.Rupees(reader.GetDecimal());

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
        {
            if (!string.Equals(value.Currency, Money.IndianRupee, StringComparison.Ordinal))
            {
                throw new JsonException($"A calculation snapshot holds rupees; {value.Currency} cannot be written.");
            }

            writer.WriteNumberValue(value.Amount);
        }
    }
}
