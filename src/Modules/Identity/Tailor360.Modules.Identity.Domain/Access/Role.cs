using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Access;

/// <summary>
/// A named bundle of permissions. Roles are data, not code: the eleven the installation starts with are
/// seeded as system roles and an administrator may add their own, which is what makes the role list
/// re-cuttable without a release (plan Section 4.4, <c>docs/prd/00-overview.md</c> section 2).
/// </summary>
/// <remarks>
/// <para>
/// A <b>system role</b> is one this release ships and the matrix records. Its key never changes and it
/// cannot be deleted, because the permission matrix, the seeding command and the regression tests all
/// name it. Everything else about it — which permissions it grants, what it is called on screen — is
/// editable, so a shop that decides its Reception should not take advances changes a grant rather than
/// waiting for a release.
/// </para>
/// <para>
/// A <b>custom role</b> is one an administrator created. It has no seeded definition, may be renamed
/// and deleted, and is subject to exactly the same rule as every other role: a principal whose
/// effective permissions include one the catalogue flags <c>RequiresMfa</c> must enrol a second factor,
/// which is why inventing a "Senior cashier" role cannot quietly create an account that skips it.
/// </para>
/// </remarks>
public sealed class Role
{
    /// <summary>The longest role key the store accepts.</summary>
    public const int MaximumKeyLength = 64;

    /// <summary>The longest display name the store accepts.</summary>
    public const int MaximumNameLength = 100;

    /// <summary>The longest description the store accepts.</summary>
    public const int MaximumDescriptionLength = 500;

