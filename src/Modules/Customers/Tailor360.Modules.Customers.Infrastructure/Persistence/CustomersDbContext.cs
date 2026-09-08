using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Naming;
using Tailor360.Modules.Customers.Domain.Preferences;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// The <c>customers</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// Which tables carry a concurrency token, and why:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>customers</c> <strong>does</strong>. It is the editable aggregate root: two people correcting
/// one record from two counters is the ordinary race, and the token is what turns the second one into
/// a 409 the screen can explain instead of a silent overwrite.
/// </description></item>
/// <item><description>
/// <c>customer_aliases</c> and <c>customer_branch_visibility</c> do <strong>not</strong>. Both are
/// append-only: an alias records something that was once true and a visibility row records that a
/// branch began serving the customer. Nothing updates either, so a token on them would only turn
/// ordinary reads into conflicts.
/// </description></item>
/// </list>
/// <para>
/// No column here is a foreign key into another schema. The branch and actor identifiers are bare
/// <c>uuid</c> columns with no <c>HasOne</c>: a key across the boundary would be an ARCH-005
/// violation, and names are resolved through Identity's published contracts instead.
/// </para>
/// </remarks>
/// <param name="options">The context options.</param>
public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "customers";

    /// <summary>Customer records.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>Previous names, spellings and merged numbers.</summary>
    public DbSet<CustomerAlias> CustomerAliases => Set<CustomerAlias>();

    /// <summary>Which branches see which records in ordinary search.</summary>
    public DbSet<CustomerBranchVisibility> CustomerBranchVisibility => Set<CustomerBranchVisibility>();

    /// <summary>The things a customer is asked to agree to. Configuration, seeded and Owner-maintained.</summary>
    public DbSet<ConsentPurpose> ConsentPurposes => Set<ConsentPurpose>();

    /// <summary>The published wording versions of each purpose.</summary>
    public DbSet<ConsentWording> ConsentWordings => Set<ConsentWording>();

    /// <summary>What customers said, when, and against which wording. Append-only.</summary>
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    /// <summary>How each customer wants to be reached.</summary>
    public DbSet<CommunicationPreferences> CommunicationPreferences => Set<CommunicationPreferences>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        ConfigureCustomers(modelBuilder);
        ConfigureAliases(modelBuilder);
        ConfigureVisibility(modelBuilder);
        ConfigureConsentPurposes(modelBuilder);
        ConfigureConsentRecords(modelBuilder);
        ConfigurePreferences(modelBuilder);
    }

    private static void ConfigureCustomers(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers", table =>
            {
                // E.164: a plus sign, a leading digit that is not zero, then seven to fourteen more.
                // Asserted here as well as in the domain because a repair script is not the domain.
                table.HasCheckConstraint(
                    "ck_customers_phone_is_e164", @"phone_e164 ~ '^\+[1-9][0-9]{7,14}$'");
                table.HasCheckConstraint(
                    "ck_customers_alternate_phone_is_e164",
                    @"alternate_phone_e164 IS NULL OR alternate_phone_e164 ~ '^\+[1-9][0-9]{7,14}$'");

                // A deactivated record has a date and an active one does not. The two halves of the
                // status are stored separately, so nothing but a constraint keeps them agreeing.
                table.HasCheckConstraint(
                    "ck_customers_deactivated_at_matches_status",
                    "(status = 'Deactivated') = (deactivated_at IS NOT NULL)");
            });

            entity.HasKey(e => e.Id);

            // Derived from the visibility rows; there is nothing to store.
            entity.Ignore(e => e.VisibilityBranchIds);

            entity.Property(e => e.CustomerNumber)
                .HasMaxLength(Customer.MaximumCustomerNumberLength).IsRequired();
            entity.Property(e => e.DisplayName)
                .HasMaxLength(CustomerDetails.MaximumDisplayNameLength).IsRequired();
            entity.Property(e => e.NormalisedName)
                .HasMaxLength(CustomerNameNormaliser.MaximumLength).IsRequired();
            entity.Property(e => e.NativeName)
                .HasMaxLength(CustomerDetails.MaximumNativeNameLength);
            entity.Property(e => e.PhoneE164)
                .HasMaxLength(TelephoneNumber.MaximumLength).IsRequired();
            entity.Property(e => e.PhoneLastSix)
                .HasMaxLength(TelephoneNumber.SearchTailLength).IsRequired();
            entity.Property(e => e.AlternatePhoneE164).HasMaxLength(TelephoneNumber.MaximumLength);
            entity.Property(e => e.AlternatePhoneLastSix)
                .HasMaxLength(TelephoneNumber.SearchTailLength);
            entity.Property(e => e.Email).HasMaxLength(CustomerDetails.MaximumEmailLength);
            entity.Property(e => e.AddressLine).HasMaxLength(CustomerDetails.MaximumAddressLineLength);
            entity.Property(e => e.Locality).HasMaxLength(CustomerDetails.MaximumLocalityLength);
            entity.Property(e => e.Postcode).HasMaxLength(CustomerDetails.MaximumPostcodeLength);
            entity.Property(e => e.Language)
                .HasMaxLength(CustomerDetails.MaximumLanguageLength).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // The display number is unique within the organisation and never reused. It is what a
            // customer reads off a card, so two records answering to one number is a real confusion.
            entity.HasIndex(e => new { e.OrganisationId, e.CustomerNumber })
                .IsUnique()
                .HasDatabaseName("ux_customers_organisation_customer_number");

            // The counter search: the tail of a telephone number, within the organisation.
            entity.HasIndex(e => new { e.OrganisationId, e.PhoneLastSix })
                .HasDatabaseName("ix_customers_phone_last_six");

            entity.HasIndex(e => new { e.OrganisationId, e.AlternatePhoneLastSix })
                .HasDatabaseName("ix_customers_alternate_phone_last_six")
                .HasFilter("alternate_phone_last_six IS NOT NULL");

            // Exact-match name search, and the leading edge of the trigram index the migration adds
            // by hand for the fuzzy one.
            entity.HasIndex(e => new { e.OrganisationId, e.NormalisedName })
                .HasDatabaseName("ix_customers_normalised_name");

            entity.HasIndex(e => new { e.OrganisationId, e.NativeName })
                .HasDatabaseName("ix_customers_native_name")
                .HasFilter("native_name IS NOT NULL");

            UseRowVersion(entity);
        });

    private static void ConfigureAliases(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerAlias>(entity =>
        {
            entity.ToTable("customer_aliases");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(e => e.Value)
                .HasMaxLength(CustomerAlias.MaximumValueLength).IsRequired();
            entity.Property(e => e.NormalisedValue)
                .HasMaxLength(CustomerAlias.MaximumValueLength).IsRequired();

            entity.HasOne<Customer>()
                .WithMany(customer => customer.Aliases)
                .HasForeignKey(alias => alias.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            // A search for a previous name has to find the surviving record.
            entity.HasIndex(e => e.NormalisedValue).HasDatabaseName("ix_customer_aliases_normalised_value");
        });

        // Without this EF cannot write through the read-only property and the aggregate silently
        // loses its children.
        modelBuilder.Entity<Customer>()
            .Navigation(customer => customer.Aliases)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureVisibility(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerBranchVisibility>(entity =>
        {
            entity.ToTable("customer_branch_visibility");

            // The pair is the key: a branch sees a record once, and the database says so rather than
            // the code hoping so.
            entity.HasKey(e => new { e.CustomerId, e.BranchId });

            entity.HasOne<Customer>()
                .WithMany(customer => customer.Visibility)
                .HasForeignKey(visibility => visibility.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            // "Which records does this branch see" is the search's own question, asked from the
            // branch's side.
            entity.HasIndex(e => e.BranchId).HasDatabaseName("ix_customer_branch_visibility_branch");
        });

        modelBuilder.Entity<Customer>()
            .Navigation(customer => customer.Visibility)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureConsentPurposes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsentPurpose>(entity =>
        {
            entity.ToTable("consent_purposes");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Key)
                .HasMaxLength(ConsentPurposeKeys.MaximumLength).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(ConsentPurpose.MaximumNameLength).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(ConsentPurpose.MaximumDescriptionLength);

            // Derived from the wording rows; there is nothing to store, and a stored copy is a second
            // place for the highest version to be wrong.
            entity.Ignore(e => e.CurrentWordingVersion);

            // One purpose per key, because a consent record names the key and two purposes answering
            // to one would make a record ambiguous about what was agreed.
            entity.HasIndex(e => new { e.OrganisationId, e.Key })
                .IsUnique()
                .HasDatabaseName("ux_consent_purposes_organisation_key");

            // Editable configuration: an Owner renames one or retires it, and two doing so at once is
            // the ordinary race.
            UseRowVersion(entity);
        });

        modelBuilder.Entity<ConsentWording>(entity =>
        {
            entity.ToTable("consent_wordings");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Text).HasMaxLength(ConsentWording.MaximumTextLength).IsRequired();

            entity.HasOne<ConsentPurpose>()
                .WithMany(purpose => purpose.Wordings)
                .HasForeignKey(wording => wording.PurposeId)
                .OnDelete(DeleteBehavior.Cascade);

            // The version a consent record names has to resolve to exactly one wording.
            entity.HasIndex(e => new { e.PurposeId, e.Version })
                .IsUnique()
                .HasDatabaseName("ux_consent_wordings_purpose_version");
        });

        modelBuilder.Entity<ConsentPurpose>()
            .Navigation(purpose => purpose.Wordings)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureConsentRecords(ModelBuilder modelBuilder)
        => modelBuilder.Entity<ConsentRecord>(entity =>
        {
            entity.ToTable("consent_records");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.PurposeKey)
                .HasMaxLength(ConsentPurposeKeys.MaximumLength).IsRequired();
            entity.Property(e => e.Decision).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Source)
                .HasMaxLength(ConsentRecord.MaximumSourceLength).IsRequired();

            // No foreign key to consent_purposes. A record names a key, and the key survives the
            // purpose being renamed in the register or removed from a later configuration — which is
            // the point of holding evidence rather than a pointer to current configuration.
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(record => record.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            // The query every send makes: the latest record for one customer and one purpose. Ordered
            // descending so the answer is the index's first row rather than a sort over the history.
            entity.HasIndex(e => new { e.CustomerId, e.PurposeKey, e.RecordedAt })
                .IsDescending(false, false, true)
                .HasDatabaseName("ix_consent_records_customer_purpose_recorded");

            // No concurrency token: the table is append-only, so there is nothing to overwrite. The
            // migration adds the trigger that makes that true of the database and not only of the
            // domain type.
        });

    private static void ConfigurePreferences(ModelBuilder modelBuilder)
        => modelBuilder.Entity<CommunicationPreferences>(entity =>
        {
            entity.ToTable("communication_preferences");

            // One row per customer, so the customer is the key rather than a surrogate beside it.
            entity.HasKey(e => e.CustomerId);

            entity.Property(e => e.Language)
                .HasMaxLength(CustomerDetails.MaximumLanguageLength).IsRequired();

            // Stored as the channel names rather than their ordinals, so that adding a channel to the
            // enumeration or reordering it cannot silently re-point every stored preference.
            entity.PrimitiveCollection(e => e.AllowedChannels)
                .HasColumnName("allowed_channels")
                .ElementType(element => element.HasConversion<string>().HasMaxLength(20))
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired();

            // Wall-clock times in the branch's timezone, so `time` and never `timestamptz` (BR-7).
            entity.ComplexProperty(e => e.QuietHours, quiet =>
            {
                quiet.IsRequired(false);
                quiet.Property(q => q.Start).HasColumnName("quiet_hours_start");
                quiet.Property(q => q.End).HasColumnName("quiet_hours_end");
            });

            entity.HasOne<Customer>()
                .WithOne()
                .HasForeignKey<CommunicationPreferences>(preferences => preferences.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Editable: a customer changes her mind, and two counters saving at once is the race.
            UseRowVersion(entity);
        });
}
