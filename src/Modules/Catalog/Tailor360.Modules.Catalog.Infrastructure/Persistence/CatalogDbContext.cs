using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The <c>catalog</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// Which tables carry a concurrency token, and why. Only <c>catalog_versions</c> does. It is the
/// aggregate root and the only row an administrator edits directly through a route that carries an
/// <c>If-Match</c>; every category and service type belongs to exactly one version, is only ever
/// changed through it, and is only ever changed while that version is a draft. A token on each of them
/// would turn ordinary reads of a tree into conflicts without protecting anything the root's token
/// does not already protect.
/// </para>
/// <para>
/// The cascade from a version to its categories and service types is real and deliberate: a draft that
/// is discarded takes its tree with it, and a published version is never deleted at all. What is
/// <em>not</em> a cascade is the link from a category to its parent — that is a self-reference inside
/// one version, restricted rather than cascading, because deleting a parent out from under its
/// children is the orphan the publish validation exists to refuse and the database should not perform
/// it quietly.
/// </para>
/// <para>
/// No column here is a foreign key into another schema. The five links a service type carries name
/// published versions of configuration owned by Customers, Orders and Billing, and they are bare
/// <c>uuid</c> and <c>text</c> columns with no <c>HasOne</c>: a key across the boundary would be an
/// ARCH-005 violation, and what checks them instead is each owning module's registered validator.
/// </para>
/// </remarks>
/// <param name="options">The context options.</param>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "catalog";

    /// <summary>
    /// The partial unique index that makes "exactly one published version" a fact about the database
    /// rather than a hope about the application.
    /// </summary>
    /// <remarks>
    /// Named as a constant because the store catches the unique violation it raises by name and turns
    /// it into a conflict a person can act on. Two administrators publishing different drafts in the
    /// same second is a race only the database can settle.
    /// </remarks>
    public const string OnePublishedVersionIndex = "ux_catalog_versions_one_published";

    /// <summary>
    /// The unique index over an organisation's catalogue version numbers.
    /// </summary>
    /// <remarks>
    /// Named because <c>CatalogStore</c> reads the name off a failed insert to tell "two administrators
    /// started a draft in the same moment" apart from every other unique violation. A literal in the
    /// catch filter would go stale the first time this mapping was renamed.
    /// </remarks>
    public const string VersionNumberIndex = "ux_catalog_versions_organisation_number";

    /// <summary>
    /// The partial unique index that makes "at most one open breach per version, code and target" a fact about the
    /// database rather than a hope about the reconciliation.
    /// </summary>
    /// <remarks>
    /// Named as a constant for the same reason the others here are: it is what makes a redelivered event a no-op,
    /// so a test asserts on it by name rather than on a literal that would go stale.
    /// </remarks>
    public const string OneOpenBreachIndex = "ux_reference_breaches_one_open";

    /// <summary>The catalogue versions, draft, published and retired.</summary>
    public DbSet<CatalogVersion> CatalogVersions => Set<CatalogVersion>();

    /// <summary>The categories of every version.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>The service types of every version.</summary>
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    /// <summary>
    /// Cross-module references of the published catalogue that stopped being valid after publication.
    /// </summary>
    /// <remarks>
    /// Written only by the reconciliation, and only from the delivery of an integration event, so a row commits
    /// with the inbox row that records the check ran (issue #91). It carries no personal data — a code, a path and
    /// a validator's own sentence about configuration.
    /// </remarks>
    public DbSet<CatalogReferenceBreach> ReferenceBreaches => Set<CatalogReferenceBreach>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        ConfigureVersions(modelBuilder);
        ConfigureCategories(modelBuilder);
        ConfigureServiceTypes(modelBuilder);
        ConfigureReferenceBreaches(modelBuilder);
    }

    private static void ConfigureVersions(ModelBuilder modelBuilder)
        => modelBuilder.Entity<CatalogVersion>(entity =>
        {
            entity.ToTable("catalog_versions", table =>
            {
                // The lifecycle, written once. The status and the two timestamps are three columns
                // holding one fact between them, and nothing but this keeps them agreeing: a draft has
                // neither timestamp, a published version has a publication, and a retired version has
                // both — because retirement is only ever reached from publication. Enumerated rather
                // than expressed as three separate implications, so that a fourth state added to the
                // enum without a migration fails here instead of quietly storing nonsense.
                table.HasCheckConstraint(
                    "ck_catalog_versions_lifecycle_is_consistent",
                    """
                    (status = 0 AND published_at IS NULL AND retired_at IS NULL)
                    OR (status = 1 AND published_at IS NOT NULL AND retired_at IS NULL)
                    OR (status = 2 AND published_at IS NOT NULL AND retired_at IS NOT NULL)
                    """);

                table.HasCheckConstraint(
                    "ck_catalog_versions_number_is_positive",
                    "version_number >= 1");
            });

            entity.HasKey(version => version.Id);

            entity.Property(version => version.Name)
                .HasMaxLength(CatalogVersion.MaximumNameLength).IsRequired();
            entity.Property(version => version.Notes)
                .HasMaxLength(CatalogVersion.MaximumNotesLength);
            entity.Property(version => version.PublishReason)
                .HasMaxLength(CatalogVersion.MaximumReasonLength);
            entity.Property(version => version.RetiredReason)
                .HasMaxLength(CatalogVersion.MaximumReasonLength);
            entity.Property(version => version.Status).HasConversion<int>();

            entity.HasIndex(version => new { version.OrganisationId, version.VersionNumber })
                .IsUnique()
                .HasDatabaseName(VersionNumberIndex);

            entity.HasIndex(version => new { version.OrganisationId, version.Status })
                .HasDatabaseName("ix_catalog_versions_organisation_status");

            // Section 7: exactly one published version is current at any time per organisation.
            entity.HasIndex(version => version.OrganisationId)
                .IsUnique()
                .HasFilter("status = 1")
                .HasDatabaseName(OnePublishedVersionIndex);

            entity.HasMany(version => version.Categories)
                .WithOne()
                .HasForeignKey(category => category.CatalogVersionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(version => version.ServiceTypes)
                .WithOne()
                .HasForeignKey(service => service.CatalogVersionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(version => version.Categories).AutoInclude();
            entity.Navigation(version => version.ServiceTypes).AutoInclude();

            UseRowVersion(entity);
        });

    private static void ConfigureCategories(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories", table =>
            {
                // Upper snake case, ASCII, beginning with a letter. Asserted here as well as in the
                // domain because a repair script is not the domain, and a code is what every price
                // list, report and export refers to.
                table.HasCheckConstraint(
                    "ck_categories_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]*$'");

                table.HasCheckConstraint(
                    "ck_categories_display_order_is_not_negative", "display_order >= 0");

                table.HasCheckConstraint(
                    "ck_categories_active_dates_are_ordered",
                    "active_from IS NULL OR active_to IS NULL OR active_to >= active_from");

                // A category that is its own parent is a cycle of length one, and every walk of the
                // tree would loop. Longer cycles are the publish validation's to find; this is the one
                // a single row can express.
                table.HasCheckConstraint(
                    "ck_categories_parent_is_not_self", "parent_id IS NULL OR parent_id <> id");
            });

            entity.HasKey(category => category.Id);

            entity.Property(category => category.Key).HasColumnName("category_key");
            entity.Property(category => category.Code)
                .HasMaxLength(CatalogCode.MaximumLength).IsRequired();
            entity.Property(category => category.Name)
                .HasMaxLength(CategoryDetails.MaximumNameLength).IsRequired();
            entity.Property(category => category.NameTamil)
                .HasMaxLength(CategoryDetails.MaximumNameLength);
            entity.Property(category => category.Description)
                .HasMaxLength(CategoryDetails.MaximumDescriptionLength);
            entity.Property(category => category.FeatureFlagKey)
                .HasMaxLength(CategoryDetails.MaximumFeatureFlagKeyLength);

            entity.Ignore(category => category.BranchIds);

            entity.HasIndex(category => new { category.CatalogVersionId, category.Code })
                .IsUnique()
                .HasDatabaseName("ux_categories_version_code");

            entity.HasIndex(category => new { category.CatalogVersionId, category.Key })
                .IsUnique()
                .HasDatabaseName("ux_categories_version_key");

            entity.HasIndex(category => category.ParentId)
                .HasDatabaseName("ix_categories_parent");

            // Restricted rather than cascading. Removing a parent while its children remain is the
            // orphan the publish validation refuses; the aggregate removes the subtree explicitly, and
            // the database refusing to do it silently is what keeps that the only way it happens.
            entity.HasOne<Category>()
                .WithMany()
                .HasForeignKey(category => category.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.OwnsMany(category => category.Branches, branches =>
            {
                branches.ToTable("category_branches");
                branches.WithOwner().HasForeignKey(branch => branch.CategoryId);
                branches.HasKey(branch => new { branch.CategoryId, branch.BranchId });
            });

            entity.Navigation(category => category.Branches).AutoInclude();
        });

    /// <summary>
    /// The reconciliation's answer, one row per distinct breach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The partial unique index is what makes a redelivered event a no-op rather than a second row: at most one
    /// <em>open</em> breach may exist per version, code and target, while every closed one is kept. A plain unique
    /// index over the three columns would refuse to reopen a breach that healed and recurred, which is the pattern
    /// most worth seeing.
    /// </para>
    /// <para>
    /// No foreign key to <c>catalog_versions</c>, even though both are in this schema. A breach outlives the
    /// interest in the version it is about — it is read as history — and a cascade from a version would delete the
    /// record of the exposure along with it.
    /// </para>
    /// <para>
    /// It carries neither the <c>created_by</c>/<c>updated_by</c> pair nor a row version, which every table an
    /// administrator edits does. Nobody edits this one: rows are written only by the reconciliation, from the
    /// delivery of an integration event, and there is no actor to record beyond the event that caused it — which
    /// is what <c>detected_because_of</c> is. Concurrent writers are settled by the outbox lease and by the partial
    /// unique index below, so an optimistic token would guard against a second writer that cannot exist.
    /// </para>
    /// </remarks>
    private static void ConfigureReferenceBreaches(ModelBuilder modelBuilder)
        => modelBuilder.Entity<CatalogReferenceBreach>(entity =>
        {
            entity.ToTable("reference_breaches", table => table.HasCheckConstraint(
                "ck_reference_breaches_resolution_is_ordered",
                "resolved_at IS NULL OR resolved_at >= detected_at"));

            entity.HasKey(breach => breach.Id);

            entity.Property(breach => breach.Code).HasMaxLength(200).IsRequired();
            entity.Property(breach => breach.Target).HasMaxLength(400).IsRequired();
            entity.Property(breach => breach.Message).HasMaxLength(2000).IsRequired();
            entity.Property(breach => breach.Validator).HasMaxLength(200).IsRequired();
            entity.Property(breach => breach.DetectedBecauseOf).HasMaxLength(200).IsRequired();
            entity.Property(breach => breach.ResolvedBecauseOf).HasMaxLength(200);

            entity.Ignore(breach => breach.IsOpen);

            entity.HasIndex(breach => new { breach.CatalogVersionId, breach.Code, breach.Target })
                .IsUnique()
                .HasFilter("resolved_at IS NULL")
                .HasDatabaseName(OneOpenBreachIndex);

            entity.HasIndex(breach => new { breach.OrganisationId, breach.ResolvedAt })
                .HasDatabaseName("ix_reference_breaches_organisation_resolved");
        });

    private static void ConfigureServiceTypes(ModelBuilder modelBuilder)
        => modelBuilder.Entity<ServiceType>(entity =>
        {
            entity.ToTable("service_types", table =>
            {
                table.HasCheckConstraint(
                    "ck_service_types_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]*$'");

                table.HasCheckConstraint(
                    "ck_service_types_display_order_is_not_negative", "display_order >= 0");

                table.HasCheckConstraint(
                    "ck_service_types_duration_is_in_range",
                    $"expected_duration_days BETWEEN 1 AND {ServiceTypeDetails.MaximumExpectedDurationDays}");

                table.HasCheckConstraint(
                    "ck_service_types_active_dates_are_ordered",
                    "active_from IS NULL OR active_to IS NULL OR active_to >= active_from");
            });

            entity.HasKey(service => service.Id);

            entity.Property(service => service.Key).HasColumnName("service_type_key");
            entity.Property(service => service.Code)
                .HasMaxLength(CatalogCode.MaximumLength).IsRequired();
            entity.Property(service => service.Name)
                .HasMaxLength(ServiceTypeDetails.MaximumNameLength).IsRequired();
            entity.Property(service => service.NameTamil)
                .HasMaxLength(ServiceTypeDetails.MaximumNameLength);
            entity.Property(service => service.Description)
                .HasMaxLength(ServiceTypeDetails.MaximumDescriptionLength);
            entity.Property(service => service.IntakeWarning)
                .HasMaxLength(ServiceTypeDetails.MaximumIntakeWarningLength);
            entity.Property(service => service.PriceListItemCode)
                .HasMaxLength(ServiceTypeDetails.MaximumPriceListItemCodeLength);

            entity.Ignore(service => service.BranchIds);
            entity.Ignore(service => service.DesignOptionGroupIds);
            entity.Ignore(service => service.HasEveryRequiredLink);
            entity.Ignore(service => service.NotOrderable);

            entity.HasIndex(service => new { service.CategoryId, service.Code })
                .IsUnique()
                .HasDatabaseName("ux_service_types_category_code");

            entity.HasIndex(service => new { service.CatalogVersionId, service.Key })
                .IsUnique()
                .HasDatabaseName("ux_service_types_version_key");

            // A service type belongs to a category of the same version, and goes when the category
            // does — which only ever happens while the version is a draft.
            entity.HasOne<Category>()
                .WithMany()
                .HasForeignKey(service => service.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.OwnsMany(service => service.Branches, branches =>
            {
                branches.ToTable("service_type_branches");
                branches.WithOwner().HasForeignKey(branch => branch.ServiceTypeId);
                branches.HasKey(branch => new { branch.ServiceTypeId, branch.BranchId });
            });

            entity.OwnsMany(service => service.DesignGroups, groups =>
            {
                groups.ToTable("service_type_design_groups");
                groups.WithOwner().HasForeignKey(group => group.ServiceTypeId);
                groups.HasKey(group => new { group.ServiceTypeId, group.DesignOptionGroupId });
            });

            entity.Navigation(service => service.Branches).AutoInclude();
            entity.Navigation(service => service.DesignGroups).AutoInclude();
        });
}