    /// <summary>The longest permission key the store accepts.</summary>
    public const int MaximumPermissionKeyLength = 100;

    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private Role(
        Guid id,
        Guid organisationId,
        string key,
        string name,
        string description,
        RoleReach reach,
        bool isSystem,
        bool assignedByDefault,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        Key = key;
        Name = name;
        Description = description;
        Reach = reach;
        IsSystem = isSystem;
        AssignedByDefault = assignedByDefault;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>Identity of the role.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the role belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The stable lower-case key, for example <c>branch_manager</c>. Never renamed.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The name shown on screen, for example <c>Branch Manager</c>.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What the role is for, in operator language.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How far the role is meant to reach.</summary>
    public RoleReach Reach { get; private set; }

    /// <summary>True when this release ships the role and the permission matrix records it.</summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// True when an installation is expected to assign this role at onboarding. False marks a role that
    /// exists and is deliberately assigned to nobody until the business asks for it — Measurement Staff,
    /// whose permission bundle Reception holds by default, and the vendor-side super-user.
    /// </summary>
    public bool AssignedByDefault { get; private set; }

    /// <summary>When the role was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it. Null for the roles written by reference-data seeding.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When the role last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>The permissions this role grants.</summary>
    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    /// <summary>The permission keys this role grants, ordered.</summary>
    public IReadOnlyList<string> PermissionKeys
        => [.. _permissions.Select(p => p.PermissionKey).Order(StringComparer.Ordinal)];

    /// <summary>Defines a role.</summary>
    /// <param name="id">Identity of the role, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="key">The stable lower-case key.</param>
    /// <param name="name">The name shown on screen.</param>
    /// <param name="description">What the role is for.</param>
    /// <param name="reach">How far the role is meant to reach.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="isSystem">True for a role this release ships.</param>
    /// <param name="assignedByDefault">True when an installation assigns it at onboarding.</param>
    /// <param name="by">The administrator defining it, when a person did.</param>
    public static Result<Role> Define(
        Guid id,
        Guid organisationId,
        string? key,
        string? name,
        string? description,
        RoleReach reach,
        DateTimeOffset now,
        bool isSystem = false,
        bool assignedByDefault = true,
        Guid? by = null)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<Role>(IdentityErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<Role>(IdentityErrors.Required("organisationId"));
        }

        var normalisedKey = key?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalisedKey))
        {
            return Result.Failure<Role>(IdentityErrors.Required("key"));
        }

        if (normalisedKey.Length > MaximumKeyLength)
        {
            return Result.Failure<Role>(IdentityErrors.TooLong("key", MaximumKeyLength));
        }

        if (!IsWellFormedKey(normalisedKey))
        {
            return Result.Failure<Role>(IdentityErrors.RoleKeyNotWellFormed);
        }

        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return Result.Failure<Role>(IdentityErrors.Required("name"));
        }

        if (trimmedName.Length > MaximumNameLength)
        {
            return Result.Failure<Role>(IdentityErrors.TooLong("name", MaximumNameLength));
        }

        var trimmedDescription = description?.Trim() ?? string.Empty;
        if (trimmedDescription.Length > MaximumDescriptionLength)
        {
            return Result.Failure<Role>(IdentityErrors.TooLong("description", MaximumDescriptionLength));
        }

        return new Role(
            id, organisationId, normalisedKey, trimmedName, trimmedDescription, reach, isSystem,
            assignedByDefault, now, by);
    }

    /// <summary>Renames the role and rewrites its description.</summary>
    public Result Describe(string? name, string? description, DateTimeOffset now, Guid? by)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return Result.Failure(IdentityErrors.Required("name"));
        }

        if (trimmedName.Length > MaximumNameLength)
        {
            return Result.Failure(IdentityErrors.TooLong("name", MaximumNameLength));
        }

        var trimmedDescription = description?.Trim() ?? string.Empty;
        if (trimmedDescription.Length > MaximumDescriptionLength)
        {
            return Result.Failure(IdentityErrors.TooLong("description", MaximumDescriptionLength));
        }

        Name = trimmedName;
        Description = trimmedDescription;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Records how far the role is meant to reach.</summary>
    public void SetReach(RoleReach reach, DateTimeOffset now, Guid? by)
    {
        Reach = reach;
        Touch(now, by);
    }

    /// <summary>Records whether an installation assigns this role at onboarding.</summary>
    public void SetAssignedByDefault(bool assignedByDefault, DateTimeOffset now, Guid? by)
    {
        AssignedByDefault = assignedByDefault;
        Touch(now, by);
    }

    /// <summary>Grants a permission. Granting one the role already holds changes nothing.</summary>
    public Result Grant(string? permissionKey, DateTimeOffset now, Guid? by)
    {
        var normalised = permissionKey?.Trim();
        if (string.IsNullOrEmpty(normalised))
        {
            return Result.Failure(IdentityErrors.Required("permissionKey"));
        }

        if (normalised.Length > MaximumPermissionKeyLength)
        {
            return Result.Failure(IdentityErrors.TooLong("permissionKey", MaximumPermissionKeyLength));
        }

        if (_permissions.Any(p => string.Equals(p.PermissionKey, normalised, StringComparison.Ordinal)))
        {
            return Result.Success();
        }

        _permissions.Add(RolePermission.Create(Id, normalised, now, by));
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Revokes a permission. Revoking one the role does not hold changes nothing.</summary>
    public void Revoke(string? permissionKey, DateTimeOffset now, Guid? by)
    {
        var removed = _permissions.RemoveAll(
            p => string.Equals(p.PermissionKey, permissionKey, StringComparison.Ordinal));

        if (removed > 0)
        {
            Touch(now, by);
        }
    }

    /// <summary>
    /// Replaces the role's grants with exactly this set. Used by reference-data seeding, so that a
    /// permission removed from a role's definition is removed from the database on the next run rather
    /// than lingering as a grant nobody approved.
    /// </summary>
    /// <returns>True when the set actually changed.</returns>
    public Result<bool> ReplacePermissions(
        IEnumerable<string> permissionKeys,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(permissionKeys);

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in permissionKeys)
        {
            var normalised = key?.Trim();
            if (string.IsNullOrEmpty(normalised))
            {
                return Result.Failure<bool>(IdentityErrors.Required("permissionKey"));
            }

            if (normalised.Length > MaximumPermissionKeyLength)
            {
                return Result.Failure<bool>(
                    IdentityErrors.TooLong("permissionKey", MaximumPermissionKeyLength));
            }

            wanted.Add(normalised);
        }

        var held = _permissions.Select(p => p.PermissionKey).ToHashSet(StringComparer.Ordinal);
        if (held.SetEquals(wanted))
        {
            return false;
        }

        _permissions.RemoveAll(p => !wanted.Contains(p.PermissionKey));
        foreach (var key in wanted.Except(held, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            _permissions.Add(RolePermission.Create(Id, key, now, by));
        }

        Touch(now, by);
        return true;
    }

    /// <summary>True when the role grants this permission.</summary>
    public bool Grants(string permissionKey)
        => _permissions.Any(p => string.Equals(p.PermissionKey, permissionKey, StringComparison.Ordinal));

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>
    /// A key is lower-case letters, digits and underscores, starting with a letter. The narrow shape is
    /// what lets a key be used unescaped in a document table, a seed file and a test name.
    /// </summary>
    private static bool IsWellFormedKey(string key)
        => char.IsAsciiLetterLower(key[0])
           && key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
}
