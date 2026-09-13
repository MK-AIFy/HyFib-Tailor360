namespace Tailor360.Modules.Catalog.Contracts.Catalogue;

/// <summary>
/// The design copy a confirmed garment carries forward, published for #32a to pin at order confirmation
/// (#30, issue #140).
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/prd/design-options.md</c> section 7: a value object embedding, per selection, the group code
/// and label, the option code and label, the illustration reference and its alternative text, both
/// display orders, the option's price-list item code and the catalogue version it was drawn from — so a
/// job card renders without a catalogue lookup, for ever. Republishing the catalogue, retiring a group or
/// renaming an option never changes a snapshot already taken: it is a copy, not a reference.
/// </para>
/// <para>
/// <strong>This is Catalog's read-side shape, and it is not Orders' own <c>DesignSnapshot</c>.</strong>
/// Orders (#32a) owns an independent, persisted value object under
/// <c>Tailor360.Modules.Orders.Domain.Snapshots</c> with its own invariants and its own illustration
/// reference — a Media identifier rather than the bundled-drawing key carried here, because by
/// confirmation time an option's illustration may have been promoted to an uploaded image (#31) and
/// Orders is the module that knows which. Building that value from this one, and deciding what happens
/// to <see cref="Instructions"/> or a snapshot with no illustration yet, is #32a's mapping to write; this
/// type only has to carry everything section 7 lists, which it does.
/// </para>
/// </remarks>
/// <param name="CatalogVersionId">Provenance: the published version the selections were validated against.</param>
/// <param name="CatalogVersionNumber">The version's number — printed on the card as "the option version".</param>
/// <param name="CategoryCode">The garment's category, as the catalogue names it.</param>
/// <param name="CategoryLabel">The category's label as it stood at the time this was built.</param>
/// <param name="ServiceTypeCode">The service type chosen, as the catalogue names it.</param>
/// <param name="ServiceTypeLabel">The service type's label as it stood at the time this was built.</param>
/// <param name="Selections">The choices, in the order the card renders them.</param>
/// <param name="ConditionalNotes">The standing instructions the rules attached, in rule order.</param>
/// <param name="Instructions">What Reception typed that is not any option, or null. Never priced.</param>
public sealed record GarmentDesignSnapshot(
    Guid CatalogVersionId,
    int CatalogVersionNumber,
    string CategoryCode,
    string CategoryLabel,
    string ServiceTypeCode,
    string ServiceTypeLabel,
    IReadOnlyList<GarmentDesignSelectionSnapshot> Selections,
    IReadOnlyList<string> ConditionalNotes,
    string? Instructions);

/// <summary>One design choice inside a <see cref="GarmentDesignSnapshot"/>.</summary>
/// <param name="GroupCode">The design option group's code.</param>
/// <param name="GroupLabel">The group's label as it stood when the snapshot was built.</param>
/// <param name="GroupDisplayOrder">Where the group sits on the card.</param>
/// <param name="OptionCode">The chosen option's code.</param>
/// <param name="OptionLabel">The option's label as it stood when the snapshot was built.</param>
/// <param name="OptionDisplayOrder">Where the option sits within its group.</param>
/// <param name="IllustrationKey">
/// The bundled line drawing's reference, <c>sheet_key#group_code.OPTION_CODE</c>, or null until one
/// exists (<c>docs/prd/design-options.md</c> section 6).
/// </param>
/// <param name="IllustrationAlt">The shape in words. Present on every option, illustrated or not.</param>
/// <param name="PriceListItemCode">The price-list item the option's impact resolved to, or null.</param>
/// <param name="OptionVersion">The catalogue version the option was drawn from.</param>
public sealed record GarmentDesignSelectionSnapshot(
    string GroupCode,
    string GroupLabel,
    int GroupDisplayOrder,
    string OptionCode,
    string OptionLabel,
    int OptionDisplayOrder,
    string? IllustrationKey,
    string IllustrationAlt,
    string? PriceListItemCode,
    int OptionVersion);
