using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Domain.Estimates;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// The <c>orders</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// Ten tables, plus the <c>outbox_messages</c> and <c>inbox_messages</c> pair every module gets from
/// <see cref="ModuleDbContext"/>. Eight of the ten are the aggregate roots
/// <c>docs/architecture/module-ownership.md</c> section 5.5 names. The two beyond that list are the draft's
/// garment sections and their dependencies, which section 5.5 folds into <c>order_drafts</c> and which are rows
/// here rather than a document for four reasons given in the pull request — the decisive one being that 5.5 also
/// says a draft is "locked per garment section", and a per-section lock is a per-row <c>xmin</c>. Two tables
/// section 5.5 does name are deliberately absent, and section 5.5 is amended in the same pull request for both:
/// <c>price_snapshots</c>, because the garment job's frozen price copy is <c>price_…</c> columns on
/// <c>garment_jobs</c> for the reason <see cref="ConfigurePriceSnapshot"/> gives; and <c>job_ready_state</c>,
/// because the gate's outcome is three more columns on the same table for the reason
/// <see cref="ConfigureReadyState"/> gives — Entity Framework Core 10.0.11 cannot read an entity that is both
/// split across two tables and holds a complex property, and the price copy is a complex property.
/// </para>
/// <para>
/// Which tables carry a concurrency token, and why:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>order_drafts</c>, <c>order_draft_garments</c>, <c>estimates</c>, <c>orders</c> and
/// <c>garment_jobs</c> <strong>do</strong>. Each is edited after it is created — a draft by two counters at
/// once, an estimate by supersession and conversion, an order by revision and cancellation, a job by every
/// production command — and the token is what turns the second writer into a 409 the screen can explain instead
/// of a silent overwrite.
/// </description></item>
/// <item><description>
/// <c>order_revisions</c> and <c>job_dependencies</c> do <strong>not</strong>. Both are append-only, so there is
/// nothing to overwrite; the migration adds the triggers that make that true of the database and not only of the
/// domain type. <c>order_draft_garment_dependencies</c> does not either: the row is inserted and deleted, never
/// updated, so a token would guard nothing.
/// </description></item>
/// <item><description>
/// The gate's materialised ready state needs no entry of its own <strong>now that it is three columns on
/// <c>garment_jobs</c></strong> rather than a <c>job_ready_state</c> table — see
/// <see cref="ConfigureReadyState"/>. It is covered by that table's token, which is what makes the cover a
/// schema guarantee rather than the behavioural coincidence it was while the state lived on a split fragment:
/// there, a fragment-only update carried no concurrency predicate at all, and every writer of ready state was
/// protected only by happening to call <c>Touch</c> on the main fragment in the same batch.
/// </description></item>
/// </list>
/// <para>
/// <strong>No table here carries <c>created_at</c>/<c>created_by</c>, and every substitution is the same
/// decision.</strong> <c>src/Modules/CLAUDE.md</c> section 5 asks for the literal pair; each table names the
/// instant of its own creating business event instead, because that is what the Domain calls the property and
/// because the two are the same fact — <c>orders</c> and <c>garment_jobs</c> are created by a confirmation and
/// carry <c>confirmed_at</c>/<c>confirmed_by</c>, <c>estimates</c> by an issue and carry
/// <c>issued_at</c>/<c>issued_by</c>, <c>order_drafts</c> by a counter opening one and carry
/// <c>started_at</c>/<c>started_by</c>, <c>order_revisions</c> by a revision and carry
/// <c>recorded_at</c>/<c>recorded_by</c>, <c>job_dependencies</c> and
/// <c>order_draft_garment_dependencies</c> by a declaration and carry <c>declared_at</c>/<c>declared_by</c>, and
/// the two snapshot tables by the confirmation that froze them and carry <c>frozen_at</c>. A second pair beside
/// those would be a duplicate of the same instant that two writers could disagree about, and a reader asking
/// "when did this come into being" would have two columns to choose between. The precedent is
/// <c>customers.measurement_drafts</c>, which reads the same way. The <em>editable</em> half of the rule is not
/// substituted and is kept literally: every row anything rewrites carries <c>updated_at</c>/<c>updated_by</c>,
/// <c>estimates</c> included.
/// </para>
/// <para>
/// No column here is a foreign key into another schema. <c>customer_id</c>, <c>catalog_version_id</c>,
/// <c>measurement_version_id</c>, <c>design_selection_draft_id</c> and the media identifiers are bare
/// <c>uuid</c> columns with no <c>HasOne</c>: a key across the boundary would be an ARCH-005 violation, and the
/// rows behind them are read through their owners' published contracts instead.
/// </para>
/// <para>
/// <strong>Deliberately absent, recorded rather than silent.</strong> <c>workflow_definition_id</c>,
/// <c>workflow_version_id</c> and every <c>*_reason_code</c> have no foreign key and no value list: workflow
/// definitions, holds and cancellations are issues #33 and #34, and the hold, cancellation and defect
/// vocabularies are OD-10 configuration whose codes are opaque strings. There is no <c>priority</c> column,
/// notwithstanding the word in section 5.5's <c>orders</c> row: the Domain models none and priority has no
/// vocabulary, so a column would be inventing a product decision. Nothing forbids <c>status = 'Closed'</c>
/// either — SQ-01 is unsettled, nothing writes it, and a constraint added now would have to be contracted out at
/// N+2 when it lands.
/// </para>
/// </remarks>
/// <param name="options">The context options.</param>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "orders";

    /// <summary>The unique index over an organisation's order numbers (INV-ORD-03).</summary>
    /// <remarks>
    /// Named because <c>OrderStore</c> reads it off a failed write. The composed value carries the branch code
    /// and the financial year, so uniqueness of the string is uniqueness per branch per financial year.
    /// </remarks>
    public const string OrderNumberIndex = "ux_orders_organisation_number";

    /// <summary>The unique index over an organisation's estimate numbers (INV-ORD-03, INV-ORD-04).</summary>
    public const string EstimateNumberIndex = "ux_estimates_organisation_number";

    /// <summary>The unique index over an organisation's garment job numbers.</summary>
    public const string GarmentJobNumberIndex = "ux_garment_jobs_organisation_number";

    /// <summary>The partial index the delivery queue reads (state-transitions.md section 4.1).</summary>
    /// <remarks>
    /// Named because it is the one index whose shape changed when <c>job_ready_state</c> stopped being a table of
    /// its own — see <see cref="ConfigureReadyState"/> — and a name in one place is what keeps the migration and
    /// the model saying the same thing about it.
    /// </remarks>
    public const string GarmentJobReadyIndex = "ix_garment_jobs_ready";

    /// <summary>The unique index over an order's job indexes.</summary>
    /// <remarks>
    /// Contiguity from one to <c>n</c> stays in <c>Order.Confirm</c>, because a gap is a property of a set and
    /// not of a row. This holds the part that is a property of a row.
    /// </remarks>
    public const string GarmentJobOrderIndexIndex = "ux_garment_jobs_order_index";

    /// <summary>The unique index over an order's revision numbers.</summary>
    /// <remarks>
    /// Named because <c>OrderStore</c> reads it off a failed write: <c>Order.Revise</c> increments after a read,
    /// so two revisions racing take the same number, and the number is what a person reads to say which revision
    /// is the later.
    /// </remarks>
    public const string OrderRevisionNumberIndex = "ux_order_revisions_order_number";

    /// <summary>The index that keeps an estimate to at most one order.</summary>
    /// <remarks>
    /// <c>Estimate.Convert</c> refuses a second conversion; this is the half that holds under two confirmations
    /// arriving at the same instant.
    /// </remarks>
    public const string OrderEstimateIndex = "ux_orders_estimate";

    /// <summary>The primary key of a draft's garment sections.</summary>
    /// <remarks>
    /// Named because <c>OrderDraftStore</c> reads it off a failed write: <c>OrderDraft.AddGarment</c> refuses a
    /// reused identifier rather than treating it as a save, so the violation is a specific conflict and not a
    /// generic one.
    /// </remarks>
    public const string DraftGarmentKey = "pk_order_draft_garments";

    /// <summary>The unique index over a draft's garment positions.</summary>
    /// <remarks>
    /// <c>NextPosition()</c> is a read-then-write over the maximum, so two sections added at once take the same
    /// number. The index settles it and the store turns the violation into a concurrency conflict.
    /// </remarks>
    public const string DraftGarmentPositionIndex = "ux_order_draft_garments_draft_position";

    /// <summary>The unique index that is <c>OrdersErrors.DuplicateDependency</c> for confirmed jobs.</summary>
    public const string JobDependencyIndex = "ux_job_dependencies_job_prerequisite_kind";

    /// <summary>The drafts table.</summary>
    /// <remarks>
    /// Named for the same reason the indexes above are: <c>OrderStore</c> takes a row lock on this table with a
    /// statement Entity Framework cannot compose, and a table name written out in raw SQL is the one part of the
    /// confirmation with no compile-time tie to the model — a rename would pass the build, pass the model
    /// snapshot, and fail at runtime inside a transaction that had already been opened.
    /// </remarks>
    public const string OrderDraftsTable = "order_drafts";

    /// <summary>The estimates table. Named for the reason <see cref="OrderDraftsTable"/> gives.</summary>
    public const string EstimatesTable = "estimates";

    /// <summary>Orders being built at the counter. Work in progress, expiring, and never an obligation.</summary>
    public DbSet<OrderDraft> OrderDrafts => Set<OrderDraft>();

    /// <summary>Priced quotations issued from a draft.</summary>
    public DbSet<Estimate> Estimates => Set<Estimate>();

    /// <summary>Confirmed orders, with their garment jobs and their revision history.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>
    /// The garment jobs of every order.
    /// </summary>
    /// <remarks>
    /// Exposed in its own right rather than reached only through <see cref="Orders"/>, because
    /// <c>IOrderSnapshotQuery.GetJobAsync</c> and <c>GetJobsAsync</c> address jobs by their own identifiers and
    /// a projection that had to load the order first would pull an aggregate to answer a question about one row.
    /// </remarks>
    public DbSet<GarmentJob> GarmentJobs => Set<GarmentJob>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        ConfigureDrafts(modelBuilder);
        ConfigureDraftGarments(modelBuilder);
        ConfigureDraftDependencies(modelBuilder);
        ConfigureEstimates(modelBuilder);
        ConfigureOrders(modelBuilder);
        ConfigureRevisions(modelBuilder);
        ConfigureGarmentJobs(modelBuilder);
        ConfigureJobDependencies(modelBuilder);
        ConfigureReadyState(modelBuilder);
        ConfigureSnapshots(modelBuilder);
    }

    private static void ConfigureDrafts(ModelBuilder modelBuilder)
        => modelBuilder.Entity<OrderDraft>(entity =>
        {
            // A draft that expired before it was started could never be typed into, and would usually mean a
            // clock or a configuration fault rather than an intention. The precedent is
            // ck_measurement_drafts_expires_after_it_started, and the domain's own refusal is
            // OrdersErrors.DraftLifetimeNotPositive.
            entity.ToTable(OrderDraftsTable, table => table.HasCheckConstraint(
                "ck_order_drafts_expires_after_it_started", "expires_at > started_at"));

            entity.HasKey(e => e.Id);

            // Derived from consumed_at; there is nothing to store.
            entity.Ignore(e => e.IsOpen);

            entity.Property(e => e.Notes).HasMaxLength(OrderDraft.MaximumNotesLength);

            // What the retention sweep reads (the draft expires and is swept), the shape
            // ix_measurement_drafts_organisation_expiry already has.
            entity.HasIndex(e => new { e.OrganisationId, e.ExpiresAt })
                .HasDatabaseName("ix_order_drafts_organisation_expiry");

            // A counter's "where was I" list. Deliberately NOT unique: OrderDraft.Start declares no
            // one-open-draft-per-customer rule, unlike MeasurementDraft, and inventing one here would be a
            // product decision taken in a persistence file.
            entity.HasIndex(e => new { e.BranchId, e.CustomerId })
                .HasFilter("consumed_at IS NULL")
                .HasDatabaseName("ix_order_drafts_branch_customer");

            // The table carries started_at/started_by rather than created_at/created_by because that is what the
            // Domain names them, and customers.measurement_drafts already reads the same way.
            UseRowVersion(entity);
        });

    private static void ConfigureDraftGarments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderDraftGarment>(entity =>
        {
            // Both halves of MeasurementReuseNeedsVersion and MeasurementVersionNotExpected, in the shape
            // ck_customers_merged_into_is_consistent uses: an intent and its version are one fact stored in two
            // columns, so a constraint is the only thing keeping them agreeing.
            entity.ToTable("order_draft_garments", table => table.HasCheckConstraint(
                "ck_order_draft_garments_measurement_reuse",
                "(measurement_intent = 'ReuseVersion') = (measurement_version_id IS NOT NULL)"));

            entity.HasKey(e => e.Id).HasName(DraftGarmentKey);

            // Derived from the version pointer; there is nothing to store.
            entity.Ignore(e => e.HasMeasurementDecision);

            entity.Property(e => e.CategoryKey)
                .HasMaxLength(OrderDraftGarmentContent.MaximumKeyLength).IsRequired();
            entity.Property(e => e.ServiceTypeKey)
                .HasMaxLength(OrderDraftGarmentContent.MaximumKeyLength).IsRequired();
            entity.Property(e => e.Instructions)
                .HasMaxLength(OrderDraftGarmentContent.MaximumInstructionsLength);

            // Stored as the intent's name rather than its ordinal, so that adding a member to the enumeration or
            // reordering it cannot silently re-decide what Reception chose. The check constraint above reads the
            // name, which is only possible because of this.
            entity.Property(e => e.MeasurementIntent).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Order is meaningful — the order Reception attached the images in is the order the picker and the
            // job card render them in — and a PostgreSQL array keeps it for free where a child table needs an
            // explicit ordinal column. The list is read and written whole. Precedent:
            // customers.communication_preferences.allowed_channels.
            entity.PrimitiveCollection(e => e.ReferenceMediaIds)
                .HasColumnName("reference_media_ids")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired();

            entity.HasOne<OrderDraft>()
                .WithMany(draft => draft.Garments)
                .HasForeignKey(garment => garment.OrderDraftId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.OrderDraftId, e.Position })
                .IsUnique()
                .HasDatabaseName(DraftGarmentPositionIndex);

            // GarmentsWithoutMeasurements(), which is the source of the Measurements needed queue. Branch
            // scoping is a join to order_drafts, because branch_id is not a property of this Domain type and
            // denormalising it would be a second place to be wrong.
            entity.HasIndex(e => e.OrderDraftId)
                .HasFilter("measurement_version_id IS NULL")
                .HasDatabaseName("ix_order_draft_garments_unmeasured");

            // The per-section ETag section 5.5 calls for when it says a draft is locked per garment section.
            UseRowVersion(entity);
        });

        // Without this Entity Framework cannot write through the read-only property and the aggregate silently
        // loses its children.
        modelBuilder.Entity<OrderDraft>()
            .Navigation(draft => draft.Garments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureDraftDependencies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderDraftGarmentDependency>(entity =>
        {
            entity.ToTable("order_draft_garment_dependencies", table => table.HasCheckConstraint(
                "ck_order_draft_garment_dependencies_not_on_itself",
                "order_draft_garment_id <> prerequisite_order_draft_garment_id"));

            // The triple IS OrdersErrors.DuplicateDependency rather than a second rule beside it. The type has
            // no identity of its own, so a surrogate key would be inventing one.
            entity.HasKey(e => new { e.OrderDraftGarmentId, e.PrerequisiteOrderDraftGarmentId, e.Kind })
                .HasName("pk_order_draft_garment_dependencies");

            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(OrderDraftGarmentDependency.MaximumReasonLength);

            entity.HasOne<OrderDraftGarment>()
                .WithMany(garment => garment.Dependencies)
                .HasForeignKey(dependency => dependency.OrderDraftGarmentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cascade on the PREREQUISITE side too, and that asymmetry with job_dependencies is deliberate: it
            // is the database half of OrderDraft.RemoveGarment -> WithdrawDependenciesNaming, which the domain
            // today performs only in memory.
            entity.HasOne<OrderDraftGarment>()
                .WithMany()
                .HasForeignKey(dependency => dependency.PrerequisiteOrderDraftGarmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.PrerequisiteOrderDraftGarmentId)
                .HasDatabaseName("ix_order_draft_garment_dependencies_prerequisite");

            // No concurrency token: the row is inserted and deleted, never updated, so a token would guard
            // nothing.
        });

        modelBuilder.Entity<OrderDraftGarment>()
            .Navigation(garment => garment.Dependencies)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureEstimates(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Estimate>(entity =>
        {
            entity.ToTable(EstimatesTable, table =>
            {
                table.HasCheckConstraint(
                    "ck_estimates_validity_not_before_issue", "valid_until >= issued_on");

                // A status and its date are one fact in two columns, and the pointer is a third. The domain sets
                // them together; this is what holds when somebody reaches the table.
                table.HasCheckConstraint(
                    "ck_estimates_superseded_is_consistent",
                    "(status = 'Superseded') = (superseded_at IS NOT NULL) "
                    + "AND (superseded_at IS NULL) = (superseded_by_estimate_id IS NULL)");

                table.HasCheckConstraint(
                    "ck_estimates_converted_is_consistent",
                    "(status = 'Converted') = (converted_at IS NOT NULL) "
                    + "AND (converted_at IS NULL) = (converted_to_order_id IS NULL)");

                // An estimate superseded by itself would be a cycle of length one, and every reader following
                // the pointer would loop. The shape is ck_customers_merged_into_is_not_self.
                table.HasCheckConstraint(
                    "ck_estimates_superseded_by_is_not_self",
                    "superseded_by_estimate_id IS NULL OR superseded_by_estimate_id <> id");

                table.HasCheckConstraint(
                    "ck_estimates_artefact_is_consistent",
                    "(artefact_checksum IS NULL) = (artefact_recorded_at IS NULL)");

                foreach (var (name, sql) in PriceChecks("estimates", "totals_"))
                {
                    table.HasCheckConstraint(name, sql);
                }
            });

            entity.HasKey(e => e.Id);

            entity.Property(e => e.EstimateNumber)
                .HasConversion(number => number.Value, value => EstimateNumber.Parse(value).Value)
                .HasMaxLength(EstimateNumber.MaximumLength)
                .IsRequired();

            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.ArtefactChecksum).HasMaxLength(Estimate.MaximumChecksumLength);

            entity.ComplexProperty(e => e.Totals, totals => ConfigurePriceSnapshot(totals, "totals_"));

            // Self-referencing and RESTRICT: an estimate is a permanent business record, and if one somehow were
            // deleted, silently taking the estimate that superseded it as well is the last thing anybody would
            // want. No foreign key to order_drafts: a draft expires and is swept, while an estimate is permanent
            // evidence — the consent_records reasoning, that a record names a key and the key survives the thing
            // it named.
            entity.HasOne<Estimate>()
                .WithMany()
                .HasForeignKey(estimate => estimate.SupersededByEstimateId)
                .OnDelete(DeleteBehavior.Restrict);

            // No foreign key on converted_to_order_id either: it and orders.estimate_id would be a two-row
            // cycle. orders.estimate_id carries the key; this column is evidence of a decision.

            // INV-ORD-03, read through INV-ORD-04's separate sequence. The composed value carries the branch
            // code and the financial year, so uniqueness of the string is uniqueness per branch per year.
            entity.HasIndex(e => new { e.OrganisationId, e.EstimateNumber })
                .IsUnique()
                .HasDatabaseName(EstimateNumberIndex);

            // "Which estimates has this draft had", newest first — the supersede path's own question.
            entity.HasIndex(e => new { e.OrganisationId, e.OrderDraftId, e.IssuedAt })
                .IsDescending(false, false, true)
                .HasDatabaseName("ix_estimates_draft");

            // The ITimelineSource section 5.5 says Orders publishes.
            entity.HasIndex(e => e.CustomerId).HasDatabaseName("ix_estimates_customer");

            // Supersede, Convert and RecordArtefact all edit the row — which is also why it carries
            // updated_at/updated_by. A row with xmin and no updated_at is one the schema says is editable and
            // then cannot say when it was last edited; the only record would be platform.audit_events, and a
            // support call starts from the row.
            UseRowVersion(entity);
        });

    private static void ConfigureOrders(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders", table =>
            {
                table.HasCheckConstraint(
                    "ck_orders_revision_number_is_positive", "revision_number >= 1");

                table.HasCheckConstraint(
                    "ck_orders_cancelled_is_consistent",
                    "(status = 'Cancelled') = (cancelled_at IS NOT NULL) "
                    + "AND (cancelled_at IS NULL) = (cancellation_reason_code IS NULL) "
                    + "AND (cancelled_at IS NULL) = (cancellation_reason IS NULL)");

                // DELIBERATELY ABSENT: nothing ties production_started_at or delivered_at to the status.
                // RecomputeStatus sets both as latches — an accepted post-delivery alteration moves the status
                // back to in production, but the date the customer received their garments did not stop being
                // true — so a row with delivered_at set and status InProduction is correct, and a naive check
                // would fail it.
                foreach (var (name, sql) in PriceChecks("orders", "totals_"))
                {
                    table.HasCheckConstraint(name, sql);
                }
            });

            entity.HasKey(e => e.Id);

            // Derived from the jobs; there is nothing to store.
            entity.Ignore(e => e.HasEnteredProduction);

            entity.Property(e => e.OrderNumber)
                .HasConversion(number => number.Value, value => OrderNumber.Parse(value).Value)
                .HasMaxLength(OrderNumber.MaximumLength)
                .IsRequired();

            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(Order.MaximumNotesLength);
            entity.Property(e => e.CancellationReasonCode).HasMaxLength(Order.MaximumReasonCodeLength);
            entity.Property(e => e.CancellationReason).HasMaxLength(Order.MaximumReasonLength);

            entity.ComplexProperty(e => e.Totals, totals => ConfigurePriceSnapshot(totals, "totals_"));

            // RESTRICT: an estimate is a permanent business record, and Order.Confirm can refuse an empty
            // identifier but cannot check that the row exists.
            entity.HasOne<Estimate>()
                .WithMany()
                .HasForeignKey(order => order.EstimateId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.OrganisationId, e.OrderNumber })
                .IsUnique()
                .HasDatabaseName(OrderNumberIndex);

            // An estimate converts into at most one order.
            entity.HasIndex(e => e.EstimateId)
                .IsUnique()
                .HasFilter("estimate_id IS NOT NULL")
                .HasDatabaseName(OrderEstimateIndex);

            // The timeline source. Workboard and due-date indexes are deliberately not added here: the screens
            // that query them are #33 and #34, and an index with no query is a guess.
            entity.HasIndex(e => new { e.CustomerId, e.ConfirmedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_orders_customer");

            UseRowVersion(entity);
        });

    private static void ConfigureRevisions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderRevision>(entity =>
        {
            entity.ToTable("order_revisions", table =>
            {
                table.HasCheckConstraint(
                    "ck_order_revisions_number_is_positive", "revision_number >= 1");

                foreach (var (name, sql) in PriceChecks("order_revisions", "totals_"))
                {
                    table.HasCheckConstraint(name, sql);
                }
            });

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Reason).HasMaxLength(OrderRevision.MaximumReasonLength);

            entity.ComplexProperty(e => e.Totals, totals => ConfigurePriceSnapshot(totals, "totals_"));

            // No foreign key on superseded_estimate_id: it is evidence of what a revision superseded, not a
            // pointer to current configuration.
            entity.HasOne<Order>()
                .WithMany(order => order.Revisions)
                .HasForeignKey(revision => revision.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Order.Revise increments after a read, so two revisions racing take the same number. The index
            // settles it, and it doubles as the read index for an order's revision history in order.
            entity.HasIndex(e => new { e.OrderId, e.RevisionNumber })
                .IsUnique()
                .HasDatabaseName(OrderRevisionNumberIndex);

            // No concurrency token: the table is append-only, so there is nothing to overwrite. The migration
            // adds the trigger that makes that true of the database and not only of the domain type.
        });

        modelBuilder.Entity<Order>()
            .Navigation(order => order.Revisions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureGarmentJobs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GarmentJob>(entity =>
        {
            entity.ToTable("garment_jobs", table =>
            {
                table.HasCheckConstraint(
                    "ck_garment_jobs_job_index_is_positive", "job_index >= 1");

                // INV-JOB-02: StartProduction pins the workflow version and stamps the date together, and
                // nothing clears either.
                table.HasCheckConstraint(
                    "ck_garment_jobs_workflow_pin_is_consistent",
                    "(production_started_at IS NULL) = (workflow_version_id IS NULL)");

                // Read this one carefully: it is NOT tied to the status. GarmentJob.Cancel sets the status
                // without clearing HeldAt or HoldReasonCode, so a job cancelled off a hold legitimately carries
                // held_at with status 'Cancelled', and a check of the form
                // (status = 'OnHold') = (held_at IS NOT NULL) would fail on that path.
                table.HasCheckConstraint(
                    "ck_garment_jobs_hold_is_consistent",
                    "(held_at IS NOT NULL) = (hold_reason_code IS NOT NULL) "
                    + "AND (held_at IS NULL) = (hold_reason IS NULL)");

                table.HasCheckConstraint(
                    "ck_garment_jobs_cancelled_is_consistent",
                    "(status = 'Cancelled') = (cancelled_at IS NOT NULL) "
                    + "AND (cancelled_at IS NULL) = (cancellation_reason_code IS NULL)");

                // One direction only, and the other direction is deliberately absent for exactly the reason the
                // orders omission below gives. A delivered garment has a handover date: that half is a property
                // of this transition and nothing can make it false. The converse — a date implies the status —
                // is the naive shape the ConfigureOrders comment refuses one aggregate level up ("a row with
                // delivered_at set and status InProduction is correct, and a naive check would fail it"), and
                // asserting it here would have been the same mistake in a smaller place: issue #34's accepted
                // post-delivery alteration is precisely the path that comment describes, and the constraint
                // would then have to be contracted out over two releases (expand in N, contract no earlier than
                // N+2). The 'Closed' arm goes with it: while SQ-01 is unsettled nothing writes that status, and
                // the previous form quietly asserted that every closed garment had been delivered, which a
                // cancelled-then-closed one never was.
                table.HasCheckConstraint(
                    "ck_garment_jobs_delivered_is_consistent",
                    "status <> 'Delivered' OR delivered_at IS NOT NULL");

                foreach (var (name, sql) in PriceChecks("garment_jobs", "price_"))
                {
                    table.HasCheckConstraint(name, sql);
                }

                // NOTE, DELIBERATELY NOT BUILT: the partial unique index giving a confirmed job exactly one
                // ACTIVE barcode identity (INV-JOB-10 / CI-01 / INV-BID-02) belongs to Custody's schema and is
                // allocated through the confirmation-participant hook. Building it here would be ARCH-005.
            });

            entity.HasKey(e => e.Id);

            // All derived from the status and the dates; there is nothing to store.
            entity.Ignore(e => e.HasLeftConfirmed);
            entity.Ignore(e => e.HasEnteredProduction);
            entity.Ignore(e => e.IsDeliverable);

            entity.Property(e => e.JobNumber)
                .HasConversion(number => number.Value, value => GarmentJobNumber.Parse(value).Value)
                .HasMaxLength(GarmentJobNumber.MaximumLength)
                .IsRequired();

            entity.Property(e => e.CategoryKey).HasMaxLength(GarmentJob.MaximumKeyLength).IsRequired();
            entity.Property(e => e.ServiceTypeKey).HasMaxLength(GarmentJob.MaximumKeyLength).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // OD-10 vocabularies. The codes are opaque strings, so there is a length and nothing else.
            entity.Property(e => e.HoldReasonCode).HasMaxLength(GarmentJob.MaximumReasonCodeLength);
            entity.Property(e => e.HoldReason).HasMaxLength(GarmentJob.MaximumReasonLength);
            entity.Property(e => e.CancellationReasonCode).HasMaxLength(GarmentJob.MaximumReasonCodeLength);
            entity.Property(e => e.CancellationReason).HasMaxLength(GarmentJob.MaximumReasonLength);

            entity.PrimitiveCollection(e => e.ReferenceMediaIds)
                .HasColumnName("reference_media_ids")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired();

            entity.HasOne<Order>()
                .WithMany(order => order.Jobs)
                .HasForeignKey(job => job.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.OrganisationId, e.JobNumber })
                .IsUnique()
                .HasDatabaseName(GarmentJobNumberIndex);

            entity.HasIndex(e => new { e.OrderId, e.JobIndex })
                .IsUnique()
                .HasDatabaseName(GarmentJobOrderIndexIndex);

            // The due-soon and overdue sweeps behind orders.job-due-soon.v1 and orders.job-overdue.v1.
            entity.HasIndex(e => new { e.BranchId, e.DueDate })
                .HasFilter("status NOT IN ('Delivered', 'Closed', 'Cancelled')")
                .HasDatabaseName("ix_garment_jobs_due");

            UseRowVersion(entity);
        });

        modelBuilder.Entity<Order>()
            .Navigation(order => order.Jobs)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureJobDependencies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobDependency>(entity =>
        {
            entity.ToTable("job_dependencies", table => table.HasCheckConstraint(
                "ck_job_dependencies_not_on_itself",
                "garment_job_id <> prerequisite_garment_job_id"));

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(JobDependency.MaximumReasonLength);

            entity.HasOne<GarmentJob>()
                .WithMany(job => job.Dependencies)
                .HasForeignKey(dependency => dependency.GarmentJobId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT on the prerequisite side and CASCADE on the owning side, and the asymmetry is
            // deliberate: a job is never deleted, and if one ever were, silently erasing a DIFFERENT job's
            // promise is the last thing anybody would want. The draft table cascades on both sides instead,
            // because RemoveGarment really does withdraw them.
            entity.HasOne<GarmentJob>()
                .WithMany()
                .HasForeignKey(dependency => dependency.PrerequisiteGarmentJobId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.GarmentJobId, e.PrerequisiteGarmentJobId, e.Kind })
                .IsUnique()
                .HasDatabaseName(JobDependencyIndex);

            // The foreign key's own lookup, and the reverse direction DeliverTogetherSiblingsOf walks.
            entity.HasIndex(e => e.PrerequisiteGarmentJobId)
                .HasDatabaseName("ix_job_dependencies_prerequisite");

            // No concurrency token and no update path: GarmentJob publishes no withdraw and JobDependency no
            // mutator. INV-JOB-09 calls a dependency a promise the shop floor has been given, and the
            // migration's append-only trigger makes the database say so too.
        });

        modelBuilder.Entity<GarmentJob>()
            .Navigation(job => job.Dependencies)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    /// <summary>
    /// The two copies frozen at confirmation that have a table of their own (INV-JOB-01).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The collections inside them are <c>jsonb</c>, and the Domain's own shape decides that rather than a
    /// preference. <see cref="MeasurementSnapshot"/> and <see cref="DesignSnapshot"/> are sealed records with a
    /// private constructor, get-only properties and no parameterless constructor, and the collection is a
    /// <strong>constructor parameter</strong>. Entity Framework cannot supply a collection navigation as a
    /// constructor argument, so an owned collection mapped to a child table could not materialise these types at
    /// all; a converted property is a scalar and is constructor-bindable.
    /// </para>
    /// <para>
    /// The cost is recorded rather than glossed. <c>MeasuredValue</c>'s "both or neither" rule and its
    /// "a length is positive" rule lose their database half, which
    /// <c>customers.measurement_version_values</c> keeps as a row constraint, and so does
    /// <c>DesignSelection</c>'s illustration-and-alternative-text pair. <c>MeasuredValue.Create</c>,
    /// <c>DesignSelection.Create</c> and <c>DesignSnapshot.Create</c> are the only guards left, and
    /// <see cref="OrdersJson"/> puts every row back through them on the way out.
    /// </para>
    /// <para>
    /// <c>category_label</c> and <c>service_type_label</c> are columns rather than fields inside the selections
    /// document because <c>GarmentJobSnapshot.CategoryLabel</c> and <c>ServiceTypeLabel</c> are the only things
    /// <c>IOrderSnapshotQuery</c> reads out of this table.
    /// </para>
    /// <para>
    /// <strong>DELIBERATE OMISSION, recorded rather than silent.</strong> The rule that
    /// <c>design_snapshots.category_key</c> equals <c>garment_jobs.category_key</c> —
    /// <c>GarmentJobSpecification.Create</c> and <c>GarmentJob.CheckRevision</c> both enforce it, both citing
    /// INV-JOB-01 freezing the disagreement permanently — is not restated in the schema, because it spans two
    /// tables and would therefore be a per-row trigger doing a lookup on the confirmation's hottest loop.
    /// </para>
    /// <para>
    /// The measurement snapshot is <strong>Sensitive Personal</strong>;
    /// <c>docs/nfr/data-classification.md</c> section 5.4 already names "the measurement snapshot copied onto a
    /// garment job", so no new classification entry is needed.
    /// </para>
    /// </remarks>
    private static void ConfigureSnapshots(ModelBuilder modelBuilder)
        => modelBuilder.Entity<GarmentJob>(entity =>
        {
            entity.OwnsOne(e => e.Measurements, measurements =>
            {
                // The precedent is ck_measurement_versions_number_is_positive, on the same data.
                measurements.ToTable("measurement_snapshots", table => table.HasCheckConstraint(
                    "ck_measurement_snapshots_version_number_is_positive", "version_number >= 1"));

                measurements.WithOwner().HasForeignKey("GarmentJobId");
                measurements.Property<Guid>("GarmentJobId").HasColumnName("garment_job_id");

                // Every column is named here, and none of it is decoration. The properties of all three
                // snapshots are get-only — `{ get; }`, not `{ get; private set; }` — and Entity Framework
                // discovers a property by convention only when it has a setter. Left to convention these
                // columns simply would not exist, and the scaffolder would produce a measurement snapshot
                // holding nothing but its key and its values.
                //
                // No foreign key on measurement_version_id: Customers owns customers.measurement_versions, and
                // no module reads another module's tables. The column is provenance only (INV-JOB-01).
                measurements.Property(m => m.MeasurementVersionId).HasColumnName("measurement_version_id");
                measurements.Property(m => m.MeasurementTemplateId)
                    .HasColumnName("measurement_template_id").IsRequired();
                measurements.Property(m => m.TemplateVersionId)
                    .HasColumnName("template_version_id").IsRequired();
                measurements.Property(m => m.VersionNumber).HasColumnName("version_number").IsRequired();
                measurements.Property(m => m.TakenAt)
                    .HasColumnName("taken_at").HasColumnType("timestamptz").IsRequired();
                measurements.Property(m => m.TakenBy).HasColumnName("taken_by");
                measurements.Property(m => m.FrozenAt)
                    .HasColumnName("frozen_at").HasColumnType("timestamptz").IsRequired();

                measurements.Property(m => m.Values)
                    .HasConversion(
                        values => OrdersJson.Write(values),
                        json => OrdersJson.ReadValues(json),
                        new ValueComparer<IReadOnlyList<MeasuredValue>>(
                            (left, right) => left!.SequenceEqual(right!),
                            values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
                            values => values.ToList()))
                    .HasColumnName("values")
                    .HasColumnType("jsonb")
                    .IsRequired();
            });

            entity.Navigation(e => e.Measurements).IsRequired();

            entity.OwnsOne(e => e.Design, design =>
            {
                design.ToTable("design_snapshots");
                design.WithOwner().HasForeignKey("GarmentJobId");
                design.Property<Guid>("GarmentJobId").HasColumnName("garment_job_id");

                design.Property(d => d.CatalogVersionId).HasColumnName("catalog_version_id").IsRequired();
                design.Property(d => d.FrozenAt)
                    .HasColumnName("frozen_at").HasColumnType("timestamptz").IsRequired();

                design.Property(d => d.CategoryKey).HasMaxLength(DesignSnapshot.MaximumKeyLength).IsRequired();
                design.Property(d => d.CategoryLabel)
                    .HasMaxLength(DesignSnapshot.MaximumLabelLength).IsRequired();
                design.Property(d => d.ServiceTypeKey)
                    .HasMaxLength(DesignSnapshot.MaximumKeyLength).IsRequired();
                design.Property(d => d.ServiceTypeLabel)
                    .HasMaxLength(DesignSnapshot.MaximumLabelLength).IsRequired();
                design.Property(d => d.GarmentInstructions)
                    .HasMaxLength(DesignSnapshot.MaximumInstructionsLength);

                design.Property(d => d.Selections)
                    .HasConversion(
                        selections => OrdersJson.Write(selections),
                        json => OrdersJson.ReadSelections(json),
                        new ValueComparer<IReadOnlyList<DesignSelection>>(
                            (left, right) => left!.SequenceEqual(right!),
                            selections => selections.Aggregate(
                                0, (hash, selection) => HashCode.Combine(hash, selection)),
                            selections => selections.ToList()))
                    .HasColumnName("selections")
                    .HasColumnType("jsonb")
                    .IsRequired();

                // jsonb rather than varchar(500)[] so that both collections on this row take one converter
                // style, and neither depends on primitive-collection constructor binding.
                design.Property(d => d.ConditionalNotes)
                    .HasConversion(
                        notes => OrdersJson.Write(notes),
                        json => OrdersJson.ReadNotes(json),
                        new ValueComparer<IReadOnlyList<string>>(
                            (left, right) => left!.SequenceEqual(right!),
                            notes => notes.Aggregate(0, (hash, note) => HashCode.Combine(hash, note)),
                            notes => notes.ToList()))
                    .HasColumnName("conditional_notes")
                    .HasColumnType("jsonb")
                    .IsRequired();
            });

            entity.Navigation(e => e.Design).IsRequired();

            // The frozen price copy, inline with the job rather than in a table of its own. See
            // ConfigurePriceSnapshot for why it cannot be a table here, and the migration for the trigger that
            // keeps INV-JOB-01 over these columns now that a table-wide one is not available.
            entity.ComplexProperty(e => e.Price, price => ConfigurePriceSnapshot(price, "price_"));
        });

    /// <summary>
    /// The gate's materialised outcome and its reason codes, inline on <c>garment_jobs</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Three columns on <c>garment_jobs</c> and not the <c>job_ready_state</c> table section 5.5 named,
    /// because entity splitting and a complex property cannot both be on one entity type.</strong> This is a
    /// measured limit of Entity Framework Core 10.0.11, not a preference. A garment job carries its frozen price
    /// copy as a complex property (<see cref="ConfigurePriceSnapshot"/> says why it cannot be anything else), and
    /// with <c>SplitToTable("job_ready_state")</c> beside it every query that materialises a garment job or reads
    /// its price fails at query-compilation time with
    /// <c>InvalidOperationException: Sequence contains more than one element</c>, thrown by
    /// <c>SelectExpression.GenerateComplexPropertyShaperExpression</c> — it resolves a complex property to the
    /// single table the entity projects from, and a split entity projects from two. Writes would have succeeded
    /// and reads would have been impossible: <c>OrderStore.FindAsync</c> and
    /// <c>OrderSnapshotQuery.GetPricedAsync</c> both threw, so an order could be created and never read back, and
    /// every command touching a confirmed order — and Billing's invoice conversion across the module boundary —
    /// would have been a 500 rather than a <c>Result.Failure</c>.
    /// </para>
    /// <para>
    /// <strong>The alternatives were measured rather than assumed, and all of them cost more.</strong> Moving the
    /// complex property's columns into the fragment does not compile — <c>SplitTableBuilder&lt;T&gt;</c> has no
    /// <c>ComplexProperty</c>. Flattening <see cref="PriceSnapshot"/> to a complex property with no nesting, and
    /// mapping it with <c>ToJson()</c>, both throw the same exception: it is the presence of a complex property
    /// on a split entity that breaks, not its shape. An owned reference <em>is</em> compatible with entity
    /// splitting, and plain scalar columns are too — but neither can hold a <see cref="Money"/>, because
    /// <c>OwnedNavigationBuilder&lt;T, TDependent&gt;</c> has no <c>ComplexProperty</c> either and a two-column
    /// value object is not a scalar. Either one is therefore a change to <see cref="PriceSnapshot"/>, which
    /// <c>orders</c>, <c>estimates</c> and <c>order_revisions</c> map as a complex property as well, so the cost
    /// is a Domain type rewritten to carry eighteen scalars — or the same value object mapped two different ways
    /// in one schema, which <see cref="PriceChecks"/> exists to prevent.
    /// </para>
    /// <para>
    /// <strong>What the two readers actually query decides which side gives way.</strong> Billing reads the price
    /// through <c>IOrderSnapshotQuery.GetPricedAsync</c> and the price must stay a complex property, because the
    /// three <see cref="PriceChecks"/> and <c>orders.garment_job_price_is_immutable</c> are written over exactly
    /// these columns. The delivery queue reads ready state, and reads it as a projection — it never materialises
    /// a garment job — so it loses nothing by the move and gains the index: what was
    /// <c>ix_job_ready_state_ready</c> over the fragment's key, with branch scoping left to a join back to
    /// <c>garment_jobs</c>, is now <c>ix_garment_jobs_ready</c> over <c>branch_id</c> on the one table, and the
    /// join is gone. <c>docs/architecture/module-ownership.md</c> section 5.5 is amended in the same pull
    /// request, as section 9 of that document requires.
    /// </para>
    /// <para>
    /// <strong>INV-JOB-07 is not lost with the table.</strong> Its database half was
    /// <c>job_ready_state_is_gate_only</c>, refusing an insert that arrived already ready and refusing a delete;
    /// the migration carries the insert arm over to <c>garment_jobs</c> unchanged, because a garment job inserted
    /// already ready is the same defect on either table. The delete arm goes, and the migration says why: on the
    /// fragment it meant "the outcome row does not go without the job", and on <c>garment_jobs</c> the same
    /// trigger would refuse the cascade from <c>orders</c>. The rest of the single-writer property stays where
    /// <c>invariants.md</c> puts it, on the write path.
    /// </para>
    /// <para>
    /// The blocks are <c>jsonb</c> rather than a child table because of what the delivery queue actually
    /// queries. <c>docs/prd/state-transitions.md</c> section 4.1 gates the delivery-team receive scan on ready
    /// state alone, which <c>ix_garment_jobs_ready</c> serves; the blocks are never a predicate and exist to
    /// be shown beside a job that is not ready. They are replaced wholesale on every write, on the hottest path
    /// in the module — recomputed on every workflow, QC, hold, dependency and custody event — and a child table
    /// would turn each of those into a delete plus <c>n</c> inserts instead of one update. A document also keeps
    /// the predicate order the gate deliberately builds, which a child table would need an ordinal column to
    /// hold. The precedent is <c>customers.measurement_template_fields.options</c>.
    /// </para>
    /// <para>
    /// <strong>There is no <c>bound_with</c> column, and that is a finding rather than an omission.</strong>
    /// <c>GarmentJob</c> has no such property: <c>RecordReadyState</c> takes a flag, the blocks and an instant,
    /// and the parcel is not one of them, so <c>ReadyGateOutcome.BoundWith</c> is a per-evaluation output that
    /// <c>Order.ParcelPresented</c> consumes and discards. Storing it would persist a property no Domain type
    /// exposes, and it would be a second, staleable copy of a relation <c>job_dependencies</c> already holds —
    /// SQ-09's interim position is that a hold closes one member's gate and not its partners', so a stored
    /// parcel would be wrong the moment one member is held. The parcel is re-derived at the door by
    /// <c>Order.DeliverTogetherParcelOf</c>.
    /// </para>
    /// <para>
    /// The two checks — a ready row carries no blocks, and nothing is ready without a recorded evaluation — and
    /// the index that serves the delivery queue are declared here rather than by hand in the migration, which is
    /// the one thing the move makes simpler: they belong on <c>garment_jobs</c> now, which is where Entity
    /// Framework puts an entity's checks and indexes anyway.
    /// </para>
    /// </remarks>
    private static void ConfigureReadyState(ModelBuilder modelBuilder)
        => modelBuilder.Entity<GarmentJob>(entity =>
        {
            entity.Property(e => e.ReadyStateBlocks)
                .HasConversion(
                    blocks => OrdersJson.Write(blocks),
                    json => OrdersJson.ReadBlocks(json),
                    new ValueComparer<IReadOnlyCollection<ReadyGateBlock>>(
                        (left, right) => left!.SequenceEqual(right!),
                        blocks => blocks.Aggregate(0, (hash, block) => HashCode.Combine(hash, block)),
                        blocks => blocks.ToList()))
                .HasColumnType("jsonb")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired();

            entity.ToTable("garment_jobs", table =>
            {
                // ReadyGateOutcome.Of defines "ready" as exactly "no blocks", so a ready row carrying a reason
                // is a contradiction; and nothing is ready without a recorded evaluation.
                table.HasCheckConstraint(
                    "ck_garment_jobs_ready_has_no_blocks",
                    "is_ready_for_delivery = false OR jsonb_array_length(ready_state_blocks) = 0");

                table.HasCheckConstraint(
                    "ck_garment_jobs_ready_has_been_computed",
                    "is_ready_for_delivery = false OR ready_state_computed_at IS NOT NULL");
            });

            // The delivery queue's whole predicate (docs/prd/state-transitions.md section 4.1). Over branch_id
            // because branch scoping used to be the join to garment_jobs and there is no join left to make.
            entity.HasIndex(e => e.BranchId)
                .HasFilter("is_ready_for_delivery")
                .HasDatabaseName(GarmentJobReadyIndex);
        });

    /// <summary>The nine amounts a <see cref="PriceSnapshot"/> carries, in the order the document prints them.</summary>
    private static readonly string[] PriceAmountColumns =
    [
        "subtotal",
        "discount_total",
        "taxable_value",
        "central_tax",
        "state_tax",
        "integrated_tax",
        "cess",
        "round_off",
        "grand_total",
    ];

    /// <summary>
    /// The three checks a stored <see cref="PriceSnapshot"/> carries, wherever it is held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defined once and applied verbatim to the <c>price_</c> prefix on <c>garment_jobs</c> and the
    /// <c>totals_</c> prefix on <c>orders</c>, <c>order_revisions</c> and <c>estimates</c>, because the same
    /// value object is stored in four places and a rule restated four times is a rule that will eventually
    /// differ in one of them.
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// Every amount shares the subtotal's currency, restating <c>PriceSnapshot.Create</c>'s
    /// <c>CurrencyMismatch</c>.
    /// </description></item>
    /// <item><description>
    /// No amount is negative <strong>except the round-off</strong>, which is the one amount the domain permits
    /// to be negative — the reason <c>RoundOff</c> is deliberately absent from the domain's own negative check.
    /// </description></item>
    /// <item><description>
    /// One tax scheme. INV-INV-04 / <c>BothTaxSchemesPresent</c>: a document carrying CGST or SGST beside IGST
    /// claims the supply was both within the state and between states.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <param name="table">The table the constraints are named for.</param>
    /// <param name="prefix">The column prefix, empty for a table that holds nothing else.</param>
    /// <returns>The constraint name and its expression, three times.</returns>
    private static List<(string Name, string Sql)> PriceChecks(string table, string prefix)
    {
        var subtotalCurrency = $"{prefix}subtotal_currency";

        var shared = string.Join(
            " AND ",
            PriceAmountColumns.Skip(1).Select(name => $"{prefix}{name}_currency = {subtotalCurrency}"));

        var notNegative = string.Join(
            " AND ",
            PriceAmountColumns
                .Where(name => name is not "round_off")
                .Select(name => $"{prefix}{name}_amount >= 0"));

        var oneScheme =
            $"NOT ({prefix}integrated_tax_amount <> 0 "
            + $"AND ({prefix}central_tax_amount <> 0 OR {prefix}state_tax_amount <> 0))";

        return
        [
            ($"ck_{table}_price_currency_is_shared", shared),
            ($"ck_{table}_price_amounts_not_negative", notNegative),
            ($"ck_{table}_price_one_tax_scheme", oneScheme),
        ];
    }

    /// <summary>The columns a frozen price copy has, wherever it is held.</summary>
    /// <remarks>
    /// <para>
    /// The three version identifiers are INV-ORD-02's provenance. Each <see cref="Money"/> becomes a nested
    /// complex property — a pair of columns that is part of the row and never absent — for the reason
    /// <c>CustomersDbContext</c> gives for a precision and a band: an owned reference is something Entity
    /// Framework decides is absent when every one of its properties holds the CLR default, and a zero amount is
    /// exactly that. The nine currency columns are not collapsed into one, because Entity Framework cannot map
    /// nine complex properties onto a shared column, and dropping the currency to a convention would be deciding
    /// multi-currency, which no document does.
    /// </para>
    /// <para>
    /// <strong>Every property is named here, and none of it is decoration.</strong> A
    /// <see cref="PriceSnapshot"/>'s properties are get-only — <c>{ get; }</c>, not
    /// <c>{ get; private set; }</c> — and Entity Framework discovers a property by convention only when it has a
    /// setter. Left to convention these columns would simply not exist.
    /// </para>
    /// <para>
    /// <strong>A price copy is a complex property and therefore inline, and that is forced rather than
    /// chosen.</strong> <see cref="Money"/> is a <c>readonly record struct</c>, so it cannot be an owned type —
    /// owned types are entity types and entity types are reference types — and a complex type cannot be nested
    /// inside an owned one: <c>OwnedNavigationBuilder</c> has no <c>ComplexProperty</c>, and adding one through
    /// the metadata API produces a model that runs but a model snapshot that does not compile, because the
    /// snapshot generator emits the call that does not exist. Only a table an owned type is mapped to can be a
    /// table of its own, so the garment job's copy lives in <c>price_</c> columns on <c>garment_jobs</c> rather
    /// than in a <c>price_snapshots</c> table. INV-JOB-01 is not lost with the table: the migration carries a
    /// trigger over exactly these columns, and <c>docs/architecture/module-ownership.md</c> section 5.5 is
    /// amended in the same pull request.
    /// </para>
    /// <para>
    /// <strong>Being a complex property is what decides the rest of the garment job's mapping, and it is the
    /// reason the job carries no split table.</strong> A complex property anywhere on an entity is incompatible
    /// with entity splitting on Entity Framework Core 10.0.11 — every read of that entity throws at
    /// query-compilation time. <see cref="ConfigureReadyState"/> records the measurement and why this side is the
    /// one that could not give way.
    /// </para>
    /// </remarks>
    /// <param name="price">The complex-property builder.</param>
    /// <param name="prefix">The column prefix — <c>totals_</c> on a document, <c>price_</c> on a job.</param>
    private static void ConfigurePriceSnapshot(ComplexPropertyBuilder<PriceSnapshot> price, string prefix)
    {
        // A price copy is never absent: an order, an estimate, a revision and a job all carry one from the
        // moment they exist, so every column below is NOT NULL.
        price.IsRequired();

        price.Property(p => p.CatalogVersionId).HasColumnName($"{prefix}catalog_version_id");
        price.Property(p => p.PriceListVersionId).HasColumnName($"{prefix}price_list_version_id");
        price.Property(p => p.TaxConfigurationVersionId)
            .HasColumnName($"{prefix}tax_configuration_version_id");
        price.Property(p => p.CalculatedAt)
            .HasColumnName($"{prefix}calculated_at").HasColumnType("timestamptz");

        ConfigureMoney(price.ComplexProperty(p => p.Subtotal), $"{prefix}subtotal");
        ConfigureMoney(price.ComplexProperty(p => p.DiscountTotal), $"{prefix}discount_total");
        ConfigureMoney(price.ComplexProperty(p => p.TaxableValue), $"{prefix}taxable_value");
        ConfigureMoney(price.ComplexProperty(p => p.CentralTax), $"{prefix}central_tax");
        ConfigureMoney(price.ComplexProperty(p => p.StateTax), $"{prefix}state_tax");
        ConfigureMoney(price.ComplexProperty(p => p.IntegratedTax), $"{prefix}integrated_tax");
        ConfigureMoney(price.ComplexProperty(p => p.Cess), $"{prefix}cess");
        ConfigureMoney(price.ComplexProperty(p => p.RoundOff), $"{prefix}round_off");
        ConfigureMoney(price.ComplexProperty(p => p.GrandTotal), $"{prefix}grand_total");

        // Both derived from the amounts; a stored copy would be a second place for a total to be wrong.
        price.Ignore(p => p.TaxTotal);
        price.Ignore(p => p.IsIntegratedSupply);
    }

    /// <summary>One amount and the currency it is in.</summary>
    /// <remarks>
    /// <c>numeric(18,4)</c> is <c>ModuleDbContext</c>'s money convention, stated here as well because the facets
    /// of a complex type's property are worth reading beside the column name. Three characters is
    /// <see cref="Money"/>'s own rule. Both are named explicitly for the reason
    /// <see cref="ConfigurePriceSnapshot"/> gives: <see cref="Money.Amount"/> and <see cref="Money.Currency"/>
    /// are get-only, and a get-only property is not discovered by convention.
    /// </remarks>
    /// <param name="money">The complex-property builder.</param>
    /// <param name="prefix">The column prefix, to which <c>_amount</c> and <c>_currency</c> are appended.</param>
    private static void ConfigureMoney(ComplexPropertyBuilder<Money> money, string prefix)
    {
        money.Property(m => m.Amount).HasColumnName($"{prefix}_amount").HasPrecision(18, Money.InternalScale);
        money.Property(m => m.Currency)
            .HasColumnName($"{prefix}_currency").HasMaxLength(CurrencyCodeLength).IsRequired();
    }

    /// <summary>An ISO 4217 code is three characters. <see cref="Money"/>'s own constructor says so.</summary>
    private const int CurrencyCodeLength = 3;
}
