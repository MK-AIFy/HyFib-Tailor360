namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// What a garment holds in one design option group, as a selection draft records it.
/// </summary>
/// <remarks>
/// Codes rather than row identities, for the same reason a rule's operands are codes
/// (<see cref="DesignRuleOperand"/>): a draft is pinned to one catalogue version for its life, and a
/// migration to a later version rewrites these codes to whatever the group and its options are called
/// there — a group or an option may be renamed in a draft that has never been published, and the codes
/// are what <see cref="DesignSelectionMigration"/> and <see cref="Catalogue.IDesignSelectionValidator"/>
/// both read.
/// </remarks>
public sealed class DesignDraftSelection
{
    private DesignDraftSelection()
    {
        // The persistence layer materialises instances through this constructor.
        GroupCode = string.Empty;
    }

    private DesignDraftSelection(string groupCode, IReadOnlyList<string> optionCodes)
    {
        GroupCode = groupCode;
        OptionCodes = [.. optionCodes];
    }

    /// <summary>The group this answers, as its code stood in the version this selection was made in.</summary>
    public string GroupCode { get; private set; }

    /// <summary>
    /// The option codes chosen — one for a single-choice group, any number otherwise. Never empty: an
    /// entry with nothing left in it is dropped rather than kept, so "the group is unset" and "the group
    /// is set to nothing" are never two different rows.
    /// </summary>
    /// <remarks>
    /// A plain array, the same shape <see cref="DesignRule.AntecedentOptionCodes"/> uses, so it maps to a
    /// PostgreSQL <c>text[]</c> the same way — EF Core's primitive-collection support, not a converter
    /// this project has to maintain.
    /// </remarks>
    public string[] OptionCodes { get; private set; } = [];

    /// <summary>Records what was chosen in one group.</summary>
    /// <param name="groupCode">The group.</param>
    /// <param name="optionCodes">The options chosen.</param>
    /// <returns>The selection.</returns>
    public static DesignDraftSelection Of(string groupCode, IReadOnlyList<string> optionCodes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupCode);
        ArgumentNullException.ThrowIfNull(optionCodes);

        return new DesignDraftSelection(groupCode, optionCodes);
    }
}
