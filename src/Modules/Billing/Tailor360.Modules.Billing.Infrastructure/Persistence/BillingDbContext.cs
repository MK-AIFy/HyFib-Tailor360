using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Platform.Abstractions.Money;
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

    /// <summary>One calculation per reference per organisation.</summary>
    public const string CalculationReferenceIndex = "ux_calculation_snapshots_organisation_reference";

    /// <summary>The calculation snapshots.</summary>
    public DbSet<CalculationSnapshot> CalculationSnapshots => Set<CalculationSnapshot>();

    /// <summary>One code per organisation: a mode is defined once and renamed, never redefined.</summary>
    public const string OnePaymentModePerCodeIndex = "ux_payment_modes_organisation_code";

    /// <summary>At most one open session per cashier per branch (INV-CSH-01).</summary>
    public const string OneOpenCashierSessionIndex = "ux_cashier_sessions_one_open";

    /// <summary>The organisation's payment modes.</summary>
    public DbSet<PaymentMode> PaymentModes => Set<PaymentMode>();

    /// <summary>Cashier sessions with their counts.</summary>
    public DbSet<CashierSession> CashierSessions => Set<CashierSession>();

    /// <summary>One payment per mode and reference: a duplicate callback or a retyped receipt is refused by the index.</summary>
    public const string OnePaymentPerModeReferenceIndex = "ux_payments_organisation_mode_reference";

    /// <summary>One payment per cashier and client key: the request's own twin is refused by the index.</summary>
    public const string OnePaymentPerClientKeyIndex = "ux_payments_organisation_cashier_client_key";

    /// <summary>The payments.</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>The allocations of payments to invoices.</summary>
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    /// <summary>The advances held against orders.</summary>
    public DbSet<Advance> Advances => Set<Advance>();

    /// <summary>
    /// A garment job is charged on at most one live invoice. Judged over `invoice_status` on the line rows,
    /// which the invoice's own trigger keeps equal to the parent's status, because a partial index cannot
    /// reach across the join; the model does not map that column.
    /// </summary>
    public const string OneLiveInvoicePerJobIndex = "ux_invoice_lines_live_garment_job";

    /// <summary>An invoice is cancelled once: the second cancellation of a race trips this rather than appending twice.</summary>
    public const string OneCancellationPerInvoiceIndex = "ux_invoice_cancellations_invoice";

    /// <summary>What Billing knows about orders.</summary>
    public DbSet<OrderFact> OrderFacts => Set<OrderFact>();

    /// <summary>The invoices.</summary>
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <summary>The invoice lines.</summary>
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    /// <summary>The cancellations appended to posted invoices.</summary>
    public DbSet<InvoiceCancellation> InvoiceCancellations => Set<InvoiceCancellation>();

    /// <summary>The credit and debit notes posted against invoices.</summary>
    public DbSet<AdjustmentNote> AdjustmentNotes => Set<AdjustmentNote>();

    /// <summary>The rendered documents, one row per posted document and version.</summary>
    public DbSet<DocumentArtifact> DocumentArtifacts => Set<DocumentArtifact>();

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
        ConfigureCalculationSnapshots(modelBuilder);
        ConfigurePaymentModes(modelBuilder);
        ConfigureCashierSessions(modelBuilder);
        ConfigurePayments(modelBuilder);
        ConfigureOrderFacts(modelBuilder);
        ConfigureInvoices(modelBuilder);
    }

    private static void ConfigureOrderFacts(ModelBuilder modelBuilder)
        => modelBuilder.Entity<OrderFact>(entity =>
        {
            entity.ToTable("order_facts");
            entity.HasKey(fact => fact.OrderId);
            entity.Property(fact => fact.OrderNumber).HasMaxLength(OrderFact.MaximumNumberLength).IsRequired();
            entity.Property(fact => fact.CancellationReasonCode).HasMaxLength(OrderFact.MaximumNumberLength);
            entity.Property(fact => fact.Status).HasConversion<int>();
            entity.Ignore(fact => fact.IsInvoiceable);
            entity.HasIndex(fact => new { fact.OrganisationId, fact.BranchId, fact.Status })
                .HasDatabaseName("ix_order_facts_organisation_branch_status");

            entity.HasMany(fact => fact.Jobs)
                .WithOne()
                .HasForeignKey(job => job.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(fact => fact.Jobs).AutoInclude();
        });

    private static void ConfigureInvoices(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderFactJob>(entity =>
        {
            entity.ToTable("order_fact_jobs");
            entity.HasKey(job => new { job.OrderId, job.GarmentJobId });
            entity.Property(job => job.GarmentJobNumber).HasMaxLength(OrderFact.MaximumNumberLength).IsRequired();
            entity.Property(job => job.CancellationReasonCode).HasMaxLength(OrderFact.MaximumNumberLength);
            entity.Ignore(job => job.IsCancelled);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices", table =>
                table.HasCheckConstraint(
                    "ck_invoices_lifecycle_is_consistent",
                    """
                    ((status = 2 AND discarded_at IS NOT NULL) OR (status <> 2 AND discarded_at IS NULL))
                    AND ((status = 1 AND invoice_number IS NOT NULL AND barcode_payload IS NOT NULL AND posted_at IS NOT NULL)
                         OR (status <> 1 AND invoice_number IS NULL AND barcode_payload IS NULL AND posted_at IS NULL))
                    """));
            entity.HasKey(invoice => invoice.Id);
            entity.Property(invoice => invoice.OrderNumber).HasMaxLength(OrderFact.MaximumNumberLength).IsRequired();
            entity.Property(invoice => invoice.Status).HasConversion<int>();
            entity.Property(invoice => invoice.DiscardReason).HasMaxLength(Invoice.MaximumReasonLength);
            entity.Property(invoice => invoice.InvoiceNumber).HasMaxLength(DocumentNumbers.MaximumLength);
            entity.Property(invoice => invoice.BarcodePayload).HasMaxLength(Platform.Abstractions.Barcodes.BarcodePayload.Length);
            entity.Property(invoice => invoice.FinancialYear).HasMaxLength(4);
            entity.Ignore(invoice => invoice.IsDraft);
            entity.Ignore(invoice => invoice.IsPosted);
            entity.Ignore(invoice => invoice.IsCancelled);
            entity.Ignore(invoice => invoice.GarmentJobIds);

            // A number is drawn once and never reused, and a barcode payload resolves to one document:
            // both unique within the organisation, over the posted rows alone.
            entity.HasIndex(invoice => new { invoice.OrganisationId, invoice.InvoiceNumber })
                .IsUnique()
                .HasDatabaseName("ux_invoices_organisation_invoice_number")
                .HasFilter("invoice_number IS NOT NULL");
            entity.HasIndex(invoice => new { invoice.OrganisationId, invoice.BarcodePayload })
                .IsUnique()
                .HasDatabaseName("ux_invoices_organisation_barcode_payload")
                .HasFilter("barcode_payload IS NOT NULL");

            entity.HasOne(invoice => invoice.Cancellation)
                .WithOne()
                .HasForeignKey<InvoiceCancellation>(cancellation => cancellation.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(invoice => invoice.Cancellation).AutoInclude();
            entity.HasMany(invoice => invoice.Notes)
                .WithOne()
                .HasForeignKey(note => note.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(invoice => invoice.Notes).AutoInclude();

            // The customer as the document names them: Personal data, kept as issued (data classification 5.10).
            entity.ComplexProperty(invoice => invoice.Customer, customer =>
            {
                customer.IsRequired();
                customer.Property(c => c.CustomerNumber).HasColumnName("customer_number").HasMaxLength(InvoiceCustomer.MaximumLength).IsRequired();
                customer.Property(c => c.DisplayName).HasColumnName("customer_display_name").HasMaxLength(InvoiceCustomer.MaximumLength).IsRequired();
                customer.Property(c => c.AddressLine).HasColumnName("customer_address_line").HasMaxLength(InvoiceCustomer.MaximumLength);
                customer.Property(c => c.Locality).HasColumnName("customer_locality").HasMaxLength(InvoiceCustomer.MaximumLength);
                customer.Property(c => c.Postcode).HasColumnName("customer_postcode").HasMaxLength(InvoiceCustomer.MaximumLength);
            });

            entity.ComplexProperty(invoice => invoice.Calculation, calculation =>
            {
                calculation.IsRequired();
                calculation.Property(c => c.Reference).HasColumnName("calculation_reference").HasMaxLength(CalculationSnapshot.MaximumReferenceLength).IsRequired();
                calculation.Property(c => c.PriceListVersionId).HasColumnName("price_list_version_id");
                calculation.Property(c => c.TaxConfigurationVersionId).HasColumnName("tax_configuration_version_id");
                calculation.Property(c => c.GstRegistrationId).HasColumnName("gst_registration_id");
                calculation.Property(c => c.Gstin).HasColumnName("gstin").HasMaxLength(15).IsRequired();
                calculation.Property(c => c.SupplierStateCode).HasColumnName("supplier_state_code").HasMaxLength(2).IsRequired();
                calculation.Property(c => c.PlaceOfSupplyStateCode).HasColumnName("place_of_supply_state_code").HasMaxLength(2).IsRequired();
                calculation.Property(c => c.Scheme).HasColumnName("scheme").HasMaxLength(20).IsRequired();
                calculation.Property(c => c.TaxInclusive).HasColumnName("tax_inclusive");
                calculation.Property(c => c.SupplierLegalName).HasColumnName("supplier_legal_name").HasMaxLength(GstRegistrationDetails.MaximumNameLength).IsRequired();
                calculation.Property(c => c.SupplierTradeName).HasColumnName("supplier_trade_name").HasMaxLength(GstRegistrationDetails.MaximumNameLength);
            });

            entity.ComplexProperty(invoice => invoice.Totals, totals =>
            {
                totals.IsRequired();
                ConfigureMoney(totals.ComplexProperty(t => t.Subtotal), "subtotal");
                ConfigureMoney(totals.ComplexProperty(t => t.DiscountTotal), "discount_total");
                ConfigureMoney(totals.ComplexProperty(t => t.TaxableValue), "taxable_value");
                ConfigureMoney(totals.ComplexProperty(t => t.CentralTax), "central_tax");
                ConfigureMoney(totals.ComplexProperty(t => t.StateTax), "state_tax");
                ConfigureMoney(totals.ComplexProperty(t => t.IntegratedTax), "integrated_tax");
                ConfigureMoney(totals.ComplexProperty(t => t.Cess), "cess");
                ConfigureMoney(totals.ComplexProperty(t => t.RoundOff), "round_off");
                ConfigureMoney(totals.ComplexProperty(t => t.GrandTotal), "grand_total");
            });

            entity.HasMany(invoice => invoice.Lines)
                .WithOne()
                .HasForeignKey(line => line.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(invoice => invoice.Lines).AutoInclude();

            entity.HasIndex(invoice => new { invoice.OrganisationId, invoice.BranchId, invoice.Status, invoice.UpdatedAt })
                .HasDatabaseName("ix_invoices_organisation_branch_status_updated_at");
            entity.HasIndex(invoice => new { invoice.OrganisationId, invoice.OrderId })
                .HasDatabaseName("ix_invoices_organisation_order");
            UseRowVersion(entity);
        });

        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.ToTable("invoice_lines");
            // Keyed by the job the line charges for, not by its position: a re-price that drops, adds or
            // reorders lines then updates a kept job's row where it is, and the one-live-invoice-per-job
            // index below never sees a job move between rows of one invoice. The line number is a plain
            // column for the same reason — two kept lines swapping numbers in one save is a cycle no
            // unique constraint on it could be satisfied through — and the aggregate keeps it distinct.
            entity.HasKey(line => new { line.InvoiceId, line.GarmentJobId });
            entity.Property(line => line.LineKey).HasMaxLength(200).IsRequired();
            entity.Property(line => line.ItemCode).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Property(line => line.Description).HasMaxLength(PriceListItemDetails.MaximumDescriptionLength).IsRequired();
            entity.Property(line => line.Quantity).HasPrecision(18, 4);
            entity.Property(line => line.CatalogueRate).HasPrecision(18, 4);
            entity.Property(line => line.AppliedRate).HasPrecision(18, 4);
            entity.Property(line => line.DiscountRuleCode).HasMaxLength(BillingCode.MaximumLength);
            entity.Property(line => line.DiscountKind).HasMaxLength(20);
            entity.Property(line => line.DiscountValue).HasPrecision(18, 4);
            entity.Property(line => line.TaxCode).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Property(line => line.Classification).HasMaxLength(8).IsRequired();
            entity.Property(line => line.TaxCodeKind).HasMaxLength(20).IsRequired();
            ConfigureMoney(entity.ComplexProperty(line => line.Base), "base");
            ConfigureMoney(entity.ComplexProperty(line => line.DiscountAmount), "discount");
            ConfigureMoney(entity.ComplexProperty(line => line.Gross), "gross");
            ConfigureMoney(entity.ComplexProperty(line => line.TaxableValue), "taxable_value");
            ConfigureMoney(entity.ComplexProperty(line => line.TaxTotal), "tax_total");
            ConfigureMoney(entity.ComplexProperty(line => line.LineTotal), "line_total");
            ConfigureMoney(entity.ComplexProperty(line => line.Variance), "variance");
            // Declared on the model as well as created by the migration, and not only for the read it serves:
            // the change tracker orders the commands of one save by the unique indexes it knows about, so a
            // re-price that moves a job onto another line number deletes the old holder before the new one
            // is written. An index the model cannot see is an index the save can trip over.
            entity.HasIndex(line => line.GarmentJobId)
                .IsUnique()
                .HasDatabaseName(OneLiveInvoicePerJobIndex)
                .HasFilter("invoice_status IN (0, 1)");

            entity.HasMany(line => line.Surcharges)
                .WithOne()
                .HasForeignKey(surcharge => new { surcharge.InvoiceId, surcharge.GarmentJobId })
                .HasConstraintName("fk_invoice_line_surcharges_invoice_lines")
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(line => line.Surcharges).AutoInclude();
            entity.HasMany(line => line.Taxes)
                .WithOne()
                .HasForeignKey(tax => new { tax.InvoiceId, tax.GarmentJobId })
                .HasConstraintName("fk_invoice_tax_components_invoice_lines")
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(line => line.Taxes).AutoInclude();
        });

        modelBuilder.Entity<InvoiceLineSurcharge>(entity =>
        {
            entity.ToTable("invoice_line_surcharges");
            entity.HasKey(surcharge => new { surcharge.InvoiceId, surcharge.GarmentJobId, surcharge.Position });
            entity.Property(surcharge => surcharge.ItemCode).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Property(surcharge => surcharge.Description).HasMaxLength(PriceListItemDetails.MaximumDescriptionLength).IsRequired();
            entity.Property(surcharge => surcharge.Rate).HasPrecision(18, 4);
            ConfigureMoney(entity.ComplexProperty(surcharge => surcharge.Amount), "amount");
        });

        modelBuilder.Entity<InvoiceTaxComponent>(entity =>
        {
            entity.ToTable("invoice_tax_components");
            entity.HasKey(tax => new { tax.InvoiceId, tax.GarmentJobId, tax.Kind });
            entity.Property(tax => tax.Kind).HasMaxLength(8).IsRequired();
            entity.Property(tax => tax.RatePercent).HasPrecision(6, 3);
            ConfigureMoney(entity.ComplexProperty(tax => tax.Amount), "amount");
        });

        modelBuilder.Entity<InvoiceCancellation>(entity =>
        {
            entity.ToTable("invoice_cancellations");
            entity.HasKey(cancellation => cancellation.Id);
            entity.Property(cancellation => cancellation.Reason).HasMaxLength(Invoice.MaximumReasonLength).IsRequired();
            entity.HasIndex(cancellation => cancellation.InvoiceId)
                .IsUnique()
                .HasDatabaseName(OneCancellationPerInvoiceIndex);
            entity.HasOne<AdjustmentNote>()
                .WithMany()
                .HasForeignKey(cancellation => cancellation.CreditNoteId)
                .HasConstraintName("fk_invoice_cancellations_adjustment_notes")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AdjustmentNote>(entity =>
        {
            entity.ToTable("adjustment_notes");
            entity.HasKey(note => note.Id);
            entity.Property(note => note.Kind).HasConversion<int>();
            entity.Property(note => note.Number).HasMaxLength(DocumentNumbers.MaximumLength).IsRequired();
            entity.Property(note => note.Reason).HasMaxLength(Invoice.MaximumReasonLength).IsRequired();
            entity.Property(note => note.PostedOn).HasColumnType("date");
            entity.ComplexProperty(note => note.Totals, totals =>
            {
                totals.IsRequired();
                ConfigureMoney(totals.ComplexProperty(t => t.Subtotal), "subtotal");
                ConfigureMoney(totals.ComplexProperty(t => t.DiscountTotal), "discount_total");
                ConfigureMoney(totals.ComplexProperty(t => t.TaxableValue), "taxable_value");
                ConfigureMoney(totals.ComplexProperty(t => t.CentralTax), "central_tax");
                ConfigureMoney(totals.ComplexProperty(t => t.StateTax), "state_tax");
                ConfigureMoney(totals.ComplexProperty(t => t.IntegratedTax), "integrated_tax");
                ConfigureMoney(totals.ComplexProperty(t => t.Cess), "cess");
                ConfigureMoney(totals.ComplexProperty(t => t.RoundOff), "round_off");
                ConfigureMoney(totals.ComplexProperty(t => t.GrandTotal), "grand_total");
            });
            entity.HasIndex(note => new { note.OrganisationId, note.Number })
                .IsUnique()
                .HasDatabaseName("ux_adjustment_notes_organisation_number");
            entity.HasIndex(note => note.InvoiceId).HasDatabaseName("ix_adjustment_notes_invoice");
            entity.HasMany(note => note.Lines)
                .WithOne()
                .HasForeignKey(line => line.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(note => note.Lines).AutoInclude();
        });

        modelBuilder.Entity<AdjustmentNoteLine>(entity =>
        {
            entity.ToTable("adjustment_note_lines");
            entity.HasKey(line => new { line.NoteId, line.GarmentJobId });
            ConfigureMoney(entity.ComplexProperty(line => line.TaxableValue), "taxable_value");
            ConfigureMoney(entity.ComplexProperty(line => line.TaxTotal), "tax_total");
            ConfigureMoney(entity.ComplexProperty(line => line.LineTotal), "line_total");
            entity.HasMany(line => line.Taxes)
                .WithOne()
                .HasForeignKey(tax => new { tax.NoteId, tax.GarmentJobId })
                .HasConstraintName("fk_adjustment_note_taxes_adjustment_note_lines")
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(line => line.Taxes).AutoInclude();
        });

        modelBuilder.Entity<DocumentArtifact>(entity =>
        {
            entity.ToTable("document_artifacts", table =>
                table.HasCheckConstraint(
                    "ck_document_artifacts_completed_is_whole",
                    """
                    (status = 1 AND object_key IS NOT NULL AND sha256 IS NOT NULL AND size_bytes IS NOT NULL AND completed_at IS NOT NULL)
                    OR (status <> 1 AND object_key IS NULL AND sha256 IS NULL AND size_bytes IS NULL AND completed_at IS NULL)
                    """));
            entity.HasKey(artifact => artifact.Id);
            entity.Property(artifact => artifact.Kind).HasConversion<int>();
            entity.Property(artifact => artifact.Status).HasConversion<int>();
            entity.Property(artifact => artifact.DocumentNumber).HasMaxLength(DocumentNumbers.MaximumLength).IsRequired();
            entity.Property(artifact => artifact.ObjectKey).HasMaxLength(200);
            entity.Property(artifact => artifact.ContentType).HasMaxLength(100);
            entity.Property(artifact => artifact.Sha256).HasMaxLength(64);
            entity.Property(artifact => artifact.LastError).HasMaxLength(DocumentArtifact.MaximumErrorLength);
            entity.Ignore(artifact => artifact.IsCompleted);

            // One rendering per document and version; the worker's poll over the pending ones.
            entity.HasIndex(artifact => new { artifact.Kind, artifact.DocumentId, artifact.Version })
                .IsUnique()
                .HasDatabaseName("ux_document_artifacts_document_version");
            entity.HasIndex(artifact => new { artifact.Status, artifact.RequestedAt })
                .HasDatabaseName("ix_document_artifacts_status_requested_at");
            entity.HasIndex(artifact => artifact.ObjectKey)
                .IsUnique()
                .HasDatabaseName("ux_document_artifacts_object_key")
                .HasFilter("object_key IS NOT NULL");
            UseRowVersion(entity);
        });

        modelBuilder.Entity<AdjustmentNoteTax>(entity =>
        {
            entity.ToTable("adjustment_note_taxes");
            entity.HasKey(tax => new { tax.NoteId, tax.GarmentJobId, tax.Kind });
            entity.Property(tax => tax.Kind).HasMaxLength(8).IsRequired();
            entity.Property(tax => tax.RatePercent).HasPrecision(6, 3);
            ConfigureMoney(entity.ComplexProperty(tax => tax.Amount), "amount");
        });
    }

    private static void ConfigurePaymentModes(ModelBuilder modelBuilder)
        => modelBuilder.Entity<PaymentMode>(entity =>
        {
            entity.ToTable("payment_modes", table =>
                table.HasCheckConstraint("ck_payment_modes_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'"));
            entity.HasKey(mode => mode.Id);
            entity.Property(mode => mode.Code).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Property(mode => mode.Name).HasMaxLength(PaymentMode.MaximumNameLength).IsRequired();
            entity.HasIndex(mode => new { mode.OrganisationId, mode.Code }).IsUnique().HasDatabaseName(OnePaymentModePerCodeIndex);
            entity.Ignore(mode => mode.Details);

            // A restriction, not an offer: the same owned shape as a price-list version's branches.
            entity.OwnsMany(mode => mode.Branches, branches =>
            {
                branches.ToTable("payment_mode_branches");
                branches.WithOwner().HasForeignKey(branch => branch.PaymentModeId);
                branches.HasKey(branch => new { branch.PaymentModeId, branch.BranchId });
            });
            entity.Navigation(mode => mode.Branches).AutoInclude();

            UseRowVersion(entity);
        });

    private static void ConfigureCashierSessions(ModelBuilder modelBuilder)
        => modelBuilder.Entity<CashierSession>(entity =>
        {
            entity.ToTable("cashier_sessions", table =>
                table.HasCheckConstraint(
                    "ck_cashier_sessions_lifecycle_is_consistent",
                    "(status = 0 AND closed_at IS NULL AND closed_by IS NULL) OR (status = 1 AND closed_at IS NOT NULL AND closed_by IS NOT NULL)"));
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Status).HasConversion<int>();
            entity.Property(session => session.VarianceReason).HasMaxLength(CashierSession.MaximumReasonLength);
            ConfigureMoney(entity.ComplexProperty(session => session.OpeningFloat), "opening_float");
            ConfigureMoney(entity.ComplexProperty(session => session.ExpectedTotal), "expected_total");
            ConfigureMoney(entity.ComplexProperty(session => session.CountedTotal), "counted_total");
            ConfigureMoney(entity.ComplexProperty(session => session.Variance), "variance");
            entity.Ignore(session => session.IsOpen);

            // INV-CSH-01 as an index, not a check in code: two opens in the same instant cannot both pass a
            // read, and only one passes the index.
            entity.HasIndex(session => new { session.BranchId, session.CashierId })
                .IsUnique()
                .HasFilter("closed_at IS NULL")
                .HasDatabaseName(OneOpenCashierSessionIndex);
            entity.HasIndex(session => new { session.OrganisationId, session.BranchId, session.OpenedAt })
                .HasDatabaseName("ix_cashier_sessions_organisation_branch_opened_at");

            entity.OwnsMany(session => session.Counts, counts =>
            {
                counts.ToTable("cashier_session_counts");
                counts.WithOwner().HasForeignKey(count => count.SessionId);
                counts.HasKey(count => new { count.SessionId, count.Denomination });
                counts.Property(count => count.Denomination).HasPrecision(18, Money.DocumentScale);
                counts.Ignore(count => count.Value);
            });
            entity.OwnsMany(session => session.ModeTotals, totals =>
            {
                totals.ToTable("cashier_session_mode_totals");
                totals.WithOwner().HasForeignKey(total => total.SessionId);
                totals.HasKey(total => new { total.SessionId, total.ModeCode });
                totals.Property(total => total.ModeCode).HasMaxLength(BillingCode.MaximumLength).IsRequired();
                totals.Property(total => total.Expected).HasPrecision(18, Money.DocumentScale);
                totals.Property(total => total.Counted).HasPrecision(18, Money.DocumentScale);
                totals.Property(total => total.Variance).HasPrecision(18, Money.DocumentScale);
            });
            entity.Navigation(session => session.Counts).AutoInclude();
            entity.Navigation(session => session.ModeTotals).AutoInclude();

            UseRowVersion(entity);
        });

    private static void ConfigurePayments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments", table =>
                table.HasCheckConstraint("ck_payments_amount_is_positive", "amount_amount > 0"));
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.ModeCode).HasMaxLength(BillingCode.MaximumLength).IsRequired();
            entity.Property(payment => payment.Reference).HasMaxLength(PaymentReferences.MaximumLength);
            entity.Property(payment => payment.ClientKey).HasMaxLength(Payment.MaximumClientKeyLength);
            entity.Property(payment => payment.Status).HasConversion<int>();
            ConfigureMoney(entity.ComplexProperty(payment => payment.Amount), "amount");
            entity.Ignore(payment => payment.Allocated);
            entity.Ignore(payment => payment.UnappliedAdvance);

            // Append-only by trigger (INV-PAY-01), so no updated pair and no row version: a row is written
            // once with recorded_at/recorded_by and never changes.
            entity.HasOne<CashierSession>().WithMany().HasForeignKey(payment => payment.CashierSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(payment => payment.Allocations)
                .WithOne()
                .HasForeignKey(allocation => allocation.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(payment => payment.Allocations).AutoInclude();
            entity.HasOne(payment => payment.Advance)
                .WithOne()
                .HasForeignKey<Advance>(advance => advance.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(payment => payment.Advance).AutoInclude();

            // The transactional guard beside the idempotency record: the natural key of the effect.
            entity.HasIndex(payment => new { payment.OrganisationId, payment.ModeCode, payment.Reference })
                .IsUnique()
                .HasFilter("reference IS NOT NULL")
                .HasDatabaseName(OnePaymentPerModeReferenceIndex);
            entity.HasIndex(payment => new { payment.OrganisationId, payment.CashierId, payment.ClientKey })
                .IsUnique()
                .HasFilter("client_key IS NOT NULL")
                .HasDatabaseName(OnePaymentPerClientKeyIndex);
            entity.HasIndex(payment => new { payment.OrganisationId, payment.OrderId, payment.RecordedAt })
                .HasDatabaseName("ix_payments_organisation_order_recorded_at");
            entity.HasIndex(payment => new { payment.CashierSessionId, payment.ModeCode })
                .HasDatabaseName("ix_payments_session_mode");
        });

        modelBuilder.Entity<PaymentAllocation>(entity =>
        {
            entity.ToTable("payment_allocations", table =>
                table.HasCheckConstraint("ck_payment_allocations_amount_is_positive", "amount_amount > 0"));
            entity.HasKey(allocation => allocation.Id);
            entity.Property(allocation => allocation.Kind).HasConversion<int>();
            ConfigureMoney(entity.ComplexProperty(allocation => allocation.Amount), "amount");
            entity.HasOne<Invoice>().WithMany().HasForeignKey(allocation => allocation.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Advance>().WithMany().HasForeignKey(allocation => allocation.AdvanceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(allocation => allocation.InvoiceId).HasDatabaseName("ix_payment_allocations_invoice");
            entity.HasIndex(allocation => allocation.AdvanceId).HasDatabaseName("ix_payment_allocations_advance").HasFilter("advance_id IS NOT NULL");
        });

        modelBuilder.Entity<Advance>(entity =>
        {
            entity.ToTable("advances", table =>
                table.HasCheckConstraint("ck_advances_amount_is_positive", "amount_amount > 0"));
            entity.HasKey(advance => advance.Id);
            ConfigureMoney(entity.ComplexProperty(advance => advance.Amount), "amount");
            entity.HasIndex(advance => advance.PaymentId).IsUnique().HasDatabaseName("ux_advances_payment");
        });
    }

    /// <summary>Money as Orders maps it: an amount at the internal scale and its three-letter currency, side by side.</summary>
    private static void ConfigureMoney(ComplexPropertyBuilder<Money> money, string prefix)
    {
        money.Property(m => m.Amount).HasColumnName($"{prefix}_amount").HasPrecision(18, Money.InternalScale);
        money.Property(m => m.Currency).HasColumnName($"{prefix}_currency").HasMaxLength(3).IsRequired();
    }

    private static void ConfigureCalculationSnapshots(ModelBuilder modelBuilder)
        => modelBuilder.Entity<CalculationSnapshot>(entity =>
        {
            entity.ToTable("calculation_snapshots");
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.Reference).HasMaxLength(CalculationSnapshot.MaximumReferenceLength).IsRequired();
            // The request and the result as the application wrote them: jsonb so that a report can reach
            // into a figure without a second table, and so that the shape is the contract's, not the schema's.
            entity.Property(snapshot => snapshot.RequestJson).HasColumnName("request").HasColumnType("jsonb").IsRequired();
            entity.Property(snapshot => snapshot.ResultJson).HasColumnName("result").HasColumnType("jsonb").IsRequired();
            entity.Property(snapshot => snapshot.SchemaVersion).IsRequired();

            // The versions a figure was made on are never deleted once published (the triggers refuse it),
            // and a snapshot naming one that does not exist is a snapshot that cannot be recomputed.
            // Named by hand where the conventional name exceeds PostgreSQL's 63-character limit.
            entity.HasOne<PriceListVersion>().WithMany().HasForeignKey(snapshot => snapshot.PriceListVersionId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_calculation_snapshots_price_list_versions");
            entity.HasOne<TaxConfigurationVersion>().WithMany().HasForeignKey(snapshot => snapshot.TaxConfigurationVersionId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_calculation_snapshots_tax_configuration_versions");
            entity.HasOne<GstRegistration>().WithMany().HasForeignKey(snapshot => snapshot.GstRegistrationId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_calculation_snapshots_gst_registrations");

            // Append-only by trigger, so there is no updated_at/updated_by pair and no row version to
            // compare: a row is written once with calculated_at/calculated_by and never changes.
            entity.HasIndex(snapshot => new { snapshot.OrganisationId, snapshot.Reference })
                .IsUnique()
                .HasDatabaseName(CalculationReferenceIndex);
            entity.HasIndex(snapshot => new { snapshot.OrganisationId, snapshot.BranchId, snapshot.CalculatedAt })
                .HasDatabaseName("ix_calculation_snapshots_organisation_branch_calculated_at");
        });

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
