using Microsoft.EntityFrameworkCore;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Auditing;

/// <summary>
/// Maps the append-only audit trail's physical shape, shared by every context that can write to it.
/// </summary>
/// <remarks>
/// <para>
/// <c>PlatformDbContext</c> owns <c>platform.audit_events</c> and calls this to map it.
/// A module context whose own <c>AuditWriter&lt;TContext&gt;</c> needs to track an entry on the <em>same</em>
/// change tracker as the business change it describes (so the two commit or roll back together — issue
/// <see href="https://github.com/MK-AIFy/HyFib-Tailor360/issues/179">#179</see>) calls it too. Mapping the
/// table a second time is not a second module claiming the schema: the table is excluded from every
/// context's migrations, so only <c>Tailor360.Cli migrate</c>, against <see cref="Contexts.PlatformDbContext"/>,
/// ever creates or alters it (ARCH-005's "own schema" is about who <em>owns</em> a table, not who may
/// enlist a write in it through the one path — the same physical connection and transaction as the
/// change — that keeps G-2's guarantee true).
/// </para>
/// <para>
/// The physical table is created by explicit SQL rather than by the migration scaffolder, because it is
/// range-partitioned by month and carries the hash-chain and append-only triggers.
/// </para>
/// </remarks>
public static class AuditEventMapping
{
    /// <summary>The schema every context maps this table into, regardless of which schema it owns.</summary>
    public const string SchemaName = "platform";

    /// <summary>Configures <see cref="AuditEvent"/> on the given model.</summary>
    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events", SchemaName, t => t.ExcludeFromMigrations());

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
    }
}
