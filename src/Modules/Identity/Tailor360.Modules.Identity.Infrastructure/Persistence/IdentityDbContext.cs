using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Domain.Credentials;
using Tailor360.Modules.Identity.Domain.Mfa;
using Tailor360.Modules.Identity.Domain.Passkeys;
using Tailor360.Modules.Identity.Domain.Recovery;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// The Identity module's database context. It owns the <c>identity</c> schema and nothing else reads
/// it: other modules learn a display name or a branch scope through the module's read contract, never
/// by joining to these tables (ARCH-005, module ownership section 5.1).
/// </summary>
/// <remarks>
/// The mapping is written here rather than split into per-entity configuration classes to match the
/// platform context, and because the <c>xmin</c> helper is a protected member of the base: keeping the
/// mapping in one file keeps the concurrency decisions visible next to each other, which is where they
/// need to be argued.
/// <para>
/// <b>Concurrency.</b> <c>users</c> and <c>user_preferences</c> carry the <c>xmin</c> token. The
/// children of the user aggregate — credentials, the authenticator, recovery codes, passkeys — do not
/// carry their own, because every operation that touches one also touches the root, so the root's token
/// is the serialisation point. That is what makes single use of a recovery code safe under a race: two
/// requests that both find the same unspent code both write the root, and exactly one of them commits.
/// </para>
/// <para>
/// <b>Sessions and trusted devices deliberately carry no token.</b> Their rows are updated on ordinary
/// requests — sliding the inactivity deadline, stamping a last-seen time — and a progressive web
/// application issues several requests at once, so an optimistic-concurrency token there would turn
/// normal page loads into conflicts. Correctness for these rows comes instead from the operations being
/// last-write-wins on a timestamp, and from revocation being one conditional update rather than a
/// read-modify-write.
/// </para>
/// </remarks>
/// <param name="options">Context options.</param>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "identity";

    /// <summary>Staff accounts.</summary>
    public DbSet<StaffUser> Users => Set<StaffUser>();

    /// <summary>Stored password hashes, one current credential per account.</summary>
    public DbSet<PasswordCredential> Credentials => Set<PasswordCredential>();

    /// <summary>Time-based one-time-password enrolments.</summary>
    public DbSet<TotpEnrolment> TotpEnrolments => Set<TotpEnrolment>();

    /// <summary>Hashed single-use recovery codes.</summary>
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    /// <summary>Registered WebAuthn credentials.</summary>
    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();

    /// <summary>Server-side session tickets.</summary>
    public DbSet<Session> Sessions => Set<Session>();

    /// <summary>Devices a holder has asked the system to remember.</summary>
    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();

    /// <summary>Per-account interface preferences.</summary>
    public DbSet<UserPreferences> Preferences => Set<UserPreferences>();

    /// <summary>Single-use expiring tokens for password recovery and invitations.</summary>
    public DbSet<RecoveryToken> RecoveryTokens => Set<RecoveryToken>();

    /// <summary>The branch register every branch-scoped decision is measured against.</summary>
    public DbSet<Branch> Branches => Set<Branch>();

    /// <summary>Roles: the seeded system roles and any an administrator has added.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>The permissions each role grants.</summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    /// <summary>Which accounts hold which roles.</summary>
    public DbSet<UserRoleAssignment> UserRoles => Set<UserRoleAssignment>();

    /// <summary>Which branches each account may act in.</summary>
    public DbSet<UserBranchAssignment> UserBranchAssignments => Set<UserBranchAssignment>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        ConfigureUsers(modelBuilder);
        ConfigureCredentials(modelBuilder);
        ConfigureMultiFactor(modelBuilder);
        ConfigurePasskeys(modelBuilder);
        ConfigureSessions(modelBuilder);
        ConfigurePreferences(modelBuilder);
        ConfigureRecovery(modelBuilder);
        ConfigureBranches(modelBuilder);
        ConfigureAccess(modelBuilder);
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
        => modelBuilder.Entity<StaffUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);

            // Computed from the aggregate's children; there is nothing to store.
            entity.Ignore(e => e.UnusedRecoveryCodeCount);
            entity.Ignore(e => e.HasConfirmedSecondFactor);

            entity.Property(e => e.UserName).HasMaxLength(StaffUser.MaximumUserNameLength).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(StaffUser.MaximumEmailLength).IsRequired();
            entity.Property(e => e.NormalisedEmail)
                .HasMaxLength(StaffUser.MaximumEmailLength).IsRequired();
            entity.Property(e => e.DisplayName)
                .HasMaxLength(StaffUser.MaximumDisplayNameLength).IsRequired();

            // Statuses are stored as their names. A reordered enum then cannot silently reinterpret
            // every existing row, and an operator reading the table sees words rather than integers.
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.MfaEnrolment).HasConversion<string>().HasMaxLength(30).IsRequired();

            // The sign-in name and the address are unique within the organisation, not globally, so a
            // second organisation could be added later without a data migration. The name is stored
            // folded to lower case and the address upper-cased, so both indexes are case-insensitive
            // without a functional index the query planner would have to be coaxed into using.
            entity.HasIndex(e => new { e.OrganisationId, e.UserName })
                .IsUnique()
                .HasDatabaseName("ux_users_organisation_user_name");
            entity.HasIndex(e => new { e.OrganisationId, e.NormalisedEmail })
                .IsUnique()
                .HasDatabaseName("ux_users_organisation_normalised_email");
            entity.HasIndex(e => e.HomeBranchId).HasDatabaseName("ix_users_home_branch");

            UseRowVersion(entity);
        });

    private static void ConfigureCredentials(ModelBuilder modelBuilder)
        => modelBuilder.Entity<PasswordCredential>(entity =>
        {
            // Algorithm-agnostic, unlike the digest constraints: any encoded hash is long and carries
            // no whitespace, and a password is neither. The check would still catch a plaintext
            // password if a future hasher stopped producing PHC strings.
            entity.ToTable("user_credentials", table => table.HasCheckConstraint(
                "ck_user_credentials_encoded_hash_is_encoded",
                $"char_length(encoded_hash) >= {PasswordCredential.MinimumEncodedLength} "
                + "AND encoded_hash !~ '\\s'"));
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EncodedHash)
                .HasMaxLength(PasswordCredential.MaximumEncodedLength).IsRequired();
            entity.Property(e => e.Algorithm)
                .HasMaxLength(PasswordCredential.MaximumAlgorithmLength).IsRequired();

            // One current credential per account. History, if it is ever needed, becomes its own table
            // rather than a second row here, so "the password" is never ambiguous.
            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("ux_user_credentials_user");

            entity.HasOne<StaffUser>()
                .WithOne(user => user.Password)
                .HasForeignKey<PasswordCredential>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureMultiFactor(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TotpEnrolment>(entity =>
        {
            entity.ToTable("totp_enrolments");
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.IsConfirmed);

            entity.Property(e => e.ProtectedSecret)
                .HasMaxLength(TotpEnrolment.MaximumProtectedSecretLength).IsRequired();

            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("ux_totp_enrolments_user");

            entity.HasOne<StaffUser>()
                .WithOne(user => user.Totp)
                .HasForeignKey<TotpEnrolment>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Accepting a code is a read-modify-write on this row: the step is read, the enrolment
            // refuses one it has already seen, and the new step is written back. Two requests carrying
            // the same code both read a step that permits it, so without a token both would write and
            // a one-time password would have been used twice. The token makes the database settle it.
            UseRowVersion(entity);
        });

        modelBuilder.Entity<RecoveryCode>(entity =>
        {
            // The digest shape is asserted by the database as well as by the domain. A recovery code
            // that reached this table in a legible form would be a credential lying in the clear, so
            // the constraint is worth the few microseconds it costs on insert.
            entity.ToTable("recovery_codes", table => table.HasCheckConstraint(
                "ck_recovery_codes_code_hash_is_digest", $"code_hash ~ '{DigestPattern}'"));
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.IsConsumed);

            entity.Property(e => e.CodeHash).HasMaxLength(HashedSecretLength).IsRequired();

            // Two codes on one account never share a digest, so a collision cannot make one redemption
            // spend the wrong row.
            entity.HasIndex(e => new { e.UserId, e.CodeHash })
                .IsUnique()
                .HasDatabaseName("ux_recovery_codes_user_hash");

            // Redemption only ever looks at unspent codes, and a long-lived account accumulates spent
            // ones, so the index that serves that lookup excludes them.
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("ix_recovery_codes_unspent")
                .HasFilter("consumed_at IS NULL");

            entity.HasOne<StaffUser>()
                .WithMany(user => user.RecoveryCodes)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Redemption is the same read-modify-write as an authenticator code, and a recovery code
            // spent twice is worth more to an attacker than a step replayed: it is a whole factor.
            UseRowVersion(entity);
        });

        modelBuilder.Entity<StaffUser>()
            .Navigation(user => user.RecoveryCodes)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigurePasskeys(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PasskeyCredential>(entity =>
        {
            entity.ToTable("passkey_credentials");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CredentialId).IsRequired();
            entity.Property(e => e.PublicKey).IsRequired();
            entity.Property(e => e.Label)
                .HasMaxLength(PasskeyCredential.MaximumLabelLength).IsRequired();
            entity.Property(e => e.Transports).HasMaxLength(PasskeyCredential.MaximumTransportsLength);

            // WebAuthn credential identifiers are unique across the relying party, not merely within an
            // account, so the uniqueness is asserted where the standard asserts it.
            entity.HasIndex(e => e.CredentialId)
                .IsUnique()
                .HasDatabaseName("ux_passkey_credentials_credential_id");
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_passkey_credentials_user");

            entity.HasOne<StaffUser>()
                .WithMany(user => user.Passkeys)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StaffUser>()
            .Navigation(user => user.Passkeys)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureSessions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Session>(entity =>
        {
            // Two invariants the domain enforces are restated here, because a session row written by
            // anything other than the domain — a repair script, a future bulk operation — must not be
            // able to create a ticket that outlives its absolute expiry or that stores a raw cookie
            // value.
            entity.ToTable("sessions", table =>
            {
                table.HasCheckConstraint(
                    "ck_sessions_token_hash_is_digest", $"token_hash ~ '{DigestPattern}'");
                table.HasCheckConstraint(
                    "ck_sessions_idle_within_absolute", "idle_expires_at <= absolute_expires_at");
            });
            entity.HasKey(e => e.Id);

            // Computed from the stored step; there is nothing to store.
            entity.Ignore(e => e.IsSignInComplete);

            entity.Property(e => e.TokenHash)
                .HasMaxLength(HashedSecretLength).IsRequired();
            entity.Property(e => e.DeviceLabel)
                .HasMaxLength(Session.MaximumDeviceLabelLength).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(Session.MaximumIpAddressLength);
            entity.Property(e => e.UserAgent).HasMaxLength(Session.MaximumUserAgentLength);
            entity.Property(e => e.EndReason).HasConversion<string>().HasMaxLength(30);

            // Stored as its name, like every other enum here: a reordered enum then cannot silently
            // reinterpret existing rows, and this column decides what a session may reach.
            entity.Property(e => e.PendingStep).HasConversion<string>().HasMaxLength(30).IsRequired();

            // Every authenticated request resolves a session by the digest of its cookie. It is the
            // hottest lookup in the system, so it is a unique index and nothing else.
            entity.HasIndex(e => e.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_sessions_token_hash");

            // The session inventory and "sign out everywhere" both walk one account's live sessions.
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("ix_sessions_user_live")
                .HasFilter("revoked_at IS NULL");

            // The retention job deletes sessions that ended long ago; it scans by expiry, not by user.
            entity.HasIndex(e => e.AbsoluteExpiresAt)
                .HasDatabaseName("ix_sessions_absolute_expires_at");

            entity.HasOne<StaffUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrustedDevice>(entity =>
        {
            entity.ToTable("trusted_devices", table =>
            {
                table.HasCheckConstraint(
                    "ck_trusted_devices_token_hash_is_digest", $"token_hash ~ '{DigestPattern}'");

                // Thirty days is the ceiling issue #23 sets on remembering a device. Restating it here
                // means no code path, present or future, can quietly issue a device that is remembered
                // for a year.
                table.HasCheckConstraint(
                    "ck_trusted_devices_lifetime",
                    $"expires_at <= created_at + interval '{TrustedDevice.MaximumLifetimeDays} days'");
            });
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TokenHash)
                .HasMaxLength(HashedSecretLength).IsRequired();
            entity.Property(e => e.Label)
                .HasMaxLength(TrustedDevice.MaximumLabelLength).IsRequired();

            entity.HasIndex(e => e.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_trusted_devices_token_hash");
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("ix_trusted_devices_user_live")
                .HasFilter("revoked_at IS NULL");

            entity.HasOne<StaffUser>()
                .WithMany(user => user.TrustedDevices)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StaffUser>()
            .Navigation(user => user.TrustedDevices)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigurePreferences(ModelBuilder modelBuilder)
        => modelBuilder.Entity<UserPreferences>(entity =>
        {
            entity.ToTable("user_preferences");
            entity.HasKey(e => e.UserId);

            entity.Property(e => e.Locale).HasMaxLength(16).IsRequired();
            entity.Property(e => e.TimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Theme).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Density).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.LandingRoute)
                .HasMaxLength(UserPreferences.MaximumLandingRouteLength);

            entity.HasOne<StaffUser>()
                .WithOne(user => user.Preferences)
                .HasForeignKey<UserPreferences>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            UseRowVersion(entity);
        });

    private static void ConfigureRecovery(ModelBuilder modelBuilder)
        => modelBuilder.Entity<RecoveryToken>(entity =>
        {
            // A recovery token is a password with a timer on it, so both halves of that sentence are
            // asserted by the database as well as by the domain: the stored value is a digest and
            // never the link's value, and the timer is never longer than the domain's ceiling however
            // the row was written.
            entity.ToTable("recovery_tokens", table =>
            {
                table.HasCheckConstraint(
                    "ck_recovery_tokens_token_hash_is_digest", $"token_hash ~ '{DigestPattern}'");
                table.HasCheckConstraint(
                    "ck_recovery_tokens_lifetime",
                    $"expires_at <= created_at + interval '{RecoveryToken.MaximumLifetime.TotalMinutes:0} minutes'");
            });
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.IsConsumed);

            entity.Property(e => e.TokenHash).HasMaxLength(HashedSecretLength).IsRequired();
            entity.Property(e => e.Purpose).HasConversion<string>().HasMaxLength(30).IsRequired();

            // Redemption resolves a token by the digest of the value in the link, which is the only
            // lookup on this table that happens while somebody is waiting.
            entity.HasIndex(e => e.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_recovery_tokens_token_hash");

            // Issuing a link withdraws the ones before it, which walks one account's outstanding
            // tokens of one purpose; spent and withdrawn rows are of no interest to that walk.
            entity.HasIndex(e => new { e.UserId, e.Purpose })
                .HasDatabaseName("ix_recovery_tokens_outstanding")
                .HasFilter("consumed_at IS NULL AND invalidated_at IS NULL");

            // The retention job clears tokens that expired long ago; it scans by expiry, not by user.
            entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_recovery_tokens_expires_at");

            entity.HasOne<StaffUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureBranches(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("branches");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Code).HasMaxLength(Branch.MaximumCodeLength).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(Branch.MaximumNameLength).IsRequired();
            entity.Property(e => e.TimeZoneId).HasMaxLength(Branch.MaximumTimeZoneLength).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.StatusReason).HasMaxLength(Branch.MaximumStatusReasonLength);

            // Master data. Every column is nullable: a branch is opened before somebody has walked
            // round it with a tape measure, and refusing to record one until its postcode is known
            // would mean the register cannot be used on the day it is needed.
            entity.Property(e => e.AddressLine1).HasMaxLength(Branch.MaximumAddressLineLength);
            entity.Property(e => e.AddressLine2).HasMaxLength(Branch.MaximumAddressLineLength);
            entity.Property(e => e.City).HasMaxLength(Branch.MaximumContactLength);
            entity.Property(e => e.State).HasMaxLength(Branch.MaximumContactLength);
            entity.Property(e => e.PostalCode).HasMaxLength(Branch.MaximumContactLength);
            entity.Property(e => e.ContactPhone).HasMaxLength(Branch.MaximumContactLength);
            entity.Property(e => e.ContactEmail).HasMaxLength(Branch.MaximumContactLength);
            entity.Property(e => e.GstRegistrationReference).HasMaxLength(Branch.MaximumContactLength);

            // The code appears in every order, estimate and invoice number, so two branches sharing one
            // would make those numbers ambiguous for the life of the installation.
            entity.HasIndex(e => new { e.OrganisationId, e.Code })
                .IsUnique()
                .HasDatabaseName("ux_branches_organisation_code");

            UseRowVersion(entity);
        });

    private static void ConfigureAccess(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.PermissionKeys);

            entity.Property(e => e.Key).HasMaxLength(Role.MaximumKeyLength).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(Role.MaximumNameLength).IsRequired();
            entity.Property(e => e.Description)
                .HasMaxLength(Role.MaximumDescriptionLength).IsRequired();
            entity.Property(e => e.Reach).HasConversion<string>().HasMaxLength(20).IsRequired();

            // The key is what the permission matrix, the seeding command and the regression tests name
            // a role by, so it is unique within the organisation and never reused.
            entity.HasIndex(e => new { e.OrganisationId, e.Key })
                .IsUnique()
                .HasDatabaseName("ux_roles_organisation_key");

            entity.HasMany(e => e.Permissions)
                .WithOne()
                .HasForeignKey(p => p.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");

            // The pair is the key. A role either grants a permission or does not; granting it twice is
            // not a state the store can hold, so no reconciliation has to consider it.
            entity.HasKey(e => new { e.RoleId, e.PermissionKey });

            entity.Property(e => e.PermissionKey)
                .HasMaxLength(Role.MaximumPermissionKeyLength).IsRequired();

            // Answering "who holds this permission" walks the key, which is what the administration
            // screens and the authorisation review both ask.
            entity.HasIndex(e => e.PermissionKey).HasDatabaseName("ix_role_permissions_permission");
        });

        modelBuilder.Entity<UserRoleAssignment>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(e => new { e.UserId, e.RoleId });

            entity.HasOne<StaffUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // A role in use cannot be deleted out from under the accounts that hold it. Removing a role
            // is removing its assignments first, deliberately, rather than by cascade.
            entity.HasOne<Role>()
                .WithMany()
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.RoleId).HasDatabaseName("ix_user_roles_role");
        });

        modelBuilder.Entity<UserBranchAssignment>(entity =>
        {
            entity.ToTable("user_branch_assignments");
            entity.HasKey(e => new { e.UserId, e.BranchId });

            entity.HasOne<StaffUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // A branch is deactivated, never deleted, so an assignment can never point at nothing.
            entity.HasOne<Branch>()
                .WithMany()
                .HasForeignKey(e => e.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.BranchId).HasDatabaseName("ix_user_branch_assignments_branch");

            // One primary branch per account: it is where a session starts, and two would make the
            // starting branch depend on row order.
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasDatabaseName("ux_user_branch_assignments_primary")
                .HasFilter("is_primary");
        });
    }

    /// <summary>The stored length of a hexadecimal SHA-256 digest.</summary>
    private const int HashedSecretLength = 64;

    /// <summary>The database's own definition of a stored digest, mirroring <c>HashedSecret</c>.</summary>
    private const string DigestPattern = "^[0-9a-f]{64}$";
}
