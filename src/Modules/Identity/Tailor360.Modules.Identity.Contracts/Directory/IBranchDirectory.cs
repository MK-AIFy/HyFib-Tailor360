namespace Tailor360.Modules.Identity.Contracts.Directory;

/// <summary>
/// What another module may know about a branch.
/// </summary>
/// <remarks>
/// <para>
/// Branches belong to Identity, and nothing outside it reads <c>identity.branches</c>
/// (<c>docs/architecture/module-ownership.md</c> section 5.1). Other modules nevertheless need three
/// facts about one: its <strong>code</strong>, because every display number in the product is
/// <c>&lt;prefix&gt;-&lt;branch&gt;-…</c> and the code is the middle of it; its <strong>timezone</strong>, because a
/// promised date is a date at a branch and not an instant; and whether it is <strong>open</strong>,
/// because a closed branch takes no new work.
/// </para>
/// <para>
/// Published now by issue #26, which is the first module outside Identity to need a branch code — a
/// customer number is <c>C-&lt;branch&gt;-000001</c> and is allocated at creation. It carries no address,
/// no contact and no GST registration: those are administration's to show and nobody else's to copy.
/// </para>
/// </remarks>
public interface IBranchDirectory
{
    /// <summary>One branch, or null when no branch has that identifier.</summary>
    /// <param name="branchId">The branch.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The branch, or null.</returns>
    Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Several branches in one read, for a screen that names more than one.
    /// </summary>
    /// <param name="branchIds">The branches. An empty collection reads nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The branches that exist, in no particular order.</returns>
    Task<IReadOnlyList<BranchSummary>> FindManyAsync(
        IReadOnlyCollection<Guid> branchIds,
        CancellationToken cancellationToken = default);
}

/// <summary>One branch as another module sees it.</summary>
/// <param name="BranchId">The branch. A UUIDv7, and the only identifier that crosses the boundary.</param>
/// <param name="Code">
/// The short code that appears in every display number allocated at this branch, for example
/// <c>CBE01</c>. Stable for the life of the branch, because a customer number that changed would
/// stop matching the card in somebody's purse.
/// </param>
/// <param name="Name">The branch's name, for a screen. Never part of an identifier.</param>
/// <param name="TimeZoneId">
/// The IANA zone the branch works in. A promised date is a date here, not an instant.
/// </param>
/// <param name="IsOpen">
/// False once the branch has been closed. A closed branch keeps its records and takes no new work,
/// which is a different thing from not existing.
/// </param>
public sealed record BranchSummary(
    Guid BranchId,
    string Code,
    string Name,
    string TimeZoneId,
    bool IsOpen);
