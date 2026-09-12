namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// One choice within a design option group, as one catalogue version holds it.
/// </summary>
/// <remarks>
/// The code is stored and referred to; the label, help text and alt text are what people read and may
/// be corrected on a published version. <see cref="Key"/> is the option as a concept across versions,
/// for the same reason <see cref="Catalogue.Category.Key"/> exists: it is what lets publication ask
/// whether a code changed since it was last published, when every draft's rows are new rows.
/// </remarks>
public sealed class DesignOption
{
    private DesignOption()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal DesignOption(
        Guid id,
        Guid key,
        Guid designOptionGroupId,
        Guid catalogVersionId,
        Guid organisationId,
        DesignOptionDetails details)
    {
        Id = id;
        Key = key;
        DesignOptionGroupId = designOptionGroupId;
        CatalogVersionId = catalogVersionId;
        OrganisationId = organisationId;

        Apply(details);
    }

    /// <summary>Identity of this row. New in every version, and what the API addresses.</summary>
    public Guid Id { get; private set; }

    /// <summary>Identity of the option as a concept, carried unchanged from version to version.</summary>
    public Guid Key { get; private set; }

    /// <summary>The group this option belongs to, in the same version.</summary>
    public Guid DesignOptionGroupId { get; private set; }

    /// <summary>The version this row belongs to.</summary>
    public Guid CatalogVersionId { get; private set; }

    /// <summary>The organisation the catalogue belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The machine key. Immutable once its version is published.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The label staff and customers read.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The Tamil label, where one is confirmed.</summary>
    public string? NameTamil { get; private set; }

    /// <summary>What the choice means for the finished garment.</summary>
    public string HelpText { get; private set; } = string.Empty;

    /// <summary>The bundled drawing, or null until one exists.</summary>
    public string? IllustrationKey { get; private set; }

    /// <summary>The shape in words.</summary>
    public string IllustrationAlt { get; private set; } = string.Empty;

    /// <summary>The price-list item the option resolves to, or null when it costs nothing extra.</summary>
    public string? PriceListItemCode { get; private set; }

    /// <summary>Signed working days added to the service's expected duration.</summary>
    public int TimeImpactDays { get; private set; }

    /// <summary>Where the option sits in its group.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Whether the option is offered on a new draft; false is retirement, never deletion.</summary>
    public bool Active { get; private set; }

    /// <summary>Whether this is the reserved "chose not to have it" option.</summary>
    public bool IsNone => string.Equals(Code, DesignCode.None, StringComparison.Ordinal);

    /// <summary>Replaces everything an administrator says about the option.</summary>
    /// <param name="details">The validated details.</param>
    internal void Apply(DesignOptionDetails details)
    {
        Code = details.Code;
        Name = details.Name;
        NameTamil = details.NameTamil;
        HelpText = details.HelpText;
        IllustrationKey = details.IllustrationKey;
        IllustrationAlt = details.IllustrationAlt;
        PriceListItemCode = details.PriceListItemCode;
        TimeImpactDays = details.TimeImpactDays;
        DisplayOrder = details.DisplayOrder;
        Active = details.Active;
    }

    /// <summary>Corrects the words of a published option.</summary>
    /// <param name="presentation">The validated correction.</param>
    internal void ApplyPresentation(DesignOptionPresentation presentation)
    {
        Name = presentation.Name;
        NameTamil = presentation.NameTamil;
        HelpText = presentation.HelpText;
        IllustrationAlt = presentation.IllustrationAlt;
        DisplayOrder = presentation.DisplayOrder;
    }

    /// <summary>The details as they stand, for a copy or an audit snapshot.</summary>
    public DesignOptionDetails Details => new(
        Code,
        Name,
        NameTamil,
        HelpText,
        IllustrationKey,
        IllustrationAlt,
        PriceListItemCode,
        TimeImpactDays,
        DisplayOrder,
        Active);

    /// <summary>Copies the option into a group of a new version.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="designOptionGroupId">The copy of this option's group in the new version.</param>
    /// <param name="catalogVersionId">The version being built.</param>
    /// <returns>The copy, carrying the same <see cref="Key"/>.</returns>
    internal DesignOption CopyInto(Guid id, Guid designOptionGroupId, Guid catalogVersionId)
        => new(id, Key, designOptionGroupId, catalogVersionId, OrganisationId, Details);
}
