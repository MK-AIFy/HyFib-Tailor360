using System.Security.Cryptography;

namespace Tailor360.Platform.Abstractions.Barcodes;

/// <summary>
/// An opaque barcode payload in the shape <c>docs/architecture/conventions.md</c> section 3.3 fixes: a
/// namespace letter, a hyphen and twelve Crockford base32 characters — eleven random (55 bits) and one check
/// character. The one generator and validator every namespace uses (<c>G-</c> garment job, <c>S-</c> stock
/// item, <c>I-</c> invoice, <c>R-</c> receipt), so that a scanner's fallback rules and a printer's label are
/// judged by the same code everywhere.
/// </summary>
/// <remarks>
/// <para>
/// The check character is a Damm-style checksum over the 32-symbol alphabet. Damm's construction needs a
/// totally anti-symmetric quasigroup of the alphabet's order; over the field GF(32) the operation
/// <c>interim ∘ symbol = α·interim + symbol</c>, with <c>α</c> any field element other than 0 and 1, is one
/// (<c>(c∘x)∘y = (c∘y)∘x</c> implies <c>x = y</c> because <c>(α − 1)(x − y) = 0</c>, and the same
/// argument gives <c>x∘y = y∘x ⇒ x = y</c>). The interim starts at zero, every body symbol folds in, and
/// the check character is what folds the interim back to zero — in characteristic two that is
/// <c>α·interim</c> itself. It detects every single-symbol error and every adjacent transposition, which
/// is what a keyboard-wedge scanner and a tired operator produce.
/// </para>
/// <para>
/// The payload carries no display number and no personal data (<c>INV-BID-04</c>): 55 bits from the
/// operating system's random generator and nothing else. A namespace says only which resolver to ask.
/// </para>
/// </remarks>
public readonly record struct BarcodePayload
{
    /// <summary>The Crockford base32 alphabet: digits and the letters without <c>I</c>, <c>L</c>, <c>O</c> and <c>U</c>.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>The namespace of a garment job.</summary>
    public const char GarmentJobNamespace = 'G';

    /// <summary>The namespace of a stock item.</summary>
    public const char StockItemNamespace = 'S';

    /// <summary>The namespace of an invoice.</summary>
    public const char InvoiceNamespace = 'I';

    /// <summary>The namespace of a receipt.</summary>
    public const char ReceiptNamespace = 'R';

    /// <summary>The random characters of the body.</summary>
    public const int BodyLength = 11;

    /// <summary>The whole payload: namespace, hyphen, body, check character.</summary>
    public const int Length = BodyLength + 3;

    /// <summary>The bits of entropy the body carries: eleven symbols of five bits.</summary>
    public const int EntropyBits = BodyLength * 5;

    /// <summary>The field's reduction polynomial, x⁵ + x² + 1, which is primitive over GF(2).</summary>
    private const int ReductionPolynomial = 0b100101;

    private static readonly int[] SymbolValues = BuildSymbolValues();

    private BarcodePayload(char ns, string value)
    {
        Namespace = ns;
        Value = value;
    }

    /// <summary>The namespace letter.</summary>
    public char Namespace { get; }

    /// <summary>The payload as printed and as scanned, in canonical upper case.</summary>
    public string Value { get; }

    /// <summary>Mints a fresh payload in a namespace from the operating system's random generator.</summary>
    /// <param name="ns">The namespace letter: one upper-case letter, which the alphabet's exclusions do not bind (<c>I-</c> is an invoice).</param>
    /// <returns>The payload.</returns>
    public static BarcodePayload Mint(char ns)
    {
        CheckNamespace(ns);

        // Seven random bytes are 56 bits; the body takes eleven five-bit symbols from them and drops the last.
        Span<byte> random = stackalloc byte[7];
        RandomNumberGenerator.Fill(random);

        Span<char> body = stackalloc char[BodyLength];
        var bits = 0;
        var buffer = 0;
        var written = 0;
        foreach (var value in random)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5 && written < BodyLength)
            {
                body[written++] = Alphabet[(buffer >> (bits - 5)) & 0b11111];
                bits -= 5;
            }
        }

        return new BarcodePayload(ns, string.Create(Length, (ns, body: body.ToString()), static (span, state) =>
        {
            span[0] = state.ns;
            span[1] = '-';
            state.body.AsSpan().CopyTo(span[2..]);
            span[Length - 1] = CheckCharacterOf(state.body);
        }));
    }

    /// <summary>
    /// Reads a scanned or typed payload: lower case is folded, the confusable <c>I</c>, <c>L</c> and <c>O</c>
    /// are taken as <c>1</c> and <c>0</c>, and the check character must hold.
    /// </summary>
    /// <param name="scanned">What the scanner delivered.</param>
    /// <param name="payload">The canonical payload, when it reads.</param>
    /// <returns>True when the text is a payload whose checksum holds.</returns>
    public static bool TryParse(string? scanned, out BarcodePayload payload)
    {
        payload = default;
        if (scanned is null)
        {
            return false;
        }

        var trimmed = scanned.Trim();
        if (trimmed.Length != Length || trimmed[1] != '-' || !char.IsAsciiLetter(trimmed[0]))
        {
            return false;
        }

        // The namespace is a letter outside the alphabet's rules — an invoice is I- — so only the body and the
        // check character are folded through the substitutions.
        var text = string.Concat(char.ToUpperInvariant(trimmed[0]).ToString(), "-", Normalise(trimmed[2..]));

        var body = text.AsSpan(2, BodyLength);
        foreach (var symbol in body)
        {
            if (!IsInAlphabet(symbol))
            {
                return false;
            }
        }

        if (!IsInAlphabet(text[Length - 1]) || CheckCharacterOf(body) != text[Length - 1])
        {
            return false;
        }

        payload = new BarcodePayload(text[0], text);
        return true;
    }

    /// <summary>True when the text is a payload of the namespace whose checksum holds.</summary>
    /// <param name="scanned">What the scanner delivered.</param>
    /// <param name="ns">The namespace expected.</param>
    /// <param name="payload">The canonical payload, when it reads.</param>
    public static bool TryParse(string? scanned, char ns, out BarcodePayload payload)
        => TryParse(scanned, out payload) && payload.Namespace == ns;

    /// <summary>The check character that folds the body's interim back to zero.</summary>
    /// <param name="body">The eleven body symbols, canonical.</param>
    public static char CheckCharacterOf(ReadOnlySpan<char> body)
    {
        var interim = 0;
        foreach (var symbol in body)
        {
            interim = Multiply(interim) ^ SymbolValues[symbol];
        }

        // Folding the check character c in gives α·interim + c, which is zero exactly when c = α·interim.
        return Alphabet[Multiply(interim)];
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static string Normalise(string text)
        => string.Create(text.Length, text, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var folded = char.ToUpperInvariant(source[index]);
                span[index] = folded switch
                {
                    'I' or 'L' => '1',
                    'O' => '0',
                    _ => folded,
                };
            }
        });

    private static bool IsInAlphabet(char symbol) => symbol < 128 && SymbolValues[symbol] >= 0;

    private static void CheckNamespace(char ns)
    {
        if (!char.IsAsciiLetterUpper(ns))
        {
            throw new ArgumentOutOfRangeException(nameof(ns), ns, "A namespace is one upper-case letter.");
        }
    }

    /// <summary>Multiplies a field element by α in GF(32): a shift, reduced by the polynomial when it overflows.</summary>
    private static int Multiply(int value)
    {
        var shifted = value << 1;
        return (shifted & 0b100000) != 0 ? (shifted ^ ReductionPolynomial) & 0b11111 : shifted;
    }

    private static int[] BuildSymbolValues()
    {
        var values = new int[128];
        Array.Fill(values, -1);
        for (var index = 0; index < Alphabet.Length; index++)
        {
            values[Alphabet[index]] = index;
        }

        return values;
    }
}
