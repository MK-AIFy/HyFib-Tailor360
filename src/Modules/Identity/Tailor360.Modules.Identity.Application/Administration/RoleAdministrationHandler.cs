using Microsoft.Extensions.Logging;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Application.Administration;

/// <summary>
/// The role register: what each role grants, and who may change it.
/// </summary>
/// <remarks>
/// <para>
/// Roles are data and the catalogue is code. That single division is what this handler exists to keep:
/// an administrator may re-cut which permissions a role bundles without waiting for a release, and
/// cannot invent a permission the application does not implement. Every refusal below is one of the two
/// halves of that line, or a guard against the change locking the organisation out of its own
/// administration.
/// </para>
/// <para>
/// <b>Four invariants, checked here rather than in the aggregate.</b> Each is a question about the
/// catalogue or about other rows, and a <see cref="Role"/> can see neither:
/// </para>
/// <list type="number">
/// <item>Every key granted is in the catalogue — otherwise a typo becomes a grant of nothing.</item>
/// <item>
/// An organisation-scoped permission goes only to a role with organisation reach. This is the runtime
/// half of the rule <c>docs/security/permission-matrix.md</c> section 4 states, whose document test
/// checks the seeded register. Without this, the rule would hold for the roles the release ships and
/// stop holding the first time somebody used the screen built to change them.
/// </item>
/// <item>
/// A grant may only pass on authority the granter already holds, read from the database rather than
/// from their session. An administrator who may edit any role could otherwise write themselves a role
/// granting everything the catalogue declares, which would make every other permission boundary in the
/// application advisory.
/// </item>
/// <item>
/// The change must leave somebody able to edit roles. This surface is the way back from most mistakes;
/// it is also the one mistake it cannot undo.
/// </item>
/// </list>
/// <para>
/// <b>There is no delete for a system role, and no delete for a role somebody holds.</b> A system role
/// is named by the permission matrix, the seeding command and the regression tests, so its key has to
/// stay resolvable; a held role is somebody's access, and removing it silently would take that access
/// away without an entry saying whose it was.
/// </para>
/// </remarks>
/// <param name="roles">The role register.</param>
/// <param name="catalogue">Every permission the application declares.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="logger">Logger. Never receives a reason or a name.</param>
public sealed class RoleAdministrationHandler(
    IRoleStore roles,
    PermissionCatalogue catalogue,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    ILogger<RoleAdministrationHandler> logger)
{
    /// <summary>The entity type role entries are recorded against.</summary>
    public const string EntityType = "Role";

    /// <summary>The audit action recorded when a custom role is defined.</summary>
    public const string DefinedAction = "identity.role.defined";

    /// <summary>The audit action recorded when a role is renamed or redescribed.</summary>
    public const string DescribedAction = "identity.role.described";

    /// <summary>The audit action recorded when a role's grants are replaced.</summary>
    public const string PermissionsReplacedAction = "identity.role.permissions-replaced";

    /// <summary>The audit action recorded when a custom role is deleted.</summary>
    public const string DeletedAction = "identity.role.deleted";

    /// <summary>Lists the organisation's roles with their grants and holder counts.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<AdministeredRole>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var register = await roles.ListAsync(organisationId, cancellationToken);
        var holders = await roles.CountHoldersAsync(organisationId, cancellationToken);

        return [.. register.Select(role => Describe(role, holders.GetValueOrDefault(role.Id)))];
    }

    /// <summary>Reads one role.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredRole>> ReadAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var role = await roles.FindAsync(roleId, cancellationToken);

        if (role is null)
        {
            return Result.Failure<AdministeredRole>(IdentityErrors.RoleNotFoundById);
        }

        var holders = await roles.CountHoldersAsync(role.OrganisationId, cancellationToken);

        return Describe(role, holders.GetValueOrDefault(role.Id));
    }

    /// <summary>Defines a custom role, granting it nothing yet.</summary>
    /// <remarks>
    /// Deliberately created empty. A role's grants are the consequential part and go through
    /// <see cref="ReplacePermissionsAsync"/>, which is where the four invariants are checked — letting
    /// creation carry a permission set would mean either duplicating those checks or having a way in
    /// that skips them.
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="key">The stable lower-case key.</param>
    /// <param name="name">The name shown on screen.</param>
    /// <param name="description">What the role is for.</param>
    /// <param name="reach">How far the role is meant to reach.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredRole>> DefineAsync(
        Guid organisationId,
        string? key,
        string? name,
        string? description,
        RoleReach reach,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        var defined = Role.Define(
            ids.NewId(), organisationId, key, name, description, reach, clock.UtcNow,
            isSystem: false, assignedByDefault: false, by: actor);

        if (defined.IsFailure)
        {
            return Result.Failure<AdministeredRole>(defined.Error);
        }

        var role = defined.Value;

        if (await roles.IsKeyTakenAsync(organisationId, role.Key, cancellationToken))
        {
            return Result.Failure<AdministeredRole>(IdentityErrors.RoleKeyAlreadyTaken);
        }

        roles.Add(role);

        var written = await roles.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<AdministeredRole>(written.Error);
        }

        await AdministrationAudit.RecordAsync(
            audit,
            DefinedAction,
            EntityType,
            role.Id,
            $"The custom role '{role.Key}' was defined with {role.Reach.ToString().ToLowerInvariant()} reach.",
            reason,
            before: null,
            after: Snapshot(role),
            cancellationToken);

        IdentityLog.RoleAdministered(logger, role.Id, DefinedAction, role.Permissions.Count);

        return Describe(role, holders: 0);
    }

    /// <summary>Renames a role and rewrites its description. The key never changes.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="name">The new name.</param>
    /// <param name="description">The new description.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredRole>> DescribeAsync(
        Guid roleId,
        string? name,
        string? description,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        var role = await roles.FindAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<AdministeredRole>(IdentityErrors.RoleNotFoundById);
        }

        var before = Snapshot(role);

        var described = role.Describe(name, description, clock.UtcNow, actor);
        if (described.IsFailure)
        {
            return Result.Failure<AdministeredRole>(described.Error);
        }

        var written = await roles.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<AdministeredRole>(written.Error);
        }

        await AdministrationAudit.RecordAsync(
            audit,
            DescribedAction,
            EntityType,
            role.Id,
            $"The role '{role.Key}' was renamed or redescribed.",
            reason,
            before,
            Snapshot(role),
            cancellationToken);

        return await ReadAsync(roleId, cancellationToken);
    }

    /// <summary>
    /// Replaces a role's grants with exactly the set given.
    /// </summary>
    /// <remarks>
    /// Whole-set rather than add-and-remove, for the reason the account assignment handler gives:
    /// "these fourteen, where it used to be these twelve" is a sentence an audit entry can be read
    /// against, and "added two" is a diff somebody has to reconstruct from a state they no longer have.
    /// </remarks>
    /// <param name="roleId">The role.</param>
    /// <param name="permissionKeys">The permissions it should grant afterwards.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator, whose own grants bound what they may pass on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredRole>> ReplacePermissionsAsync(
        Guid roleId,
        IReadOnlyCollection<string> permissionKeys,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissionKeys);

        var role = await roles.FindAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<AdministeredRole>(IdentityErrors.RoleNotFoundById);
        }

        var wanted = new List<string>();
        foreach (var raw in permissionKeys.Select(key => key?.Trim()).Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(raw))
            {
                return Result.Failure<AdministeredRole>(IdentityErrors.Required("permissionKeys"));
            }

            if (catalogue.Find(raw) is not { } permission)
            {
                return Result.Failure<AdministeredRole>(IdentityErrors.PermissionNotCatalogued(raw));
            }

            if (permission.Scope == PermissionScope.Organisation && role.Reach != RoleReach.Organisation)
            {
                return Result.Failure<AdministeredRole>(IdentityErrors.PermissionExceedsRoleReach(raw));
            }

            wanted.Add(raw);
        }

        // Only the keys actually being added are checked against the granter's own authority. Taking a
        // permission away is not escalation, and an administrator asked to reduce a role they do not
        // fully hold — which is the usual case for anybody below the Owner — must be able to do it.
        var added = wanted.Except(role.PermissionKeys, StringComparer.Ordinal).ToArray();

        if (added.Length > 0)
        {
            var held = await roles.PermissionsOfAsync(actor, cancellationToken);

            if (added.FirstOrDefault(key => !held.Contains(key)) is { } beyond)
            {
                IdentityLog.RoleGrantExceededGranter(logger, roleId);

                return Result.Failure<AdministeredRole>(IdentityErrors.PermissionNotHeldByGranter(beyond));
            }
        }

        // The lockout guard. Asked only when the change actually drops the permission, and asked of the
        // accounts that would remain, because the role being edited still grants it as we ask.
        if (role.Grants(IdentityPermissions.Roles)
            && !wanted.Contains(IdentityPermissions.Roles, StringComparer.Ordinal)
            && await roles.CountHoldersThroughOtherRolesAsync(
                role.OrganisationId, IdentityPermissions.Roles, role.Id, cancellationToken) == 0)
        {
            return Result.Failure<AdministeredRole>(IdentityErrors.LastRoleAdministrator);
        }

        var before = Snapshot(role);

        var replaced = role.ReplacePermissions(wanted, clock.UtcNow, actor);
        if (replaced.IsFailure)
        {
            return Result.Failure<AdministeredRole>(replaced.Error);
        }

        // A replace that changed something already moved the row's version, because the aggregate stamps
        // UpdatedAt as part of it. This is for the replace that changed nothing — the administrator sent
        // back the set they were shown — which would otherwise leave the version where it was and let
        // somebody still holding it write over the top afterwards. A PUT that succeeded consumed the
        // version it presented, whether or not the outcome differed.
        roles.Touch(role);

        var written = await roles.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<AdministeredRole>(written.Error);
        }

        await AdministrationAudit.RecordAsync(
            audit,
            PermissionsReplacedAction,
            EntityType,
            role.Id,
            $"The role '{role.Key}' now grants {role.Permissions.Count} permission(s).",
            reason,
            before,
            Snapshot(role),
            cancellationToken);

        IdentityLog.RoleAdministered(logger, role.Id, PermissionsReplacedAction, role.Permissions.Count);

        return await ReadAsync(roleId, cancellationToken);
    }

    /// <summary>Deletes a custom role that nobody holds.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> DeleteAsync(
        Guid roleId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var role = await roles.FindAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure(IdentityErrors.RoleNotFoundById);
        }

        if (role.IsSystem)
        {
            return Result.Failure(IdentityErrors.SystemRoleNotDeletable);
        }

        var holders = await roles.CountHoldersAsync(role.OrganisationId, cancellationToken);
        if (holders.GetValueOrDefault(role.Id) is var held and > 0)
        {
            return Result.Failure(IdentityErrors.RoleStillHeld(held));
        }

        var before = Snapshot(role);
        var key = role.Key;

        roles.Remove(role);

        var written = await roles.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return written;
        }

        // Written after the row is gone and against the identifier it had. The entry is what makes a
        // later "where did this role go" answerable, so it outlives the thing it describes.
        await AdministrationAudit.RecordAsync(
            audit,
            DeletedAction,
            EntityType,
            roleId,
            $"The custom role '{key}' was deleted.",
            reason,
            before,
            after: null,
            cancellationToken);

        IdentityLog.RoleAdministered(logger, roleId, DeletedAction, permissionCount: 0);

        return Result.Success();
    }

    private AdministeredRole Describe(Role role, int holders) => new(
        role.Id,
        role.Key,
        role.Name,
        role.Description,
        role.Reach,
        role.IsSystem,
        role.AssignedByDefault,
        role.PermissionKeys,
        holders,
        role.UpdatedAt,
        role.UpdatedBy,
        roles.EntityTagOf(role));

    private static RoleSnapshot Snapshot(Role role) => new(
        role.Key,
        role.Name,
        role.Reach.ToString(),
        role.PermissionKeys);
}

