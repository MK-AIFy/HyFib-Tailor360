namespace Tailor360.Modules.Customers.Domain.Customers;

/// <summary>
/// A previous name, another spelling, or a merged customer number, kept searchable.
/// </summary>
/// <remarks>
/// Append-only. An alias is a record of something that was once true, so it is never edited and never
/// removed — and it therefore carries no concurrency token
/// (<c>docs/architecture/conventions.md</c> section on optimistic concurrency).
/// </remarks>
public sealed class CustomerAlias
{
    /// <summary>The longest alias the column holds.</summary>
    public const int MaximumValueLength = 200;

    private CustomerAlias()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CustomerAlias(
        Guid id,
        Guid customerId,
        CustomerAliasKind kind,
        string value,
        string normalisedValue,
        DateTimeOffset recordedAt,
        Guid? recordedBy)
    {
        Id = id;
        CustomerId = customerId;
        Kind = kind;
        Value = value;
        NormalisedValue = normalisedValue;
        RecordedAt = recordedAt;
        RecordedBy = recordedBy;
    }

    /// <summary>Identity of the alias.</summary>
    public Guid Id { get; private set; }

    /// <summary>The customer it belongs to.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Why it is held.</summary>
    public CustomerAliasKind Kind { get; private set; }

    /// <summary>The alias as it was written.</summary>
    public string Value { get; private set; } = string.Empty;

    /// <summary>
    /// The alias folded to its search key, so that a search finds it by the same rules that find a
    /// current name. A merged customer number is folded too, which costs nothing and keeps one index.
    /// </summary>
    public string NormalisedValue { get; private set; } = string.Empty;

    /// <summary>When it was recorded.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Who recorded it, where a person did.</summary>
    public Guid? RecordedBy { get; private set; }

    /// <summary>Records an alias. Called by the customer aggregate, never directly.</summary>
    /// <param name="id">Identity, from <c>IIdGenerator</c>.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="kind">Why it is held.</param>
    /// <param name="value">The alias as written.</param>
    /// <param name="normalisedValue">The alias folded to its search key.</param>
    /// <param name="recordedAt">The instant, from <c>IClock</c>.</param>
    /// <param name="recordedBy">The actor, where a person did it.</param>
    /// <returns>The alias.</returns>
    internal static CustomerAlias Record(
        Guid id,
        Guid customerId,
        CustomerAliasKind kind,
        string value,
        string normalisedValue,
        DateTimeOffset recordedAt,
        Guid? recordedBy)
        => new(id, customerId, kind, value, normalisedValue, recordedAt, recordedBy);
}
