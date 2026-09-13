using System.Globalization;

namespace Tailor360.Platform.Abstractions.Money;

/// <summary>
/// A monetary amount. Money is always <see cref="decimal"/>; binary floating point is never used for
/// money anywhere in this system (D11). Amounts are held to four decimal places so that tax and
/// discount arithmetic composes without premature rounding, and are rounded to the currency's minor
/// unit only when a document line or total is produced.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>The number of decimal places retained internally.</summary>
    public const int InternalScale = 4;

    /// <summary>The number of decimal places used when a document is produced.</summary>
    public const int DocumentScale = 2;

    /// <summary>The only currency the system handles today.</summary>
    public const string IndianRupee = "INR";

    /// <summary>Creates an amount in the given ISO 4217 currency.</summary>
    /// <exception cref="ArgumentException">The currency code is not three upper-case letters.</exception>
    public Money(decimal amount, string currency = IndianRupee)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("Currency must be a three-letter upper-case ISO 4217 code.", nameof(currency));
        }

        Amount = decimal.Round(amount, InternalScale, MidpointRounding.AwayFromZero);
        Currency = currency;
    }

    /// <summary>The amount, held to <see cref="InternalScale"/> decimal places.</summary>
    public decimal Amount { get; }

    /// <summary>The ISO 4217 currency code.</summary>
    public string Currency { get; }

    /// <summary>Zero rupees.</summary>
    public static Money Zero { get; } = new(0m);

    /// <summary>True when the amount is exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>True when the amount is below zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>Creates an amount in Indian rupees.</summary>
    public static Money Rupees(decimal amount) => new(amount);

    /// <summary>
    /// Rounds to the currency's minor unit half away from zero, the rule <c>docs/architecture/conventions.md</c>
    /// section 1.2 fixes for a line and each of its tax components, applied exactly once at the end of a
    /// line. Intermediate arithmetic is never rounded this way.
    /// </summary>
    public Money ToDocumentPrecision()
        => new(decimal.Round(Amount, DocumentScale, MidpointRounding.AwayFromZero), Currency);

    /// <summary>Adds two amounts of the same currency.</summary>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    /// <summary>Subtracts two amounts of the same currency.</summary>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    /// <summary>Negates an amount.</summary>
    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    /// <summary>Multiplies an amount by a quantity or rate.</summary>
    public static Money operator *(Money left, decimal factor) => new(left.Amount * factor, left.Currency);

    /// <summary>Multiplies a quantity or rate by an amount.</summary>
    public static Money operator *(decimal factor, Money right) => right * factor;

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>Adds two amounts of the same currency.</summary>
    public static Money Add(Money left, Money right) => left + right;

    /// <summary>Subtracts two amounts of the same currency.</summary>
    public static Money Subtract(Money left, Money right) => left - right;

    /// <summary>Multiplies an amount by a factor.</summary>
    public static Money Multiply(Money left, decimal factor) => left * factor;

    /// <summary>Negates an amount.</summary>
    public Money Negate() => -this;

    /// <inheritdoc />
    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Currency} {Amount.ToString("0.####", CultureInfo.InvariantCulture)}");

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot combine amounts in different currencies ({left.Currency} and {right.Currency}).");
        }
    }
}