/// <summary>One role as the administration screens show it.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="Key">The stable lower-case key, which never changes.</param>
/// <param name="Name">The name shown on screen.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="Reach">How far the role is meant to reach.</param>
/// <param name="IsSystem">True when this release ships the role and it cannot be deleted.</param>
/// <param name="AssignedByDefault">True when an installation assigns it at onboarding.</param>
/// <param name="PermissionKeys">What it grants, in key order.</param>
/// <param name="Holders">How many accounts hold it.</param>
/// <param name="UpdatedAt">When it last changed.</param>
/// <param name="UpdatedBy">Who last changed it, or null for a value that arrived with the seed data.</param>
/// <param name="Version">The concurrency token an edit must present.</param>
public sealed record AdministeredRole(
    Guid RoleId,
    string Key,
    string Name,
    string Description,
    RoleReach Reach,
    bool IsSystem,
    bool AssignedByDefault,
    IReadOnlyList<string> PermissionKeys,
    int Holders,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy,
    EntityTag Version);

/// <summary>
/// The administered state of one role, as it is recorded in the audit trail before and after a change.
/// </summary>
/// <remarks>
/// Keys and a reach, never a holder's name: a role is configuration, and who holds it is recorded
/// against the accounts rather than against the role, where a rename would orphan it.
/// </remarks>
/// <param name="Key">The role key.</param>
/// <param name="Name">The display name.</param>
/// <param name="Reach">The role's reach, as its name.</param>
/// <param name="PermissionKeys">What it granted, in key order.</param>
internal sealed record RoleSnapshot(
    string Key,
    string Name,
    string Reach,
    IReadOnlyList<string> PermissionKeys);
