namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>How many options a group lets the customer choose (section 3 of the design options document).</summary>
public enum DesignSelectionMode
{
    /// <summary>Exactly one option, rendered as a radio-style card grid.</summary>
    SingleChoice = 0,

    /// <summary>Any number of options, rendered as checkable cards with a running count.</summary>
    MultipleChoice = 1,
}
