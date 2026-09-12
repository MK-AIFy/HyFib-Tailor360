using Shouldly;
using Tailor360.Platform.Abstractions.Barcodes;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The shared barcode payload (<c>docs/architecture/conventions.md</c> section 3.3): the shape, the alphabet,
/// the check character, the confusable substitutions a scanner's fallback applies, and what the checksum
/// catches.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BarcodePayloadTests
{
    [Fact]
    public void MintsANamespacedPayloadOfTwelveAlphabetSymbolsThatRoundTrips()
    {
        var payload = BarcodePayload.Mint(BarcodePayload.InvoiceNamespace);

        payload.Value.Length.ShouldBe(BarcodePayload.Length);
        payload.Value[0].ShouldBe('I');
        payload.Value[1].ShouldBe('-');
        payload.Value[2..].ShouldAllBe(symbol => BarcodePayload.Alphabet.Contains(symbol));
        payload.Namespace.ShouldBe('I');

        BarcodePayload.TryParse(payload.Value, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(payload);
        BarcodePayload.TryParse(payload.Value, BarcodePayload.InvoiceNamespace, out _).ShouldBeTrue();
        BarcodePayload.TryParse(payload.Value, BarcodePayload.GarmentJobNamespace, out _).ShouldBeFalse();
    }

    [Fact]
    public void TwoMintsDiffer()
    {
        var payloads = Enumerable.Range(0, 100).Select(_ => BarcodePayload.Mint(BarcodePayload.ReceiptNamespace).Value).ToHashSet(StringComparer.Ordinal);

        payloads.Count.ShouldBe(100);
    }

    [Fact]
    public void ReadsLowerCaseAndTheConfusableSubstitutions()
    {
        var payload = BarcodePayload.Mint(BarcodePayload.GarmentJobNamespace).Value;
        var scanned = payload.ToLowerInvariant().Replace('1', 'l').Replace('0', 'o');

        BarcodePayload.TryParse(scanned, out var parsed).ShouldBeTrue();
        parsed.Value.ShouldBe(payload);

        // I and L both read as 1, O as 0 in the body — the substitutions the convention names, and only
        // those; the namespace letter is not folded, which is what lets an invoice be I-.
        const string body = "7K3M9QW2XZ4";
        var check = BarcodePayload.CheckCharacterOf(body);
        var canonical = $"I-{body}{check}";
        BarcodePayload.TryParse(canonical, out var invoice).ShouldBeTrue();
        invoice.Namespace.ShouldBe('I');
        BarcodePayload.TryParse(canonical.ToLowerInvariant(), out var lower).ShouldBeTrue();
        lower.Value.ShouldBe(canonical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("G-7K3M9QW2XZ4")]
    [InlineData("G7K3M9QW2XZ4BB")]
    [InlineData("7-7K3M9QW2XZ4B")]
    [InlineData("G-7K3M9QW2XU4B")]
    public void RefusesTheWrongShape(string scanned)
        => BarcodePayload.TryParse(scanned, out _).ShouldBeFalse();

    [Fact]
    public void TheCheckCharacterCatchesEverySingleSymbolErrorAndEveryAdjacentTransposition()
    {
        var payload = BarcodePayload.Mint(BarcodePayload.StockItemNamespace).Value;

        for (var position = 2; position < BarcodePayload.Length; position++)
        {
            foreach (var replacement in BarcodePayload.Alphabet)
            {
                if (replacement == payload[position])
                {
                    continue;
                }

                var damaged = payload.ToCharArray();
                damaged[position] = replacement;
                BarcodePayload.TryParse(new string(damaged), out _).ShouldBeFalse($"a single error at {position} must be caught");
            }
        }

        for (var position = 2; position < BarcodePayload.Length - 1; position++)
        {
            if (payload[position] == payload[position + 1])
            {
                continue;
            }

            var swapped = payload.ToCharArray();
            (swapped[position], swapped[position + 1]) = (swapped[position + 1], swapped[position]);
            BarcodePayload.TryParse(new string(swapped), out _).ShouldBeFalse($"a transposition at {position} must be caught");
        }
    }

    [Fact]
    public void ANamespaceIsOneUpperCaseLetter()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BarcodePayload.Mint('7'));
        Should.Throw<ArgumentOutOfRangeException>(() => BarcodePayload.Mint('g'));
        Should.Throw<ArgumentOutOfRangeException>(() => BarcodePayload.Mint('-'));
    }
}
