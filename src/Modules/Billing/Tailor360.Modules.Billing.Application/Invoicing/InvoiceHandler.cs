using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>
/// The commands a cashier runs against draft invoices (#42, #153): draft one from an order's stored
/// calculation, re-price a draft, discard a draft.
/// </summary>
/// <remarks>
/// <para>
/// Billing holds no reference to Orders (ARCH-010). What it knows about the order came through the
/// outbox into <see cref="OrderFact"/>; what it knows about the price came through
/// <see cref="IPricingService"/>, under the reference the caller names, and is verified to reproduce
/// on the versions it was made on before a single line is copied (<c>INV-INV-03</c>). A figure that no
/// longer reproduces is refused, never re-priced quietly.
/// </para>
/// <para>
/// The customer is copied onto the invoice as the document names them, read through Customers'
/// snapshot contract under the caller's own permissions: a cashier who may not read contact details
/// drafts an invoice without an address on it, and the document says so rather than printing what the
/// cashier could not see.
/// </para>
/// </remarks>
public sealed class InvoiceHandler(
    IInvoiceStore invoices,
    IOrderFactStore orders,
    IGstRegistrationStore registrations,
    IPricingService pricing,
    ICustomerSnapshotQuery customers,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A draft was created.</summary>
    public const string DraftedAction = "billing.invoice.drafted";

    /// <summary>A draft's lines were replaced.</summary>
    public const string UpdatedAction = "billing.invoice.updated";

    /// <summary>A draft was discarded.</summary>
    public const string DiscardedAction = "billing.invoice.discarded";

    /// <summary>Drafts an invoice from an order's stored calculation.</summary>
    public async Task<Result<AdministeredInvoice>> CreateDraftAsync(CreateInvoiceDraftCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await orders.FindAsync(command.OrderId, command.OrganisationId, cancellationToken);
        var checkedOrder = CheckOrder(order, command.BranchId);
        if (checkedOrder.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(checkedOrder.Error);
        }

        // Trimmed once, so the reference the invoice carries is the key the snapshot is stored under.
        var reference = command.CalculationReference.Trim();
        var verified = await pricing.VerifySnapshotAsync(command.OrganisationId, reference, cancellationToken);
        if (verified.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(verified.Error);
        }

        // A figure priced under another branch's list is not this branch's document, however well it reproduces.
        if (verified.Value.Request.BranchId != order!.BranchId)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.CalculationForAnotherBranch);
        }

        var lines = InvoiceLines.From(verified.Value.Result, order);
        if (lines.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(lines.Error);
        }

        var jobIds = lines.Value.Select(line => line.GarmentJobId).Distinct().ToArray();
        if (command.GarmentJobIds is { Count: > 0 } wanted && !wanted.ToHashSet().SetEquals(jobIds))
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.LineNotAGarmentJob("garmentJobIds"));
        }

        if ((await invoices.AlreadyInvoicedAsync(command.OrganisationId, jobIds, cancellationToken)).Count > 0)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.JobAlreadyInvoiced);
        }

        var calculation = await CalculationOf(verified.Value.Result, reference, verified.Value.Request.PlaceOfSupplyStateCode, command.OrganisationId, cancellationToken);
        if (calculation.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(calculation.Error);
        }

        var customer = await customers.GetAsync(order.CustomerId, command.CallerPermissions, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.CustomerNotFound);
        }

        var created = Invoice.CreateDraft(
            ids.NewId(), command.OrganisationId, order.BranchId, order.CustomerId, order.OrderId, order.OrderNumber,
            new InvoiceCustomer(customer.CustomerNumber, customer.DisplayName, customer.AddressLine, customer.Locality, customer.Postcode),
            calculation.Value, lines.Value, InvoiceLines.TotalsOf(verified.Value.Result), clock.UtcNow, command.By);
        if (created.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(created.Error);
        }

        invoices.Add(created.Value);
        var saved = await invoices.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, DraftedAction, BillingAudit.InvoiceEntity, created.Value.Id,
            $"Invoice drafted for order {order.OrderNumber} with {created.Value.Lines.Count} line(s) from calculation "
            + $"'{reference}'.",
            command.Reason, null, InvoiceSnapshot.Of(created.Value), cancellationToken);

        return Result.Success(Administered(created.Value));
    }

    /// <summary>Replaces a draft's lines by pricing them afresh.</summary>
    public async Task<Result<AdministeredInvoice>> RepriceAsync(RepriceInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.InvoiceId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(found.Error);
        }

        var invoice = found.Value;
        if (!invoice.IsDraft)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.InvoiceNotEditable);
        }

        var order = await orders.FindAsync(invoice.OrderId, command.OrganisationId, cancellationToken);
        var checkedOrder = CheckOrder(order, invoice.BranchId);
        if (checkedOrder.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(checkedOrder.Error);
        }

        // Everything that can be refused about the lines is refused before a figure is stored: the engine
        // keeps every calculation it makes, so a refusal after pricing would leave a snapshot behind.
        var wantedJobs = InvoiceLines.JobsOf(command.Lines, order!);
        if (wantedJobs.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(wantedJobs.Error);
        }

        var newJobs = wantedJobs.Value.Except(invoice.GarmentJobIds).ToArray();
        if (newJobs.Length > 0 && (await invoices.AlreadyInvoicedAsync(command.OrganisationId, newJobs, cancellationToken)).Count > 0)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.JobAlreadyInvoiced);
        }

        // A fresh calculation under a reference of the invoice's own, stored like every other. The
        // revision says which edit it was; the attempt token keeps an edit that failed after this point
        // — a lost race on the row, say — from leaving a snapshot the next edit would collide with.
        var reference = $"invoice:{invoice.Id:N}:{invoice.Revision + 1}:{ids.NewId():N}";
        var priced = await pricing.PriceAsync(
            new PricingRequest(command.OrganisationId, invoice.BranchId, command.On, command.PlaceOfSupplyStateCode, reference, command.Lines),
            cancellationToken);
        if (priced.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(priced.Error);
        }

        var lines = InvoiceLines.From(priced.Value, order!);
        if (lines.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(lines.Error);
        }

        var calculation = await CalculationOf(priced.Value, reference, command.PlaceOfSupplyStateCode, command.OrganisationId, cancellationToken);
        if (calculation.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(calculation.Error);
        }

        var before = InvoiceSnapshot.Of(invoice);
        var set = invoice.SetLines(calculation.Value, lines.Value, InvoiceLines.TotalsOf(priced.Value), clock.UtcNow, command.By);
        if (set.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(set.Error);
        }

        var saved = await invoices.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, UpdatedAction, BillingAudit.InvoiceEntity, invoice.Id,
            $"Invoice draft re-priced as revision {invoice.Revision} with {invoice.Lines.Count} line(s) from calculation '{reference}'.",
            command.Reason, before, InvoiceSnapshot.Of(invoice), cancellationToken);

        return Result.Success(Administered(invoice));
    }

    /// <summary>Discards a draft.</summary>
    public async Task<Result<AdministeredInvoice>> DiscardAsync(DiscardInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.InvoiceId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(found.Error);
        }

        var invoice = found.Value;
        var before = InvoiceSnapshot.Of(invoice);
        var discarded = invoice.Discard(clock.UtcNow, command.By, command.Reason);
        if (discarded.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(discarded.Error);
        }

        var saved = await invoices.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, DiscardedAction, BillingAudit.InvoiceEntity, invoice.Id,
            $"Invoice draft for order {invoice.OrderNumber} discarded.",
            command.Reason, before, InvoiceSnapshot.Of(invoice), cancellationToken);

        return Result.Success(Administered(invoice));
    }

    /// <summary>What every command asks of the order: heard of, confirmed, not cancelled, and the caller's branch's.</summary>
    private static Result CheckOrder(OrderFact? order, Guid branchId)
    {
        if (order is null)
        {
            return Result.Failure(BillingErrors.OrderNotKnown);
        }

        if (order.CustomerId == Guid.Empty)
        {
            // Its garments arrived ahead of its confirmation; the customer is not known yet.
            return Result.Failure(BillingErrors.OrderNotYetConfirmed);
        }

        if (!order.IsInvoiceable)
        {
            return Result.Failure(BillingErrors.OrderCancelled);
        }

        return order.BranchId == branchId ? Result.Success() : Result.Failure(BillingErrors.OrderAtAnotherBranch);
    }

    private async Task<Result<InvoiceCalculation>> CalculationOf(PricingResult result, string reference, string placeOfSupply, Guid organisationId, CancellationToken cancellationToken)
    {
        var registration = await registrations.FindAsync(result.GstRegistrationId, organisationId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure<InvoiceCalculation>(BillingErrors.ConfigurationMissing(
                "The registration the calculation was made against is not one of this organisation.", "calculationReference"));
        }

        return Result.Success(new InvoiceCalculation(
            reference, result.PriceListVersionId, result.TaxConfigurationVersionId, registration.Id, registration.Gstin,
            registration.StateCode, placeOfSupply, result.Scheme.ToString(), result.TaxInclusive));
    }

    private async Task<Result<Invoice>> LoadForChangeAsync(Guid invoiceId, Guid organisationId, EntityTag expectedVersion, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindAsync(invoiceId, organisationId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<Invoice>(BillingErrors.InvoiceNotFound);
        }

        return expectedVersion.Matches(invoices.EntityTagOf(invoice))
            ? Result.Success(invoice)
            : Result.Failure<Invoice>(BillingErrors.InvoiceChanged);
    }

    private AdministeredInvoice Administered(Invoice invoice) => new(invoice, invoices.EntityTagOf(invoice));
}

