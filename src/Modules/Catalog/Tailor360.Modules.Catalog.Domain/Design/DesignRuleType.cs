namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>The four rule types of section 4 of the design options document.</summary>
public enum DesignRuleType
{
    /// <summary>Selecting the antecedent obliges one of the consequent options to be selected. Blocking.</summary>
    Requires = 0,

    /// <summary>The antecedent and the consequent may not both be selected. Blocking.</summary>
    Excludes = 1,

    /// <summary>Selecting the antecedent obliges a reference image on the garment. Blocking.</summary>
    RequiresAttachment = 2,

    /// <summary>Selecting the antecedent attaches a standing instruction to the job. Never blocking.</summary>
    Note = 3,
}
