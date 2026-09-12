using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>
/// The Billing module's context: the <c>billing</c> schema and nothing else (ARCH-005).
/// </summary>
/// <remarks>
/// Tables arrive with the epic's slices. This slice (#145) maps the tax configuration versions with
/// their codes and components, and the GST registrations. Immutability of a published version's rows
/// is a database trigger written in the migration, because the application role must be unable to
/// change them even through a repair script.
/// </remarks>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The schema this module owns.</summary>
    public const string SchemaName = "billing";

    /// <summary>
    /// Exactly one published tax configuration version per organisation: a deferred exclusion
    /// constraint written in the migration, judged at commit so that retiring one version and
    /// publishing another in one save is one transition whatever order the two updates run in.
    /// </summary>
    public const string OnePublishedTaxConfigurationConstraint = "ux_tax_configuration_versions_one_published";

    /// <summary>Version numbers are unique per organisation.</summary>
    public const string TaxConfigurationNumberIndex = "ux_tax_configuration_versions_organisation_number";

    /// <summary>At most one registration of a branch is in force on any day.</summary>
    public const string OneRegistrationInForceConstraint = "ex_gst_registrations_one_in_force";

    /// <summary>The tax configuration versions.</summary>
    public DbSet<TaxConfigurationVersion> TaxConfigurationVersions => Set<TaxConfigurationVersion>();

    /// <summary>The tax codes of every version.</summary>
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();

    /// <summary>The GST registrations.</summary>
    public DbSet<GstRegistration> GstRegistrations => Set<GstRegistration>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        ConfigureTaxConfigurationVersions(modelBuilder);
        ConfigureTaxCodes(modelBuilder);
        ConfigureGstRegistrations(modelBuilder);
    }

    private static void ConfigureTaxConfigurationVersions(ModelBuilder modelBuilder)
        => modelBuilder.Entity<TaxConfigurationVersion>(entity =>
        {
            entity.ToTable("tax_configuration_versions", table =>
            {
                // The lifecycle written once, as the catalogue writes it: a draft has neither
                // timestamp, a published version has a publication, a retired one has both.
                table.HasCheckConstraint(
                    "ck_tax_configuration_versions_lifecycle_is_consistent",
                    """
                    (status = 0 AND published_at IS NULL AND retired_at IS NULL)
                    OR (status = 1 AND published_at IS NOT NULL AND retired_at IS NULL)
                    OR (status = 2 AND published_at IS NOT NULL AND retired_at IS NOT NULL)
                    """);
                table.HasCheckConstraint("ck_tax_configuration_versions_number_is_positive", "version_number >= 1");
            });

            entity.HasKey(version => version.Id);
            entity.Property(version => version.Name).HasMaxLength(TaxConfigurationVersion.MaximumNameLength).IsRequired();
            entity.Property(version => version.Notes).HasMaxLength(TaxConfigurationVersion.MaximumNotesLength);
            entity.Property(version => version.PublishReason).HasMaxLength(TaxConfigurationVersion.MaximumReasonLength);
            entity.Property(version => version.RetiredReason).HasMaxLength(TaxConfigurationVersion.MaximumReasonLength);
            entity.Property(version => version.Status).HasConversion<int>();

            entity.HasIndex(version => new { version.OrganisationId, version.VersionNumber })
                .IsUnique()
                .HasDatabaseName(TaxConfigurationNumberIndex);
            entity.HasIndex(version => new { version.OrganisationId, version.Status })
                .HasDatabaseName("ix_tax_configuration_versions_organisation_status");

            entity.HasMany(version => version.TaxCodes)
                .WithOne()
                .HasForeignKey(code => code.TaxConfigurationVersionId)
                // Named by hand: the conventional name exceeds PostgreSQL's 63-character limit.
                .HasConstraintName("fk_tax_codes_tax_configuration_versions")
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(version => version.TaxCodes).AutoInclude();

            UseRowVersion(entity);
        });

    private static void ConfigureTaxCodes(ModelBuilder modelBuilder)
        => modelBuilder.Entity<TaxCode>(entity =>
        {
            entity.ToTable("tax_codes", table =>
            {
                table.HasCheckConstraint("ck_tax_codes_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'");
                table.HasCheckConstraint("ck_tax_codes_classification_is_digits", "classification ~ '^[0-9]{4,8}$'");
            });

            entity.HasKey(code => code.Id);
            entity.Property(code => code.Key).HasColumnName("tax_code_key");
            entity.Property(code => code.Code).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Property(code => code.Description).HasMaxLength(TaxCodeDetails.MaximumDescriptionLength).IsRequired();
            entity.Property(code => code.Classification).HasMaxLength(TaxCodeDetails.MaximumClassificationLength).IsRequired();
            entity.Property(code => code.Kind).HasConversion<int>();
            entity.Ignore(code => code.Rates);
            entity.Ignore(code => code.Details);

            entity.HasIndex(code => new { code.TaxConfigurationVersionId, code.Code })
                .IsUnique()
                .HasDatabaseName("ux_tax_codes_version_code");
            entity.HasIndex(code => new { code.TaxConfigurationVersionId, code.Key })
                .IsUnique()
                .HasDatabaseName("ux_tax_codes_version_key");

            // Components are owned rows: one per component kind per code, and the rate is
            // numeric(6,3) — a percentage with three decimal places, per conventions section 1.1.
            entity.OwnsMany(code => code.Components, components =>
            {
                components.ToTable("tax_components", table => table.HasCheckConstraint(
                    "ck_tax_components_rate_is_a_percentage", "rate_percent >= 0 AND rate_percent <= 100"));
                components.WithOwner().HasForeignKey(component => component.TaxCodeId);
                components.HasKey(component => new { component.TaxCodeId, component.Kind });
                components.Property(component => component.Kind).HasConversion<int>();
                components.Property(component => component.RatePercent).HasPrecision(6, 3);
            });
        });

    private static void ConfigureGstRegistrations(ModelBuilder modelBuilder)
        => modelBuilder.Entity<GstRegistration>(entity =>
        {
            entity.ToTable("gst_registrations", table =>
            {
                table.HasCheckConstraint("ck_gst_registrations_gstin_shape", "gstin ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$'");
                table.HasCheckConstraint("ck_gst_registrations_state_code_shape", "state_code ~ '^[0-9]{2}$'");
                table.HasCheckConstraint(
                    "ck_gst_registrations_dates_are_ordered",
                    "effective_to IS NULL OR effective_to >= effective_from");
            });

            entity.HasKey(registration => registration.Id);
            entity.Property(registration => registration.Gstin).HasMaxLength(Gstin.Length).IsRequired();
            entity.Property(registration => registration.StateCode).HasMaxLength(2).IsRequired();
            entity.Property(registration => registration.LegalName).HasMaxLength(GstRegistrationDetails.MaximumNameLength).IsRequired();
            entity.Property(registration => registration.TradeName).HasMaxLength(GstRegistrationDetails.MaximumNameLength);
            entity.Ignore(registration => registration.Details);

            entity.HasIndex(registration => new { registration.OrganisationId, registration.BranchId, registration.EffectiveFrom })
                .HasDatabaseName("ix_gst_registrations_branch_effective_from");

            UseRowVersion(entity);
        });
}
