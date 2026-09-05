namespace Tailor360.Platform.Abstractions.Multitenancy;

/// <summary>
/// How widely a piece of data or an operation reaches. The system is a single organisation with
/// branch scoping (ADR-0007); every branch-owned row carries a branch identifier and every query on
/// such a row is filtered by the caller's assigned branches.
/// </summary>
public enum BranchScope
{
    /// <summary>Visible to the caller's currently selected branch only.</summary>
    CurrentBranch,

    /// <summary>Visible across every branch the caller is assigned to.</summary>
    AssignedBranches,

    /// <summary>Visible across the whole organisation; requires an organisation-wide permission.</summary>
    Organisation,
}
