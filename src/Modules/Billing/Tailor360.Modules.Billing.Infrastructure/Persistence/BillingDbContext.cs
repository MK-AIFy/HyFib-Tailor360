using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Pricing;
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

    /// <summary>Exactly one published version per price list; a deferred exclusion constraint in the migration.</summary>
    public const string OnePublishedPriceListVersionConstraint = "ux_price_list_versions_one_published";

    /// <summary>
    /// A branch is priced by at most one published version across every list. Kept by the migration's
    /// <c>published</c> column on the branch rows, which the version's own trigger maintains; the model does
    /// not map that column because nothing but the database ever writes it.
    /// </summary>
    public const string OnePublishedVersionPerBranchConstraint = "ex_price_list_version_branches_one_published";

    /// <summary>Price-list codes are unique per organisation.</summary>
    public const string PriceListCodeIndex = "ux_price_lists_organisation_code";

    /// <summary>Version numbers are unique per price list.</summary>
    public const string PriceListVersionNumberIndex = "ux_price_list_versions_list_number";

    /// <summary>The price lists.</summary>
    public DbSet<PriceList> PriceLists => Set<PriceList>();

    /// <summary>The price-list versions.</summary>
    public DbSet<PriceListVersion> PriceListVersions => Set<PriceListVersion>();

    /// <summary>The items of every version.</summary>
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();

    /// <summary>The discount rules of every version.</summary>
    public DbSet<DiscountRule> DiscountRules => Set<DiscountRule>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        ConfigureTaxConfigurationVersions(modelBuilder);
        ConfigureTaxCodes(modelBuilder);
        ConfigureGstRegistrations(modelBuilder);
        ConfigurePriceLists(modelBuilder);
        ConfigurePriceListVersions(modelBuilder);
        ConfigurePriceListItems(modelBuilder);
        ConfigureDiscountRules(modelBuilder);
    }

    private static void ConfigurePriceLists(ModelBuilder modelBuilder)
        => modelBuilder.Entity<PriceList>(entity =>
        {
            entity.ToTable("price_lists", table =>
                table.HasCheckConstraint("ck_price_lists_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'"));
            entity.HasKey(list => list.Id);
            entity.Property(list => list.Code).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Property(list => list.Name).HasMaxLength(PriceList.MaximumNameLength).IsRequired();
            entity.HasIndex(list => new { list.OrganisationId, list.Code })
                .IsUnique()
                .HasDatabaseName(PriceListCodeIndex);
            UseRowVersion(entity);
        });

    private static void ConfigurePriceListVersions(ModelBuilder modelBuilder)
        => modelBuilder.Entity<PriceListVersion>(entity =>
        {
            entity.ToTable("price_list_versions", table =>
            {
                table.HasCheckConstraint(
                    "ck_price_list_versions_lifecycle_is_consistent",
                    """
                    (status = 0 AND published_at IS NULL AND retired_at IS NULL)
                    OR (status = 1 AND published_at IS NOT NULL AND retired_at IS NULL)
                    OR (status = 2 AND published_at IS NOT NULL AND retired_at IS NOT NULL)
                    """);
                table.HasCheckConstraint("ck_price_list_versions_number_is_positive", "version_number >= 1");
                table.HasCheckConstraint(
                    "ck_price_list_versions_threshold_is_a_percentage",
                    "override_threshold_percent >= 0 AND override_threshold_percent <= 100");
            });

            entity.HasKey(version => version.Id);
            entity.Property(version => version.Name).HasMaxLength(PriceListVersionDetails.MaximumNameLength).IsRequired();
            entity.Property(version => version.Notes).HasMaxLength(PriceListVersionDetails.MaximumNotesLength);
            entity.Property(version => version.PublishReason).HasMaxLength(PriceListVersion.MaximumReasonLength);
            entity.Property(version => version.RetiredReason).HasMaxLength(PriceListVersion.MaximumReasonLength);
            entity.Property(version => version.Status).HasConversion<int>();
            entity.Property(version => version.RoundOff).HasConversion<int>();
            entity.Property(version => version.OverrideThresholdPercent).HasPrecision(6, 3);
            entity.Ignore(version => version.BranchIds);
            entity.Ignore(version => version.Details);

            entity.HasOne<PriceList>()
                .WithMany()
                .HasForeignKey(version => version.PriceListId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(version => new { version.PriceListId, version.VersionNumber })
                .IsUnique()
                .HasDatabaseName(PriceListVersionNumberIndex);
            entity.HasIndex(version => new { version.OrganisationId, version.Status })
                .HasDatabaseName("ix_price_list_versions_organisation_status");

            entity.OwnsMany(version => version.Branches, branches =>
            {
                branches.ToTable("price_list_version_branches");
                branches.WithOwner()
                    .HasForeignKey(branch => branch.PriceListVersionId)
                    // Named by hand: the conventional name exceeds PostgreSQL's 63-character limit.
                    .HasConstraintName("fk_price_list_version_branches_price_list_versions");
                branches.HasKey(branch => new { branch.PriceListVersionId, branch.BranchId });
            });
            entity.HasMany(version => version.Items)
                .WithOne()
                .HasForeignKey(item => item.PriceListVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(version => version.DiscountRules)
                .WithOne()
                .HasForeignKey(rule => rule.PriceListVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(version => version.Branches).AutoInclude();
            entity.Navigation(version => version.Items).AutoInclude();
            entity.Navigation(version => version.DiscountRules).AutoInclude();

            UseRowVersion(entity);
        });

    private static void ConfigurePriceListItems(ModelBuilder modelBuilder)
        => modelBuilder.Entity<PriceListItem>(entity =>
        {
            entity.ToTable("price_list_items", table =>
            {
                table.HasCheckConstraint("ck_price_list_items_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'");
                table.HasCheckConstraint("ck_price_list_items_rate_is_not_negative", "base_rate >= 0");
            });

            entity.HasKey(item => item.Id);
            entity.Property(item => item.Key).HasColumnName("price_list_item_key");
            entity.Property(item => item.Code).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(PriceListItemDetails.MaximumDescriptionLength).IsRequired();
            entity.Property(item => item.Kind).HasConversion<int>();
            // Unit rates are numeric(18,4): four decimal places so a single rounding step happens at the
            // end of a calculation (conventions section 1.1).
            entity.Property(item => item.BaseRate).HasPrecision(18, 4);
            entity.Property(item => item.Unit).HasMaxLength(PriceListItemDetails.MaximumUnitLength).IsRequired();
            entity.Property(item => item.TaxCode).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Ignore(item => item.Details);

            entity.HasIndex(item => new { item.PriceListVersionId, item.Code })
                .IsUnique()
                .HasDatabaseName("ux_price_list_items_version_code");
            entity.HasIndex(item => new { item.PriceListVersionId, item.Key })
                .IsUnique()
                .HasDatabaseName("ux_price_list_items_version_key");
        });

    private static void ConfigureDiscountRules(ModelBuilder modelBuilder)
        => modelBuilder.Entity<DiscountRule>(entity =>
        {
            entity.ToTable("discount_rules", table =>
            {
                table.HasCheckConstraint("ck_discount_rules_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'");
                table.HasCheckConstraint(
                    "ck_discount_rules_bounds_are_ordered",
                    "maximum_without_approval >= 0 AND maximum_without_approval <= maximum");
            });

            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.Key).HasColumnName("discount_rule_key");
            entity.Property(rule => rule.Code).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Property(rule => rule.Description).HasMaxLength(DiscountRuleDetails.MaximumDescriptionLength).IsRequired();
            entity.Property(rule => rule.Kind).HasConversion<int>();
            // One pair of columns serves both kinds: an amount for an Amount rule, a percentage for a
            // Percentage rule. Money precision (18,4) holds either without loss; the convention's (6,3) for a
            // percentage is a ceiling this column merely exceeds, and the domain caps a percentage at 100.
            entity.Property(rule => rule.MaximumWithoutApproval).HasPrecision(18, 4);
            entity.Property(rule => rule.Maximum).HasPrecision(18, 4);
            entity.Ignore(rule => rule.Details);

            entity.HasIndex(rule => new { rule.PriceListVersionId, rule.Code })
                .IsUnique()
                .HasDatabaseName("ux_discount_rules_version_code");
            entity.HasIndex(rule => new { rule.PriceListVersionId, rule.Key })
                .IsUnique()
                .HasDatabaseName("ux_discount_rules_version_key");
        });

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
