using Microsoft.Extensions.Logging;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Administration;

/// <summary>
/// The branch register: opening a location, describing it, and closing it.
/// </summary>
/// <remarks>
/// There is no delete. A branch code is embedded in every order, estimate and invoice number it ever
/// produced, and those numbers are printed on paper customers still hold — so a branch leaves the
/// register by being closed, never by being removed, and closing one is refused while anybody still
/// works there.
/// </remarks>
/// <param name="branches">The branch register.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="logger">Logger.</param>
public sealed class BranchAdministrationHandler(
    IBranchStore branches,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    ILogger<BranchAdministrationHandler> logger)
{
    /// <summary>The entity type branch entries are recorded against.</summary>
    public const string EntityType = "Branch";

    /// <summary>The audit action recorded when a branch is opened.</summary>
    public const string OpenedAction = "identity.branch.opened";

    /// <summary>The audit action recorded when a branch's description changes.</summary>
    public const string ReconfiguredAction = "identity.branch.reconfigured";

    /// <summary>The audit action recorded when a branch is closed.</summary>
    public const string ClosedAction = "identity.branch.closed";

    /// <summary>The audit action recorded when a closed branch is reopened.</summary>
    public const string ReopenedAction = "identity.branch.reopened";

    /// <summary>Lists the organisation's branches.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<AdministeredBranch>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var register = await branches.ListAsync(organisationId, cancellationToken);

        return [.. register.Select(branch => Describe(branch, branches.EntityTagOf(branch)))];
    }

    /// <summary>Reads one branch.</summary>
    /// <param name="branchId">The branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredBranch>> ReadAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var branch = await branches.FindAsync(branchId, cancellationToken);

        return branch is null
            ? Result.Failure<AdministeredBranch>(IdentityErrors.BranchNotFound)
            : Result.Success(Describe(branch, branches.EntityTagOf(branch)));
    }

    /// <summary>Opens a branch.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="code">The short code that will appear in its document numbers.</param>
    /// <param name="details">Its name, timezone, address and contacts.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredBranch>> OpenAsync(
        Guid organisationId,
        string? code,
        BranchDetails details,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);

        var normalised = code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (normalised.Length > 0
            && await branches.IsCodeTakenAsync(organisationId, normalised, cancellationToken))
        {
            return Result.Failure<AdministeredBranch>(IdentityErrors.BranchCodeAlreadyTaken);
        }

        var now = clock.UtcNow;

        var opened = Branch.Open(
            ids.NewId(), organisationId, normalised, details.Name, now, details.TimeZoneId, actor);

        if (opened.IsFailure)
        {
            return Result.Failure<AdministeredBranch>(opened.Error);
        }

        var branch = opened.Value;

        // Open validates the name and the timezone's length; Reconfigure is what checks the timezone
        // exists and records the rest of the description, so the two paths cannot disagree about what
        // a valid branch looks like.
        var described = branch.Reconfigure(details, now, actor);
        if (described.IsFailure)
        {
            return Result.Failure<AdministeredBranch>(described.Error);
        }

        branches.Add(branch);

        var written = await branches.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<AdministeredBranch>(written.Error);
        }

        await RecordAsync(
            OpenedAction, branch, $"Branch {branch.Code} was opened.", reason, null, cancellationToken);

        return Result.Success(Describe(branch, branches.EntityTagOf(branch)));
    }

    /// <summary>Changes a branch's description.</summary>
    /// <param name="branchId">The branch.</param>
    /// <param name="details">Its new name, timezone, address and contacts.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<AdministeredBranch>> ReconfigureAsync(
        Guid branchId,
        BranchDetails details,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            branchId,
            reason,
            actor,
            (branch, now) => branch.Reconfigure(details, now, actor),
            ReconfiguredAction,
            branch => $"Branch {branch.Code} was reconfigured.",
            cancellationToken);

    /// <summary>Closes a branch, if nobody still works there.</summary>
    /// <param name="branchId">The branch.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredBranch>> CloseAsync(
        Guid branchId,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        var dependents = await branches.CountDependentAccountsAsync(branchId, cancellationToken);

        if (dependents > 0)
        {
            // A count, not names. An administrator needs to know how many people to move; who they are
            // is on the list they are about to open, and putting names in a refusal puts them in every
            // log and trace that carries the response.
            return Result.Failure<AdministeredBranch>(IdentityErrors.BranchStillInUse(dependents));
        }

        return await ApplyAsync(
            branchId,
            reason,
            actor,
            (branch, now) => branch.Deactivate(reason, now, actor),
            ClosedAction,
            branch => $"Branch {branch.Code} was closed.",
            cancellationToken);
    }

    /// <summary>Reopens a closed branch.</summary>
    /// <param name="branchId">The branch.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<AdministeredBranch>> ReopenAsync(
        Guid branchId,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            branchId,
            reason,
            actor,
            (branch, now) => branch.Reactivate(reason, now, actor),
            ReopenedAction,
            branch => $"Branch {branch.Code} was reopened.",
            cancellationToken);

    private async Task<Result<AdministeredBranch>> ApplyAsync(
        Guid branchId,
        string reason,
        Guid actor,
        Func<Branch, DateTimeOffset, Result> change,
        string action,
        Func<Branch, string> summary,
        CancellationToken cancellationToken)
    {
        var branch = await branches.FindAsync(branchId, cancellationToken);
        if (branch is null)
        {
            return Result.Failure<AdministeredBranch>(IdentityErrors.BranchNotFound);
        }

        var before = SnapshotOf(branch);

        var changed = change(branch, clock.UtcNow);
        if (changed.IsFailure)
        {
            return Result.Failure<AdministeredBranch>(changed.Error);
        }

        var written = await branches.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<AdministeredBranch>(written.Error);
        }

        await RecordAsync(action, branch, summary(branch), reason, before, cancellationToken);

        return Result.Success(Describe(branch, branches.EntityTagOf(branch)));
    }

    private async Task RecordAsync(
        string action,
        Branch branch,
        string summary,
        string reason,
        BranchSnapshot? before,
        CancellationToken cancellationToken)
    {
        await AdministrationAudit.RecordAsync(
            audit, action, EntityType, branch.Id, summary, reason, before, SnapshotOf(branch),
            cancellationToken);

        IdentityLog.BranchAdministered(logger, branch.Id, action);
    }

    /// <summary>
    /// What the trail records. The code, the standing and the timezone — the three things whose change
    /// changes what the system does — and not the address, which is contact detail rather than control.
    /// </summary>
    private static BranchSnapshot SnapshotOf(Branch branch)
        => new(branch.Code, branch.Name, branch.TimeZoneId, branch.Status.ToString());

    private static AdministeredBranch Describe(Branch branch, EntityTag version) => new(
        branch.Id,
        branch.Code,
        branch.Name,
        branch.TimeZoneId,
        branch.Status.ToString(),
        branch.StatusReason,
        new BranchDetails(
            branch.Name,
            branch.TimeZoneId,
            branch.AddressLine1,
            branch.AddressLine2,
            branch.City,
            branch.State,
            branch.PostalCode,
            branch.ContactPhone,
            branch.ContactEmail,
            branch.GstRegistrationReference),
        version);
}

/// <summary>One branch as an administrative screen sees it.</summary>
/// <param name="BranchId">The branch.</param>
/// <param name="Code">Its short code, which never changes.</param>
/// <param name="Name">Its name.</param>
/// <param name="TimeZoneId">The timezone its due dates are computed in.</param>
/// <param name="Status">Whether it is open.</param>
/// <param name="StatusReason">Why it was last opened or closed.</param>
/// <param name="Details">Its editable description.</param>
/// <param name="Version">The concurrency token an edit must present.</param>
public sealed record AdministeredBranch(
    Guid BranchId,
    string Code,
    string Name,
    string TimeZoneId,
    string Status,
    string? StatusReason,
    BranchDetails Details,
    EntityTag Version);

/// <summary>The branch state recorded in the audit trail before and after a change.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="TimeZoneId">Its timezone.</param>
/// <param name="Status">Its standing.</param>
internal sealed record BranchSnapshot(string Code, string Name, string TimeZoneId, string Status);
