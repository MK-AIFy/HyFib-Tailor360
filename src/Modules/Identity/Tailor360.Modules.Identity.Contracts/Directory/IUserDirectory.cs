namespace Tailor360.Modules.Identity.Contracts.Directory;

/// <summary>
/// Who the staff are, for the modules that have to name them.
/// </summary>
/// <remarks>
/// <para>
/// Identity's first published read contract, and the only sanctioned way another module answers "who
/// is this person" — Orders validating that a job can be assigned to somebody (#33), Reporting naming
/// the recipients of a scheduled report (#44), and the workload views that show a branch's day (#45).
/// None of them may read <c>identity.users</c>; a read across a boundary is a contract call
/// (module-ownership MO-2, ARCH-004, ARCH-011).
/// </para>
/// <para>
/// <b>A directory, not a profile.</b> There is no address, no phone number, no sign-in name, no status
/// history and no second-factor state here. A caller deciding whether to assign a job needs a name, a
/// branch and whether the account still works; anything more would put contact details into modules
/// that have no reason to hold them and no screen that shows them. The display name is personal data
/// and is the one piece that has to be here — it is what a workload board and a report say instead of
/// a UUID — so it is subject to the usual rule: it belongs on a screen and in the audit trail, and
/// never in a log line, a metric or a trace attribute.
/// </para>
/// <para>
/// <b>Answers are current, never cached by the caller.</b> Every method reads the database at the
/// moment it is called, which is what makes a suspension take effect on the next assignment rather
/// than on the next deployment. A consumer that holds a result across a request is choosing to act on
/// what was true earlier.
/// </para>
/// <para>
/// <b>No skills attribute yet.</b> The plan lists a tailor-skills attribute on this contract, and it is
/// deliberately not here: which skills exist, whether they are a seeded vocabulary or free text, and
/// whether they gate assignment or merely rank it are product decisions with no source, and the issue
/// that first reads them is #45. Inventing a vocabulary now would publish a guess in a contract three
/// modules depend on. It arrives with its consumer.
/// </para>
/// </remarks>
public interface IUserDirectory
{
    /// <summary>One member of staff, or null when no such account exists.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StaffMember?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Several members of staff at once, in display-name order, skipping identifiers that match nobody.
    /// </summary>
    /// <remarks>
    /// Here because the alternative is what callers will otherwise write: a loop of
    /// <see cref="FindAsync"/> over the assignees of a day's jobs, which is one query per row of a
    /// screen. Identifiers that match nobody are skipped rather than refused — a report naming a
    /// recipient whose account was since deleted should send to the others, not fail.
    /// </remarks>
    /// <param name="userIds">The accounts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<StaffMember>> FindManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everybody currently able to work in a branch, in display-name order.
    /// </summary>
    /// <remarks>
    /// Only accounts that are active and assigned to the branch. An invited account that has never set
    /// a password, and a suspended one, are both people who cannot sign in — offering either as an
    /// assignee would produce a job nobody can pick up.
    /// </remarks>
    /// <param name="branchId">The branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<StaffMember>> ListActiveInBranchAsync(
        Guid branchId,
        CancellationToken cancellationToken = default);
}

/// <summary>One member of staff as another module sees them.</summary>
/// <param name="UserId">The account. A UUIDv7, and the only identifier that crosses the boundary.</param>
/// <param name="DisplayName">
/// The name shown on a board, a job card or a report. Personal data: screens and the audit trail only.
/// </param>
/// <param name="IsActive">
/// True when the account can sign in today. False covers invited, suspended and deactivated alike,
/// because to a caller deciding whether to give somebody work the three are the same answer.
/// </param>
/// <param name="Locale">
/// The IETF tag the person reads, for a notification or a report addressed to them. Never a branch's
/// locale and never the request's — a message is written in the language of whoever opens it.
/// </param>
/// <param name="RoleKeys">The roles they hold, by key, in a stable order.</param>
/// <param name="BranchIds">Every branch they are assigned to, in a stable order.</param>
/// <param name="HomeBranchId">
/// The branch their screens open on, when they have one. It is a default, not a permission: reach comes
/// from <paramref name="BranchIds"/>, and a caller treating this as authority would grant somebody
/// their home branch after it had been taken off their assignments.
/// </param>
public sealed record StaffMember(
    Guid UserId,
    string DisplayName,
    bool IsActive,
    string Locale,
    IReadOnlyList<string> RoleKeys,
    IReadOnlyList<Guid> BranchIds,
    Guid? HomeBranchId);
