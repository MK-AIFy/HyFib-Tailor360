namespace Tailor360.Platform.Abstractions.Multitenancy;

/// <summary>
/// How widely a piece of data or an operation reaches. The system is a single organisation with branch
/// scoping (ADR-0007), and every branch-owned row carries a branch identifier.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is enforced today, and what is not.</b> An endpoint that names one row declares
/// <c>ScopedToResource</c>, and the row's branch is loaded and checked against the value below before
/// the handler runs. That is the whole of the mechanism: an endpoint that returns a <em>collection</em>
/// — a list, a search, an export — has no equivalent yet, because there is no scoped-query port and no
/// global filter, so nothing narrows such a query to the caller's branches. The first issue to publish
/// one (#32a) builds that half; until then the property is an intention for collections and a
/// guarantee only for the single-row case. It is stated here rather than left to be discovered because
/// an earlier wording of this remark claimed the guarantee outright.
/// </para>
/// </remarks>
public enum BranchScope
{
    /// <summary>
    /// Visible to the branch the caller's session is currently working in, and to that branch only.
    /// Not "any branch the caller could switch to": somebody assigned to two branches reaches the
    /// second one through this scope only after switching, which is what makes the switch an act with
    /// a trail rather than a label on a screen.
    /// </summary>
    CurrentBranch,

    /// <summary>Visible across every branch the caller is assigned to, whichever one they are working in.</summary>
    AssignedBranches,

    /// <summary>Visible across the whole organisation; requires an organisation-wide permission.</summary>
    Organisation,
}
