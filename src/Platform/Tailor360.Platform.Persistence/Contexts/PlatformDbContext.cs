using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tailor360.Platform.Persistence.Auditing;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Contexts;

/// <summary>
/// The Platform module's context: the outbox and inbox, idempotency, document sequences, the audit
/// trail, feature flags, job leases, worker heartbeats and the data-protection key ring. It owns the
/// <c>platform</c> schema and nothing else touches it directly.
/// </summary>
/// <param name="options">Context options.</param>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
    : ModuleDbContext(options, SchemaName), IDataProtectionKeyContext
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "platform";

    // The outbox and the inbox are inherited from ModuleDbContext, which maps them into whichever
    // schema the context owns — `platform` here, `customers` for Customers, and so on. They were
    // declared here until #77: one shared table on this context is a second connection and a second
    // transaction for every module that publishes, which is the one thing a transactional outbox
    // exists to rule out. The physical `platform.outbox_messages` and `platform.inbox_messages` are
    // unchanged; what changed is that every other module now has its own beside them.

    /// <summary>Recorded outcomes of idempotent commands.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>Gap-free document number sequences.</summary>
    public DbSet<SequenceRow> Sequences => Set<SequenceRow>();

    /// <summary>The append-only audit trail.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <summary>Feature flag values.</summary>
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    /// <summary>Leases held on scheduled jobs.</summary>
    public DbSet<JobLease> JobLeases => Set<JobLease>();

    /// <summary>Per-instance worker heartbeats.</summary>
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();

    /// <summary>
    /// The ASP.NET Core data-protection key ring. It lives here rather than on a container's disk
    /// because the images run with a read-only root file system and because a second web replica with
    /// its own ring would reject the first replica's anti-forgery tokens (Section 4.4). The rows are
    /// written and read by the framework, never by application code.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        // The outbox and the inbox are mapped by ModuleDbContext, for every module including this one.

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_keys");
            entity.HasKey(e => new { e.PrincipalId, e.Route, e.ClientKey });
            entity.Property(e => e.PrincipalId).HasMaxLength(128);
            entity.Property(e => e.Route).HasMaxLength(256);
            entity.Property(e => e.ClientKey).HasMaxLength(128);
            entity.Property(e => e.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_idempotency_keys_expires_at");
            entity.HasIndex(e => e.InFlightUntil)
                .HasDatabaseName("ix_idempotency_keys_in_flight")
                .HasFilter("status = 'in_progress'");
        });

        modelBuilder.Entity<SequenceRow>(entity =>
        {
            entity.ToTable("sequences");
            entity.HasKey(e => new { e.SequenceKey, e.Scope });
            entity.Property(e => e.SequenceKey).HasMaxLength(64);
            entity.Property(e => e.Scope).HasMaxLength(64);
        });

        // Shared with any module context whose own AuditWriter<TContext> needs to track an entry on the
        // same change tracker as the change it describes (issue #179) — see AuditEventMapping's remarks.
        AuditEventMapping.Configure(modelBuilder);

        modelBuilder.Entity<FeatureFlag>(entity =>
        {
            entity.ToTable("feature_flags");
            entity.HasKey(e => new { e.Key, e.ScopeType, e.ScopeId });
            entity.Property(e => e.Key).HasMaxLength(128);
            entity.Property(e => e.ScopeType).HasMaxLength(20);
            entity.Property(e => e.ScopeId).HasDefaultValue(Guid.Empty);
            entity.Property(e => e.Reason).HasMaxLength(500);
            UseRowVersion(entity);
        });

        modelBuilder.Entity<JobLease>(entity =>
        {
            entity.ToTable("job_leases");
            entity.HasKey(e => e.JobName);
            entity.Property(e => e.JobName).HasMaxLength(128);
            entity.Property(e => e.Owner).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<WorkerHeartbeat>(entity =>
        {
            entity.ToTable("worker_heartbeats");
            entity.HasKey(e => e.InstanceName);
            entity.Property(e => e.InstanceName).HasMaxLength(128);
            entity.Property(e => e.Version).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<DataProtectionKey>(entity =>
        {
            entity.ToTable("data_protection_keys");
            entity.HasKey(e => e.Id);

            // The framework's own mapping leaves both columns unbounded. Naming them explicitly keeps
            // the column names in this table's snake-case style and documents that the XML is the
            // key material: the application role may read and write it, and nothing else may.
            entity.Property(e => e.FriendlyName).HasColumnName("friendly_name");
            entity.Property(e => e.Xml).HasColumnName("xml").IsRequired();
        });
    }
}
