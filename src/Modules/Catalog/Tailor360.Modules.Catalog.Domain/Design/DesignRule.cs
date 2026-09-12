namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// One rule between the options of a category — requires, excludes, requires attachment or a note.
/// </summary>
/// <remarks>
/// <para>
/// A rule carries a stable identifier, <c>DR-nn</c>, unique across the whole catalogue and never
/// re-used (section 4 rule 7 of the design options document): two rules can both concern
/// <c>padding = MOULDED_CUP</c>, so a finding or a test that named only the option would be
/// ambiguous between them. The number is allocated by the catalogue when the rule is added and
/// travels with the rule into every later version, as <see cref="Key"/> does.
/// </para>
/// <para>
/// The two operands are stored flattened — group code, form and option codes for each side — rather
/// than as a serialised expression, so a change to an option code in a draft can be checked against
/// them with a query, and so the printed form of a rule is always derivable from what is stored.
/// </para>
/// </remarks>
public sealed class DesignRule
{
    private DesignRule()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal DesignRule(
        Guid id,
        Guid key,
        Guid catalogVersionId,
        Guid organisationId,
        Guid categoryId,
        int number,
        DesignRuleDetails details)
    {
        Id = id;
        Key = key;
        CatalogVersionId = catalogVersionId;
        OrganisationId = organisationId;
        CategoryId = categoryId;
        Number = number;

        Apply(details);
    }

    /// <summary>Identity of this row. New in every version, and what the API addresses.</summary>
    public Guid Id { get; private set; }

    /// <summary>Identity of the rule as a concept, carried unchanged from version to version.</summary>
    public Guid Key { get; private set; }

    /// <summary>The version this row belongs to.</summary>
    public Guid CatalogVersionId { get; private set; }

    /// <summary>The organisation the catalogue belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The category whose options the rule reads. A rule never crosses categories.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>The number in <c>DR-nn</c>: unique across the catalogue, allocated once, never re-used.</summary>
    public int Number { get; private set; }

    /// <summary>The identifier as the document writes it.</summary>
    public string Identifier => $"DR-{Number:00}";

    /// <summary>Which of the four types it is.</summary>
    public DesignRuleType Type { get; private set; }

    /// <summary>The antecedent's group, or null for <c>always</c>.</summary>
    public string? AntecedentGroupCode { get; private set; }

    /// <summary>The antecedent's form.</summary>
    public DesignOperandForm AntecedentForm { get; private set; }

    /// <summary>The antecedent's option codes.</summary>
    public string[] AntecedentOptionCodes { get; private set; } = [];

    /// <summary>The consequent's group, or null when the type has no option consequent.</summary>
    public string? ConsequentGroupCode { get; private set; }

    /// <summary>The consequent's form, or null when the type has no option consequent.</summary>
    public DesignOperandForm? ConsequentForm { get; private set; }

    /// <summary>The consequent's option codes.</summary>
    public string[] ConsequentOptionCodes { get; private set; } = [];

    /// <summary>The standing instruction a note attaches, or the explanation on another type.</summary>
    public string? Note { get; private set; }

    /// <summary>Why the rule exists.</summary>
    public string? Why { get; private set; }

    /// <summary>What has to hold for the rule to fire.</summary>
    public DesignRuleOperand Antecedent
        => new(AntecedentGroupCode, AntecedentForm, AntecedentOptionCodes);

    /// <summary>The option set a requires or excludes rule names, or null.</summary>
    public DesignRuleOperand? Consequent
        => ConsequentForm is { } form
            ? new DesignRuleOperand(ConsequentGroupCode, form, ConsequentOptionCodes)
            : null;

    /// <summary>The details as they stand, for a copy or an audit snapshot.</summary>
    public DesignRuleDetails Details => new(Type, Antecedent, Consequent, Note, Why);

    /// <summary>Whether a violation of this rule stops a confirmation. A note never does.</summary>
    public bool Blocks => Type != DesignRuleType.Note;

    /// <summary>The rule as the document writes it.</summary>
    /// <returns>For example <c>DR-01 requires: padding in (LIGHT, MOULDED_CUP) requires lining ≠ NONE</c>.</returns>
    public string Statement
        => Type switch
        {
            DesignRuleType.Requires => $"{Antecedent} requires {Consequent}",
            DesignRuleType.Excludes => $"{Antecedent} excludes {Consequent}",
            DesignRuleType.RequiresAttachment => $"{Antecedent} requires a reference image on the garment",
            _ => $"{Antecedent} → \"{Note}\"",
        };

    /// <summary>Replaces everything an administrator says about the rule.</summary>
    /// <param name="details">The validated details.</param>
    internal void Apply(DesignRuleDetails details)
    {
        Type = details.Type;
        AntecedentGroupCode = details.Antecedent.GroupCode;
        AntecedentForm = details.Antecedent.Form;
        AntecedentOptionCodes = [.. details.Antecedent.OptionCodes];
        ConsequentGroupCode = details.Consequent?.GroupCode;
        ConsequentForm = details.Consequent?.Form;
        ConsequentOptionCodes = details.Consequent is { } consequent ? [.. consequent.OptionCodes] : [];
        Note = details.Note;
        Why = details.Why;
    }

    /// <summary>Copies the rule into a new version.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="catalogVersionId">The version being built.</param>
    /// <param name="categoryId">The copy of this rule's category in the new version.</param>
    /// <returns>The copy, carrying the same <see cref="Key"/> and <see cref="Number"/>.</returns>
    internal DesignRule CopyInto(Guid id, Guid catalogVersionId, Guid categoryId)
        => new(id, Key, catalogVersionId, OrganisationId, categoryId, Number, Details);
}
