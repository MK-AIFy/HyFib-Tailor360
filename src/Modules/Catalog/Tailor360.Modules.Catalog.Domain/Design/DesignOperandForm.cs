namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// The operand forms of section 4 of the design options document — the only ones a rule may use.
/// </summary>
/// <remarks>
/// Every form but <see cref="Always"/> is a condition over <em>one group</em> of the rule's category,
/// and a condition over an <em>unset</em> group is false, negations included (section 8). The
/// reference-image predicate of a <c>requires attachment</c> rule is not an operand form: it is the
/// rule's type, because it reads the garment rather than the catalogue.
/// </remarks>
public enum DesignOperandForm
{
    /// <summary><c>group = OPTION</c> — the single-selection group holds exactly that option.</summary>
    Equals = 0,

    /// <summary><c>group ≠ OPTION</c> — the group holds a value and it is not the named option.</summary>
    NotEquals = 1,

    /// <summary><c>group in (A, B)</c> — the group's value is one of the listed options.</summary>
    In = 2,

    /// <summary><c>group includes OPTION</c> — the multiple-selection group holds that option among its selections.</summary>
    Includes = 3,

    /// <summary><c>group excludes OPTION</c> — the multiple-selection group holds a value and that option is not among it.</summary>
    Excludes = 4,

    /// <summary><c>any selection in group</c> — the group holds at least one option, whichever it is.</summary>
    AnySelection = 5,

    /// <summary><c>always</c> — unconditionally, for every garment of the category.</summary>
    Always = 6,
}
