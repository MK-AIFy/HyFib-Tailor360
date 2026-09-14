using System.Globalization;
using Shouldly;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The branch-wide read #421 adds over fake stores: which posted invoices count as "still owed", and
/// the loop that fills a page from source pages that can themselves be entirely settled.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OutstandingBalanceQueryTests
{
    private static readonly Guid Cashier = BillingTestData.Id("cashier");

    [Fact]
    public async Task ASettledInvoiceIsExcluded()
    {
        var invoice = PostedInvoice("settled");
        var query = new OutstandingBalanceQuery(
            new FakeInvoiceStore([[invoice]]),
            new FakePaymentStore(allocated: new Dictionary<Guid, Money> { [invoice.Id] = Money.Rupees(567m) }));

        var page = await query.GetAsync(BillingTestData.Organisation, BillingTestData.MainBranch, null, 20, TestContext.Current.CancellationToken);

        page.Rows.ShouldBeEmpty();
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task APartPaidInvoiceIsIncludedWithTheFigureInvoiceBalanceOfGives()
    {
        var invoice = PostedInvoice("part-paid");
        var query = new OutstandingBalanceQuery(
            new FakeInvoiceStore([[invoice]]),
            new FakePaymentStore(allocated: new Dictionary<Guid, Money> { [invoice.Id] = Money.Rupees(100m) }));

        var page = await query.GetAsync(BillingTestData.Organisation, BillingTestData.MainBranch, null, 20, TestContext.Current.CancellationToken);

        var row = page.Rows.ShouldHaveSingleItem();
        var expected = InvoiceBalance.Of(invoice, Money.Rupees(100m), Money.Zero);
        row.Balance.Outstanding.ShouldBe(expected.Outstanding);
        row.Balance.Status.ShouldBe(InvoicePaidStatus.PartlyPaid);
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ACancelledAndCreditedInvoiceShowsZeroOutstandingAndIsExcluded()
    {
        var invoice = PostedInvoice("cancelled");
        invoice.Cancel(BillingTestData.Id("cancel"), BillingTestData.Id("cn"), "CN-MAIN-2627-000001", "Wrong customer.", BillingTestData.Today, BillingTestData.Now, Cashier)
            .IsSuccess.ShouldBeTrue();

        var query = new OutstandingBalanceQuery(new FakeInvoiceStore([[invoice]]), new FakePaymentStore());

        var page = await query.GetAsync(BillingTestData.Organisation, BillingTestData.MainBranch, null, 20, TestContext.Current.CancellationToken);

        page.Rows.ShouldBeEmpty();
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task TheLoopFillsThePageRatherThanAnsweringWithEmptySourcePages()
    {
        // The first two source pages are entirely settled; the third holds two outstanding invoices.
        var settledOne = PostedInvoice("settled-1");
        var settledTwo = PostedInvoice("settled-2");
        var owingOne = PostedInvoice("owing-1");
        var owingTwo = PostedInvoice("owing-2");

        var allocated = new Dictionary<Guid, Money>
        {
            [settledOne.Id] = Money.Rupees(567m),
            [settledTwo.Id] = Money.Rupees(567m),
            [owingOne.Id] = Money.Rupees(100m),
            [owingTwo.Id] = Money.Zero,
        };

        var invoiceStore = new FakeInvoiceStore([[settledOne], [settledTwo], [owingOne, owingTwo]]);
        var query = new OutstandingBalanceQuery(invoiceStore, new FakePaymentStore(allocated: allocated));

        var page = await query.GetAsync(BillingTestData.Organisation, BillingTestData.MainBranch, null, 20, TestContext.Current.CancellationToken);

        // One request, two rows — not three answers of which the first two are empty.
        page.Rows.Select(row => row.Invoice.Id).ShouldBe([owingOne.Id, owingTwo.Id]);
        page.NextCursor.ShouldBeNull();
        invoiceStore.Queries.Count.ShouldBe(3);
        invoiceStore.Queries.ShouldAllBe(sent => sent.Status == InvoiceStatus.Posted && sent.Limit == InvoiceListQuery.MaximumLimit);
    }

    [Fact]
    public async Task TheBoundHoldsAgainstASourceThatNeverRunsOut()
    {
        // A source with no settled invoices and no end: every page is outstanding, so without the
        // bound the loop would never come back.
        var pages = Enumerable.Range(0, 50).Select(index => (IReadOnlyList<Invoice>)[PostedInvoice($"endless-{index}")]).ToList();
        var invoiceStore = new FakeInvoiceStore(pages);

        var query = new OutstandingBalanceQuery(invoiceStore, new FakePaymentStore());

        // Ask for far more rows than ten source pages of one invoice each could ever supply.
        var page = await query.GetAsync(BillingTestData.Organisation, BillingTestData.MainBranch, null, 1000, TestContext.Current.CancellationToken);

        // Ten source pages, mirroring OutstandingBalanceQuery's own MaximumSourcePagesPerRequest —
        // the read-budget bound of docs/nfr/capacity-and-performance.md section 3.3, line 220.
        page.Rows.Count.ShouldBe(10);
        page.NextCursor.ShouldNotBeNull();
        invoiceStore.Queries.Count.ShouldBe(10);
    }

    [Fact]
    public async Task ASourcePageThatEndsTheSequenceYieldsANullNextCursor()
    {
        var invoice = PostedInvoice("last");
        var invoiceStore = new FakeInvoiceStore([[invoice]]);
        var query = new OutstandingBalanceQuery(invoiceStore, new FakePaymentStore());

        var page = await query.GetAsync(BillingTestData.Organisation, BillingTestData.MainBranch, null, 20, TestContext.Current.CancellationToken);

        page.NextCursor.ShouldBeNull();
    }

    private static Invoice PostedInvoice(string suffix)
    {
        var jobId = BillingTestData.Id($"job-{suffix}");
        var invoice = Invoice.CreateDraft(
                BillingTestData.Id($"invoice-{suffix}"), BillingTestData.Organisation, BillingTestData.MainBranch,
                BillingTestData.Id($"customer-{suffix}"), BillingTestData.Id($"order-{suffix}"), $"ORD-{suffix}",
                new InvoiceCustomer($"C-MAIN-{suffix}", "Kavitha", "12 Second Street", "Peelamedu", "641004"),
                InvoiceTests.Calculation($"order:{suffix}:1"), [InvoiceTests.Line(jobId, "BLOUSE")], InvoiceTests.Totals(567m),
                BillingTestData.Now, Cashier)
            .Value;
        invoice.Post($"INV-MAIN-2627-{suffix}", $"I-{suffix}", "2627", new DateOnly(2026, 9, 12), BillingTestData.Now, Cashier)
            .IsSuccess.ShouldBeTrue();
        return invoice;
    }

    /// <summary>Pages of posted invoices, keyed by a cursor that is just the next page's index.</summary>
    private sealed class FakeInvoiceStore(IReadOnlyList<IReadOnlyList<Invoice>> pages) : IInvoiceStore
    {
        public List<InvoiceListQuery> Queries { get; } = [];

        public Task<InvoicePage> ListPostedWithNotesAsync(InvoiceListQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var index = query.Cursor is null ? 0 : int.Parse(query.Cursor, CultureInfo.InvariantCulture);
            var invoices = index < pages.Count ? pages[index] : (IReadOnlyList<Invoice>)[];
            var next = index + 1 < pages.Count ? (index + 1).ToString(CultureInfo.InvariantCulture) : null;

            return Task.FromResult(new InvoicePage(invoices, next));
        }

        public Task<InvoicePage> ListAsync(InvoiceListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Invoice?> FindAsync(Guid invoiceId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> AlreadyInvoicedAsync(Guid organisationId, IReadOnlyCollection<Guid> garmentJobIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(Invoice Invoice, AdjustmentNote Note)?> FindNoteAsync(Guid noteId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Invoice?> FindByBarcodeAsync(string barcodePayload, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Invoice>> ListPostedForOrderAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Add(Invoice invoice) => throw new NotSupportedException();

        public EntityTag EntityTagOf(Invoice invoice) => throw new NotSupportedException();

        public Task<Result> SaveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TOutcome>> PostInTransactionAsync<TOutcome>(Guid invoiceId, Guid organisationId, string barcodePayload, Func<CancellationToken, Task<Result<TOutcome>>> work, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TOutcome>> AppendInTransactionAsync<TOutcome>(Guid invoiceId, Guid noteId, Guid organisationId, Func<CancellationToken, Task<Result<TOutcome>>> work, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<long> AllocateAsync(string sequenceKey, string scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakePaymentStore(
        IReadOnlyDictionary<Guid, Money>? allocated = null,
        IReadOnlyDictionary<Guid, Money>? refunded = null) : IPaymentStore
    {
        public Task<IReadOnlyDictionary<Guid, Money>> AllocatedByInvoiceAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default)
            => Task.FromResult(allocated ?? new Dictionary<Guid, Money>());

        public Task<IReadOnlyDictionary<Guid, Money>> RefundedByInvoiceAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default)
            => Task.FromResult(refunded ?? new Dictionary<Guid, Money>());

        public Task<TResult> ReadConsistentlyAsync<TResult>(Func<CancellationToken, Task<TResult>> read, CancellationToken cancellationToken = default)
            => read(cancellationToken);

        public Task<Payment?> FindAsync(Guid paymentId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Payment>> ListForOrderAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, Money>> TakenByModeAsync(Guid cashierSessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void AddReversal(PaymentReversal reversal) => throw new NotSupportedException();

        public void AddRefund(Refund refund) => throw new NotSupportedException();

        public Task<Refund?> FindRefundAsync(Guid refundId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task LockPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Add(Payment payment) => throw new NotSupportedException();

        public void AddReceipt(Receipt receipt) => throw new NotSupportedException();

        public Task<Receipt?> FindReceiptAsync(Guid receiptId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Receipt?> FindReceiptForPaymentAsync(Guid paymentId, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Receipt?> FindReceiptByBarcodeAsync(string barcodePayload, Guid organisationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<long> AllocateAsync(string sequenceKey, string scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task LockOrderInvoicesAsync(Guid orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid?> LockOrderInvoicesOfAsync(Guid invoiceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TOutcome>> InAllocationTransactionAsync<TOutcome>(Guid orderId, Func<CancellationToken, Task<Result<TOutcome>>> work, Func<CancellationToken, Task<bool>> committed, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result> SaveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
