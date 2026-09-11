using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;
using Tailor360.Modules.Customers.Domain.Measurements;
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

    /// <summary>The irreversible merge decisions. Append-only.</summary>
    public DbSet<CustomerMerge> CustomerMerges => Set<CustomerMerge>();

    /// <summary>What people decided about scored duplicate suspicions.</summary>
    public DbSet<DuplicateCandidateDecision> DuplicateCandidates => Set<DuplicateCandidateDecision>();

    /// <summary>Generated subject-access exports, and the emptied rows of the ones that have gone.</summary>
    public DbSet<CustomerExport> CustomerExports => Set<CustomerExport>();

    /// <summary>The measurement templates, across every version of each.</summary>
    public DbSet<MeasurementTemplate> MeasurementTemplates => Set<MeasurementTemplate>();

    /// <summary>Every version of every template, draft to retired.</summary>
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();

    /// <summary>Every field of every version.</summary>
    public DbSet<TemplateField> TemplateFields => Set<TemplateField>();

    /// <summary>Garments being measured. Work in progress, shared within a branch, and expiring.</summary>
    public DbSet<MeasurementDraft> MeasurementDrafts => Set<MeasurementDraft>();

    /// <summary>What customers measured. Append-only, because INV-MSR-01 says a version is never edited.</summary>
    public DbSet<MeasurementVersion> MeasurementVersions => Set<MeasurementVersion>();

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
        ConfigureMerges(modelBuilder);
        ConfigureDuplicateCandidates(modelBuilder);
        ConfigureExports(modelBuilder);
        ConfigureMeasurementTemplates(modelBuilder);
        ConfigureMeasurementCapture(modelBuilder);
    }

    /// <summary>The index that holds "at most one published version per measurement template".</summary>
    /// <remarks>
    /// Named because <c>MeasurementTemplateStore</c> reads it off a failed write to tell two administrators
    /// publishing at once apart from every other unique violation.
    /// </remarks>
    public const string OnePublishedTemplateVersionIndex = "ux_template_versions_one_published";

    /// <summary>The unique index over a template's version numbers.</summary>
    public const string TemplateVersionNumberIndex = "ux_template_versions_template_number";

    /// <summary>The unique index over a template's code within its organisation.</summary>
    public const string TemplateCodeIndex = "ux_measurement_templates_organisation_code";

    /// <summary>The unique index over a customer's measurement version numbers for one template.</summary>
    /// <remarks>
    /// Named because <c>MeasurementCaptureStore</c> reads it off a failed write: two people confirming
    /// measurements for one customer in the same instant would otherwise both take the same number, and the
    /// number is what a person reads to say which measurement is the newer.
    /// </remarks>
    public const string MeasurementVersionNumberIndex = "ux_measurement_versions_customer_template_number";

    /// <summary>The index that keeps a branch to one open draft per customer and template.</summary>
    /// <remarks>
    /// Partial, on unconsumed rows: a branch measures one garment at a time against one template, and two open
    /// drafts would leave two people each filling in half of a different one. Every consumed draft is kept.
    /// </remarks>
    public const string OneOpenDraftIndex = "ux_measurement_drafts_one_open";

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

                // The pointer and its date are one fact stored in two columns, so a constraint is the
                // only thing keeping them agreeing.
                table.HasCheckConstraint(
                    "ck_customers_merged_into_is_consistent",
                    "(merged_into_customer_id IS NULL) = (merged_at IS NULL)");

                // A record merged into itself would be a cycle of length one, and every reader
                // following the pointer would loop.
                table.HasCheckConstraint(
                    "ck_customers_merged_into_is_not_self",
                    "merged_into_customer_id IS NULL OR merged_into_customer_id <> id");

                // A merged record is out of ordinary use, which is what takes it off the search. The
                // domain sets both together; this is what holds when somebody reaches the table.
                table.HasCheckConstraint(
                    "ck_customers_merged_into_is_deactivated",
                    "merged_into_customer_id IS NULL OR status = 'Deactivated'");
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

            // "Which records were merged into this one" — asked when a merge is followed by a second
            // merge and the earlier pointers have to be flattened. Filtered, because almost every row
            // in the table has no pointer at all.
            entity.HasIndex(e => e.MergedIntoCustomerId)
                .HasDatabaseName("ix_customers_merged_into")
                .HasFilter("merged_into_customer_id IS NOT NULL");

            // Self-referencing, and RESTRICT rather than the cascade the child tables use: a customer
            // is never deleted, and if one ever were, silently taking the records merged into it as
            // well is the last thing anybody would want.
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(customer => customer.MergedIntoCustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Derived from the pointer; there is nothing to store.
            entity.Ignore(e => e.IsMerged);

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

    private static void ConfigureMerges(ModelBuilder modelBuilder)
        => modelBuilder.Entity<CustomerMerge>(entity =>
        {
            entity.ToTable("customer_merges");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.MergedCustomerNumber)
                .HasMaxLength(Customer.MaximumCustomerNumberLength).IsRequired();

            // Nullable only so that the erasure workflow (#57) can redact free text a member of staff
            // typed about a person, without deleting the evidence that the merge happened. Every path
            // that writes one supplies it.
            entity.Property(e => e.Reason).HasMaxLength(CustomerMerge.MaximumReasonLength);

            // RESTRICT on both sides. A customer is never deleted; if one somehow were, losing the
            // record of why two people became one is not an acceptable consequence.
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(merge => merge.SurvivorCustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(merge => merge.MergedCustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // A record is folded in exactly once, and the database says so rather than the code
            // hoping so. It is also the last line of defence behind the row lock: two merges racing
            // for the same victim end with one conflict rather than two merge records.
            entity.HasIndex(e => e.MergedCustomerId)
                .IsUnique()
                .HasDatabaseName("ux_customer_merges_merged_customer");

            // "What has been merged into this record", which is the survivor's own history.
            entity.HasIndex(e => new { e.SurvivorCustomerId, e.MergedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_customer_merges_survivor_merged_at");

            // No concurrency token: the table is append-only and there is nothing to overwrite. The
            // migration adds the trigger that makes that true of the database and not only of the
            // domain type.
        });

    private static void ConfigureExports(ModelBuilder modelBuilder)
        => modelBuilder.Entity<CustomerExport>(entity =>
        {
            entity.ToTable("customer_exports", table =>
            {
                // The document and the two columns describing its absence are one fact in three
                // places. A row with a document must not claim to have been purged, and a purged row
                // must have said why — otherwise "is this copy still out there" cannot be answered by
                // looking.
                table.HasCheckConstraint(
                    "ck_customer_exports_purge_is_consistent",
                    "(purged_at IS NULL) = (purge_reason IS NULL) "
                    + "AND (purged_at IS NULL OR document IS NULL)");

                // An export that expired before it was generated could never be downloaded, and would
                // usually mean a clock or a configuration fault rather than an intention.
                table.HasCheckConstraint(
                    "ck_customer_exports_expires_after_generated",
                    "expires_at > generated_at");
            });

            entity.HasKey(e => e.Id);

            entity.Property(e => e.DocumentCode).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Classification).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(128).IsRequired();

            // Nullable for the one reason customer_merges.reason is: so #57 can redact free text a
            // member of staff typed about a named person without destroying the evidence that somebody
            // took a copy of that person's data. Every path that writes one supplies it.
            entity.Property(e => e.Reason).HasMaxLength(CustomerExport.MaximumReasonLength);

            // The copy itself, and the only nullable column that is nullable because it is *meant* to
            // go away. Everything around it survives the purge.
            entity.Property(e => e.Document);

            entity.Property(e => e.PurgeReason).HasConversion<string>().HasMaxLength(32);

            // RESTRICT: a customer is never deleted, and losing the record that their data was copied
            // out would be the wrong thing to lose first.
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(export => export.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // "The live exports for this customer", which is what supersession reads and what the
            // download re-reads. Partial, because a purged row is never the answer to that question
            // and the table only ever grows.
            entity.HasIndex(e => new { e.CustomerId, e.GeneratedAt })
                .IsDescending(false, true)
                .HasFilter("purged_at IS NULL")
                .HasDatabaseName("ix_customer_exports_live_by_customer");

            // "What has expired and still holds a copy", which is the cleanup job's whole query.
            entity.HasIndex(e => e.ExpiresAt)
                .HasFilter("purged_at IS NULL")
                .HasDatabaseName("ix_customer_exports_pending_purge");

            // Unlike the append-only evidence tables in this schema, this one is written twice after
            // it is created — once when somebody downloads it and once when the copy is destroyed —
            // and the two writers race by design: a download can arrive while the cleanup job is
            // emptying the row it names. Without the token the loser would win silently and increment
            // the download count of an export that no longer exists.
            UseRowVersion(entity);
        });

    private static void ConfigureDuplicateCandidates(ModelBuilder modelBuilder)
        => modelBuilder.Entity<DuplicateCandidateDecision>(entity =>
        {
            entity.ToTable("duplicate_candidates");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Confidence).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Decision).HasConversion<string>().HasMaxLength(30).IsRequired();

            // Stored as the reason names rather than their ordinals, so that adding a reason to the
            // enumeration or reordering it cannot silently re-explain a decision somebody already
            // took. The same choice the communication preferences made for channels.
            entity.PrimitiveCollection(e => e.Reasons)
                .HasColumnName("reasons")
                .ElementType(element => element.HasConversion<string>().HasMaxLength(40))
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired();

            // Cascade, unlike the merge record: this row says that two named people were once thought
            // to be one, which is an assertion about them rather than about the shop's own operations,
            // and #57 has to be able to remove it. Nothing deletes a customer today, so nothing
            // cascades today either.
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(decision => decision.SubjectCustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(decision => decision.CandidateCustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<CustomerMerge>()
                .WithMany()
                .HasForeignKey(decision => decision.MergeId)
                .OnDelete(DeleteBehavior.Restrict);

            // "What was decided about this record", newest first.
            entity.HasIndex(e => new { e.SubjectCustomerId, e.DecidedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_duplicate_candidates_subject_decided_at");

            // Deliberately no uniqueness over the pair. The same two records can be raised, judged
            // different people, raised again after a correction, and finally merged — four decisions
            // by four people on four days, each of which is evidence in its own right.
            //
            // No concurrency token either, and no append-only trigger: unlike a merge, these rows must
            // be removable outright by the erasure workflow.
        });

    private static void ConfigureMeasurementTemplates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MeasurementTemplate>(entity =>
        {
            entity.ToTable("measurement_templates");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Code).HasMaxLength(MeasurementTemplate.MaximumCodeLength).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(MeasurementTemplate.MaximumNameLength).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(MeasurementTemplate.MaximumDescriptionLength);

            // A catalogue service type points at a template by code in every document and by identifier in the
            // database, so two templates answering to one code would make the reference ambiguous.
            entity.HasIndex(e => new { e.OrganisationId, e.Code })
                .IsUnique()
                .HasDatabaseName(TemplateCodeIndex);

            UseRowVersion(entity);
        });

        modelBuilder.Entity<TemplateVersion>(entity =>
        {
            entity.ToTable("measurement_template_versions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).HasMaxLength(TemplateVersion.MaximumNameLength).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(TemplateVersion.MaximumNotesLength);
            entity.Property(e => e.PublishReason).HasMaxLength(TemplateVersion.MaximumReasonLength);
            entity.Property(e => e.RetiredReason).HasMaxLength(TemplateVersion.MaximumReasonLength);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.DefaultDisplayUnit).HasConversion<int>();

            entity.HasOne<MeasurementTemplate>()
                .WithMany(template => template.Versions)
                .HasForeignKey(version => version.MeasurementTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            // Version numbers are read and then written, so two administrators starting a draft at once can pick
            // the same one. The index settles it and the store turns the violation into a conflict.
            entity.HasIndex(e => new { e.MeasurementTemplateId, e.VersionNumber })
                .IsUnique()
                .HasDatabaseName(TemplateVersionNumberIndex);

            // At most one published version per template, held by the database rather than by a read-then-write in
            // the application: publishing is a race two administrators can enter at the same instant.
            entity.HasIndex(e => e.MeasurementTemplateId)
                .IsUnique()
                .HasFilter("status = 2")
                .HasDatabaseName(OnePublishedTemplateVersionIndex);

            UseRowVersion(entity);
        });

        modelBuilder.Entity<TemplateField>(entity =>
        {
            entity.ToTable("measurement_template_fields");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Key)
                .HasConversion(key => key.Value, value => FieldKey.Create(value).Value)
                .HasColumnName("field_key")
                .HasMaxLength(FieldKey.MaximumLength)
                .IsRequired();

            entity.Property(e => e.Label).HasMaxLength(TemplateField.MaximumLabelLength).IsRequired();
            entity.Property(e => e.LabelTamil).HasMaxLength(TemplateField.MaximumLabelLength);
            entity.Property(e => e.GroupName).HasMaxLength(TemplateField.MaximumGroupLength).IsRequired();
            entity.Property(e => e.HelpText).HasMaxLength(TemplateField.MaximumHelpTextLength).IsRequired();
            entity.Property(e => e.DiagramKey).HasMaxLength(TemplateField.MaximumDiagramKeyLength);
            entity.Property(e => e.DiagramAlt).HasMaxLength(TemplateField.MaximumDiagramAltLength);
            entity.Property(e => e.CanonicalUnit).HasConversion<int>();

            // Derived from the canonical unit and the precision, and from the sheet key plus the field key. Stored
            // copies would be two more places for a field and its description to disagree.
            entity.Ignore(e => e.DisplayUnits);
            entity.Ignore(e => e.DiagramReference);
            entity.Ignore(e => e.IsChoice);

            // Complex properties rather than owned entities, and the difference is not cosmetic. An owned
            // reference is something that can be absent, so Entity Framework decides it is absent when every one
            // of its properties holds the CLR default — which is exactly what a choice field's precision (0, 0)
            // and a choice field's bands (0, 0, null, null) look like. It then writes NULL into columns declared
            // NOT NULL and the insert fails. A complex property is part of the row and is never absent, which is
            // what a precision and a band actually are.
            entity.ComplexProperty(e => e.Precision, precision =>
            {
                precision.Property(p => p.InchFraction).HasColumnName("inch_fraction");
                precision.Property(p => p.CentimetreDecimals).HasColumnName("centimetre_decimals");
            });

            entity.ComplexProperty(e => e.Bands, bands =>
            {
                // numeric(8,2): the canonical millimetre, to the two decimals a sixteenth-inch step round-trips
                // through (docs/prd/measurement-templates.md section 2).
                bands.Property(b => b.MinimumMillimetres).HasColumnName("minimum_mm").HasPrecision(8, 2);
                bands.Property(b => b.MaximumMillimetres).HasColumnName("maximum_mm").HasPrecision(8, 2);
                bands.Property(b => b.WarnBelowMillimetres).HasColumnName("warn_below_mm").HasPrecision(8, 2);
                bands.Property(b => b.WarnAboveMillimetres).HasColumnName("warn_above_mm").HasPrecision(8, 2);
            });

            // The rule language is a small JSON document by design (plan blueprint for #27). Three tables for a
            // rule that is only ever read and written whole would buy nothing and cost a join per field.
            entity.Property(e => e.Rule)
                .HasConversion(
                    rule => MeasurementJson.Write(rule),
                    json => MeasurementJson.ReadRule(json))
                .HasColumnName("rule")
                .HasColumnType("jsonb");

            entity.Property(e => e.Options)
                .HasConversion(
                    options => MeasurementJson.Write(options),
                    json => MeasurementJson.ReadOptions(json),
                    new ValueComparer<IReadOnlyList<ChoiceOption>>(
                        (left, right) => left!.SequenceEqual(right!),
                        options => options.Aggregate(0, (hash, option) => HashCode.Combine(hash, option)),
                        options => options.ToList()))
                .HasColumnName("options")
                .HasColumnType("jsonb")
                .IsRequired();

            entity.HasOne<TemplateVersion>()
                .WithMany(version => version.Fields)
                .HasForeignKey(field => field.TemplateVersionId)
                .OnDelete(DeleteBehavior.Cascade);

            // A key identifies exactly one field within its version — the rule the domain enforces, held here too
            // because it is what captured values are filed under.
            entity.HasIndex(e => new { e.TemplateVersionId, e.Key })
                .IsUnique()
                .HasDatabaseName("ux_template_fields_version_key");
        });

        modelBuilder.Entity<MeasurementTemplate>()
            .Navigation(template => template.Versions)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        modelBuilder.Entity<TemplateVersion>()
            .Navigation(version => version.Fields)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
    /// <summary>
    /// The capture side: a draft being measured, and the version it becomes (issue #121).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The values are <strong>owned</strong> by each aggregate rather than shared between them. A value is only
    /// ever read through the thing that holds it, and one table with two owners would need a discriminator, a
    /// nullable key per owner, and a constraint to keep exactly one of them set — three ways to get wrong what
    /// two tables get right by construction.
    /// </para>
    /// <para>
    /// A draft carries the row version and a confirmed version does not. Drafts are shared within a branch and are
    /// written by two people at once; a confirmed version is written once and never again, so an optimistic token
    /// on it would guard against a second writer that the append-only trigger already makes impossible.
    /// </para>
    /// </remarks>
    private static void ConfigureMeasurementCapture(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MeasurementDraft>(entity =>
        {
            entity.ToTable("measurement_drafts", table => table.HasCheckConstraint(
                "ck_measurement_drafts_expires_after_it_started", "expires_at > started_at"));

            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.IsOpen);

            // One open draft per branch, customer and template. Every consumed one is kept, because it is the
            // other half of the record of how a measurement came to be taken.
            entity.HasIndex(e => new { e.BranchId, e.CustomerId, e.TemplateId })
                .IsUnique()
                .HasFilter("consumed_at IS NULL")
                .HasDatabaseName(OneOpenDraftIndex);

            // What the retention job sweeps by (INV-MSR-07), and what a counter's "where was I" list reads.
            entity.HasIndex(e => new { e.OrganisationId, e.ExpiresAt })
                .HasDatabaseName("ix_measurement_drafts_organisation_expiry");

            entity.OwnsMany(e => e.Values, values =>
            {
                values.ToTable("measurement_draft_values");
                ConfigureValues<MeasurementDraft>(values);
            });

            UseRowVersion(entity);
        });

        modelBuilder.Entity<MeasurementVersion>(entity =>
        {
            entity.ToTable("measurement_versions", table => table.HasCheckConstraint(
                "ck_measurement_versions_number_is_positive", "version_number >= 1"));

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Reason).HasMaxLength(MaximumMeasurementReasonLength);

            // Two people confirming for one customer in the same instant would otherwise both take the same
            // number, and the number is what a person reads to say which measurement is the newer.
            entity.HasIndex(e => new { e.CustomerId, e.TemplateId, e.VersionNumber })
                .IsUnique()
                .HasDatabaseName(MeasurementVersionNumberIndex);

            // The read behind a sheet and behind "which measurements does this customer have".
            entity.HasIndex(e => new { e.CustomerId, e.TemplateId, e.TakenAt })
                .HasDatabaseName("ix_measurement_versions_customer_template_taken");

            entity.OwnsMany(e => e.Values, values =>
            {
                values.ToTable("measurement_version_values");
                ConfigureValues<MeasurementVersion>(values);
            });
        });

        modelBuilder.Entity<MeasurementDraft>()
            .Navigation(draft => draft.Values)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        modelBuilder.Entity<MeasurementVersion>()
            .Navigation(version => version.Values)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }

    /// <summary>The columns a captured value has, wherever it is held.</summary>
    /// <remarks>
    /// <c>millimetres</c> is <c>decimal(12,3)</c>: three decimal places is what <c>UnitConversion</c> rounds a
    /// stored value to, and a wider column would hold digits no conversion can produce and no tape can read.
    /// </remarks>
    private static void ConfigureValues<TOwner>(
        OwnedNavigationBuilder<TOwner, MeasurementValue> values)
        where TOwner : class
    {
        values.WithOwner();
        values.Property(value => value.Key)
            .HasConversion(key => key.Value, value => FieldKey.Create(value).Value)
            .HasColumnName("field_key")
            .HasMaxLength(FieldKey.MaximumLength)
            .IsRequired();

        values.Property(value => value.Millimetres).HasColumnType("decimal(12,3)");
        values.Property(value => value.Choice).HasMaxLength(ChoiceOption.MaximumCodeLength);
        values.Property(value => value.EnteredUnit).HasConversion<int>();

        values.Ignore(value => value.IsChoice);

        // Exactly one half of a value is set. The domain refuses the other combinations; this is what holds when
        // somebody reaches the table, and it is the constraint that keeps a "measurement" from being neither.
        values.ToTable(table => table.HasCheckConstraint(
            $"ck_{table.Name}_is_measured_or_chosen",
            "(millimetres IS NULL) <> (choice IS NULL)"));
    }

    /// <summary>How long a reason for a measurement may be.</summary>
    public const int MaximumMeasurementReasonLength = 500;
}
