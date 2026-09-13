using Tailor360.Platform.Abstractions.Identifiers;

namespace Tailor360.Modules.Billing.Domain.Tax;

/// <summary>
/// One tax code of one tax configuration version: a classification and the components it carries.
/// </summary>
/// <remarks>
/// A row belongs to exactly one version. The <see cref="Key"/> is the concept across versions — the
/// same key in version 3 and version 4 is the same code, cloned — which is what lets a publish check
/// say that a code changed its spelling after invoices already carried it.
/// </remarks>
public sealed class TaxCode
{
    private readonly List<TaxComponent> _components = [];

    private TaxCode()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal TaxCode(Guid id, Guid key, Guid versionId, Guid organisationId, TaxCodeDetails details)
    {
        Id = id;
        Key = key;
        TaxConfigurationVersionId = versionId;
        OrganisationId = organisationId;

        Apply(details);
    }

    /// <summary>This row.</summary>
    public Guid Id { get; private set; }

    /// <summary>The concept, carried across versions by cloning.</summary>
    public Guid Key { get; private set; }

    /// <summary>The version this row belongs to.</summary>
    public Guid TaxConfigurationVersionId { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The stable business key.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What the code covers.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The HSN or SAC classification.</summary>
    public string Classification { get; private set; } = string.Empty;

    /// <summary>Goods or services.</summary>
    public TaxCodeKind Kind { get; private set; }

    /// <summary>Whether the code may be given to a price-list item.</summary>
    public bool Active { get; private set; }

    /// <summary>The stored components.</summary>
    public IReadOnlyCollection<TaxComponent> Components => _components;

    /// <summary>The components as values, in component order.</summary>
    public IReadOnlyList<TaxRate> Rates
        => [.. _components.OrderBy(component => component.Kind).Select(component => component.ToRate())];

    /// <summary>The details as an administrator would re-enter them.</summary>
    public TaxCodeDetails Details => new(Code, Description, Classification, Kind, Active, Rates);

    /// <summary>The rate of one component, or zero when the code does not carry it.</summary>
    /// <param name="kind">The component.</param>
    /// <returns>The rate as a percentage.</returns>
    public decimal RateOf(TaxComponentKind kind)
        => _components.FirstOrDefault(component => component.Kind == kind)?.RatePercent ?? 0m;

    internal void Apply(TaxCodeDetails details)
    {
        Code = details.Code;
        Description = details.Description.Trim();
        Classification = details.Classification;
        Kind = details.Kind;
        Active = details.Active;

        _components.Clear();
        foreach (var rate in details.Rates.OrderBy(rate => rate.Kind))
        {
            _components.Add(TaxComponent.For(Id, rate));
        }
    }

    internal TaxCode CopyInto(IIdGenerator ids, Guid versionId)
        => new(ids.NewId(), Key, versionId, OrganisationId, Details);
}