/// <summary>Draft an invoice from an order's stored calculation.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="BranchId">The branch the caller is working in; it must be the order's.</param>
/// <param name="OrderId">The order.</param>
/// <param name="CalculationReference">The reference the order's calculation was stored under.</param>
/// <param name="GarmentJobIds">The jobs the invoice must cover, or empty for whatever the calculation priced.</param>
/// <param name="CallerPermissions">The caller's permissions, deciding what of the customer may be copied.</param>
/// <param name="Reason">Why, or null.</param>
/// <param name="By">Who.</param>
public sealed record CreateInvoiceDraftCommand(
    Guid OrganisationId,
    Guid BranchId,
    Guid OrderId,
    string CalculationReference,
    IReadOnlyCollection<Guid> GarmentJobIds,
    IReadOnlyCollection<string> CallerPermissions,
    string? Reason,
    Guid? By);

/// <summary>Replace a draft's lines by pricing them afresh.</summary>
public sealed record RepriceInvoiceCommand(
    Guid InvoiceId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    DateOnly On,
    string PlaceOfSupplyStateCode,
    IReadOnlyList<PricingLineRequest> Lines,
    string? Reason,
    Guid? By);

/// <summary>Discard a draft.</summary>
public sealed record DiscardInvoiceCommand(Guid InvoiceId, Guid OrganisationId, EntityTag ExpectedVersion, string? Reason, Guid? By);

/// <summary>An invoice with the tag its row carries.</summary>
public sealed record AdministeredInvoice(Invoice Invoice, EntityTag Tag);

/// <summary>What the audit trail records about an invoice: the figures, never the customer's details.</summary>
internal sealed record InvoiceSnapshot(string Status, int Revision, int Lines, decimal GrandTotal, string CalculationReference)
{
    public static InvoiceSnapshot Of(Invoice invoice)
        => new(invoice.Status.ToString(), invoice.Revision, invoice.Lines.Count, invoice.Totals.GrandTotal.Amount, invoice.Calculation.Reference);
}
