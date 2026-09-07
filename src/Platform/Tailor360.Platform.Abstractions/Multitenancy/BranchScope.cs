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

    /// <summary>
    /// The operation is not about a branch at all, so no branch reach is demanded of the caller — only
    /// the permission the endpoint declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not a weaker <see cref="Organisation"/>. The distinction is whether branch-owned data is
    /// touched: an organisation-scoped read of orders reaches rows every branch owns, and demanding
    /// organisation-wide reach for it is exactly right. A feature flag, a module toggle or a system
    /// setting owns no branch and belongs to none, so there is nothing for a branch check to compare
    /// against — and demanding one anyway does not make the endpoint safer, it makes it unreachable by
    /// principals who hold the permission and are assigned to no branch.
    /// </para>
    /// <para>
    /// That was not hypothetical. The vendor super-user role is seeded holding <c>admin.feature_flags</c>
    /// and nothing else, deliberately, so that the one principal approved to change a flag is not also
    /// able to read every branch's data. Declaring <see cref="Organisation"/> on the flag endpoint made
    /// that role unable to reach the only endpoint it exists for, and the alternative — granting it
    /// organisation-wide read as well — would hand a vendor principal reach over every branch in order
    /// to let it flip a boolean.
    /// </para>
    /// <para>
    /// Use it only where the claim is true. An endpoint that declares this and then reads a
    /// branch-owned row has removed its own scope check, and no test can tell that from an endpoint
    /// where the claim holds.
    /// </para>
    /// </remarks>
    NotBranchOwned,
}
