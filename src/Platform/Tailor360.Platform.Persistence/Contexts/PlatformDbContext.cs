using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>Integration events awaiting delivery.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Messages already handled, used to make redelivery a no-op.</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

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

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.Property(e => e.LeaseOwner).HasMaxLength(128);
            entity.Property(e => e.LastError).HasMaxLength(2000);

            // The dispatcher's claim query filters on unprocessed messages that are due; a partial index
            // keeps that query on a small index even once millions of delivered rows have accumulated.
            entity.HasIndex(e => new { e.AvailableAt, e.AggregateId })
                .HasDatabaseName("ix_outbox_messages_pending")
                .HasFilter("processed_at IS NULL AND dead_lettered_at IS NULL");

            entity.HasIndex(e => e.ProcessedAt).HasDatabaseName("ix_outbox_messages_processed_at");
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(e => new { e.MessageId, e.HandlerName });
            entity.Property(e => e.HandlerName).HasMaxLength(200);
        });

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
        });

        modelBuilder.Entity<SequenceRow>(entity =>
        {
            entity.ToTable("sequences");
            entity.HasKey(e => new { e.SequenceKey, e.Scope });
            entity.Property(e => e.SequenceKey).HasMaxLength(64);
            entity.Property(e => e.Scope).HasMaxLength(64);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            // The physical table is created by explicit SQL rather than by the migration scaffolder,
            // because it is range-partitioned by month and carries the hash-chain and append-only
            // triggers. EF Core still knows its shape so that it can be queried and written normally.
            entity.ToTable("audit_events", t => t.ExcludeFromMigrations());

            // PostgreSQL requires the partition key in every unique constraint, so the key is the
            // identity together with the month the entry falls in.
            entity.HasKey(e => new { e.Id, e.OccurredAt });
            entity.Property(e => e.Sequence).ValueGeneratedOnAdd();
            entity.Property(e => e.ActorDisplayName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(200).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.Property(e => e.Summary).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.Before).HasColumnType("jsonb");
            entity.Property(e => e.After).HasColumnType("jsonb");

            // Written by the chain trigger, never by the application.
            entity.Property(e => e.PreviousHash).HasMaxLength(64).ValueGeneratedOnAdd();
            entity.Property(e => e.Hash).HasMaxLength(64).ValueGeneratedOnAdd();

            entity.HasIndex(e => new { e.EntityType, e.EntityId }).HasDatabaseName("ix_audit_events_entity");
            entity.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_audit_events_occurred_at");
            entity.HasIndex(e => e.ActorId).HasDatabaseName("ix_audit_events_actor");
        });

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
