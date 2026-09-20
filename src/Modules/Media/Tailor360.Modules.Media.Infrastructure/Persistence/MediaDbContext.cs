using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Media.Domain.Media;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Modules.Media.Infrastructure.Persistence;

/// <summary>
/// The <c>media</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// Every table module-ownership.md §5.4 lists is mapped here in one migration, per §3's "first issue"
/// convention — but only <see cref="MediaObjects"/> and <see cref="QuarantineEntries"/> are written to
/// by issue #592's own code. <see cref="Derivatives"/>, <see cref="RetentionHolds"/> and
/// <see cref="AccessLog"/> exist so #593, #192 and #188 add only their own migration and code on top,
/// never a table this one should have created.
/// </para>
/// <para>
/// <c>media_objects</c> carries a concurrency token: its <see cref="MediaObject.Status"/> moves through
/// the pipeline under the worker's own writes (#593), and an administrator's delete request (#192) can
/// race the same row, the same way <c>catalog_versions</c>' publish and retire can race an edit.
/// </para>
/// <para>
/// No column here is a foreign key into another schema. <see cref="MediaObject.CustomerId"/>,
/// <see cref="MediaObject.OrderId"/> and <see cref="MediaObject.JobId"/> name rows owned by Customers
/// and Orders; a key across the boundary would be an ARCH-005 violation, and what resolves them instead
/// is each owning module's own read contract, the same convention <c>CatalogDbContext</c> follows for
/// the five links a service type carries.
/// </para>
/// </remarks>
/// <param name="options">The context options.</param>
public sealed class MediaDbContext(DbContextOptions<MediaDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "media";

    /// <summary>The name of the shared append-only trigger function every append-only table in this schema uses.</summary>
    public const string AppendOnlyFunctionName = "media.records_are_append_only";

    /// <summary>Every stored image, from upload to tombstone.</summary>
    public DbSet<MediaObject> MediaObjects => Set<MediaObject>();

    /// <summary>Objects awaiting the worker's validation and malware scan (#593).</summary>
    public DbSet<MediaQuarantineEntry> QuarantineEntries => Set<MediaQuarantineEntry>();

    /// <summary>Thumbnail and preview derivatives (#593).</summary>
    public DbSet<MediaDerivative> Derivatives => Set<MediaDerivative>();

    /// <summary>Legal and business holds that suspend retention deletion (#192).</summary>
    public DbSet<MediaRetentionHold> RetentionHolds => Set<MediaRetentionHold>();

    /// <summary>Who streamed which object, when (#188). Append-only.</summary>
    public DbSet<MediaAccessLogEntry> AccessLog => Set<MediaAccessLogEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        ConfigureMediaObjects(modelBuilder);
        ConfigureQuarantineEntries(modelBuilder);
        ConfigureDerivatives(modelBuilder);
        ConfigureRetentionHolds(modelBuilder);
        ConfigureAccessLog(modelBuilder);
    }

    private static void ConfigureMediaObjects(ModelBuilder modelBuilder)
        => modelBuilder.Entity<MediaObject>(entity =>
        {
            entity.ToTable("media_objects", table =>
            {
                table.HasCheckConstraint(
                    "ck_media_objects_size_is_positive", "size_bytes > 0");
                table.HasCheckConstraint(
                    "ck_media_objects_checksum_is_well_formed",
                    $"checksum ~ '^[0-9a-f]{{{MediaObject.ChecksumLength}}}$'");
                // Diagram and Illustration are purpose values 2 and 3 — see MediaPurpose.
                table.HasCheckConstraint(
                    "ck_media_objects_alt_text_required_for_diagrams",
                    "purpose NOT IN (2, 3) OR alt_text IS NOT NULL");
            });

            entity.HasKey(media => media.Id);

            entity.Property(media => media.Purpose).HasConversion<int>();
            entity.Property(media => media.Classification).HasConversion<int>();
            entity.Property(media => media.Status).HasConversion<int>();

            entity.Property(media => media.ObjectKey)
                .HasMaxLength(MediaObject.MaximumObjectKeyLength).IsRequired();
            entity.Property(media => media.ContentType)
                .HasMaxLength(MediaObject.MaximumContentTypeLength).IsRequired();
            entity.Property(media => media.Checksum)
                .HasMaxLength(MediaObject.ChecksumLength).IsFixedLength().IsRequired();
            entity.Property(media => media.AltText)
                .HasMaxLength(MediaObject.MaximumAltTextLength);

            // The object key is where every access is authorised from; two rows pointing at the same
            // key would mean two aggregates disagreeing about which one really owns those bytes.
            entity.HasIndex(media => media.ObjectKey)
                .IsUnique()
                .HasDatabaseName("ux_media_objects_object_key");

            entity.HasIndex(media => new { media.OrganisationId, media.Status })
                .HasDatabaseName("ix_media_objects_organisation_status");

            entity.HasIndex(media => media.CustomerId)
                .HasDatabaseName("ix_media_objects_customer");

            entity.HasIndex(media => media.JobId)
                .HasDatabaseName("ix_media_objects_job");

            // What the retention job (#192) sweeps by.
            entity.HasIndex(media => media.RetentionDate)
                .HasDatabaseName("ix_media_objects_retention_date");

            UseRowVersion(entity);
        });

    private static void ConfigureQuarantineEntries(ModelBuilder modelBuilder)
        => modelBuilder.Entity<MediaQuarantineEntry>(entity =>
        {
            entity.ToTable("media_quarantine", table => table.HasCheckConstraint(
                "ck_media_quarantine_attempt_count_is_not_negative", "attempt_count >= 0"));

            // The object's own id is this table's key: at most one entry tracks one object's journey
            // through quarantine, which is the whole reason the two are separate tables rather than
            // separate rows.
            entity.HasKey(entry => entry.MediaObjectId);

            entity.Property(entry => entry.QuarantineKey)
                .HasMaxLength(MediaObject.MaximumObjectKeyLength).IsRequired();
            entity.Property(entry => entry.ScanOutcome).HasConversion<int?>();
            entity.Property(entry => entry.LastError).HasMaxLength(2000);

            entity.HasOne<MediaObject>()
                .WithOne()
                .HasForeignKey<MediaQuarantineEntry>(entry => entry.MediaObjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureDerivatives(ModelBuilder modelBuilder)
        => modelBuilder.Entity<MediaDerivative>(entity =>
        {
            entity.ToTable("media_derivatives", table =>
            {
                table.HasCheckConstraint("ck_media_derivatives_size_is_positive", "size_bytes > 0");
                table.HasCheckConstraint(
                    "ck_media_derivatives_dimensions_are_positive",
                    "width_px > 0 AND height_px > 0");
            });

            entity.HasKey(derivative => derivative.Id);

            entity.Property(derivative => derivative.Variant).HasConversion<int>();
            entity.Property(derivative => derivative.ObjectKey)
                .HasMaxLength(MediaObject.MaximumObjectKeyLength).IsRequired();
            entity.Property(derivative => derivative.ContentType)
                .HasMaxLength(MediaObject.MaximumContentTypeLength).IsRequired();

            entity.HasIndex(derivative => derivative.ObjectKey)
                .IsUnique()
                .HasDatabaseName("ux_media_derivatives_object_key");

            entity.HasIndex(derivative => new { derivative.MediaObjectId, derivative.Variant })
                .IsUnique()
                .HasDatabaseName("ux_media_derivatives_object_variant");

            entity.HasOne<MediaObject>()
                .WithMany()
                .HasForeignKey(derivative => derivative.MediaObjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureRetentionHolds(ModelBuilder modelBuilder)
        => modelBuilder.Entity<MediaRetentionHold>(entity =>
        {
            entity.ToTable("media_retention_holds", table => table.HasCheckConstraint(
                "ck_media_retention_holds_release_is_ordered",
                "released_at IS NULL OR released_at >= placed_at"));

            entity.HasKey(hold => hold.Id);

            entity.Property(hold => hold.Reason).HasMaxLength(1000).IsRequired();

            // What the retention job checks before deleting: at most one open hold matters, and this is
            // the index that makes "is there an open hold" a lookup rather than a scan.
            entity.HasIndex(hold => new { hold.MediaObjectId, hold.ReleasedAt })
                .HasDatabaseName("ix_media_retention_holds_object_released");

            entity.HasOne<MediaObject>()
                .WithMany()
                .HasForeignKey(hold => hold.MediaObjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureAccessLog(ModelBuilder modelBuilder)
        => modelBuilder.Entity<MediaAccessLogEntry>(entity =>
        {
            entity.ToTable("media_access_log");

            entity.HasKey(entry => entry.Id);

            entity.Property(entry => entry.CorrelationId).HasMaxLength(64);
            entity.Property(entry => entry.Variant).HasConversion<int>();
            entity.Property(entry => entry.Outcome).HasConversion<int>();

            // No HasOne<MediaObject> — see this type's own remarks for why the log carries no foreign
            // key to the object it is about.
            entity.HasIndex(entry => new { entry.MediaObjectId, entry.AccessedAt })
                .HasDatabaseName("ix_media_access_log_object_accessed");
        });
}
