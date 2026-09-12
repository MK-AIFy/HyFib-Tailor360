using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Sequencing;

namespace Tailor360.Modules.Billing.Infrastructure.Payments;

/// <summary>EF Core persistence for <see cref="Payment"/>, its allocations and its advance.</summary>
public sealed class PaymentStore(BillingDbContext context, ITransactionalSequenceAllocator sequences) : IPaymentStore
{
    /// <inheritdoc />
    public Task<Payment?> FindAsync(Guid paymentId, Guid organisationId, CancellationToken cancellationToken = default)
        => context.Payments.SingleOrDefaultAsync(payment => payment.Id == paymentId && payment.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Payment>> ListForOrderAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.Payments
            .Where(payment => payment.OrderId == orderId && payment.OrganisationId == organisationId)
            .OrderBy(payment => payment.RecordedAt)
            .ThenBy(payment => payment.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, Money>> AllocatedByInvoiceAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoiceIds);

        if (invoiceIds.Count == 0)
        {
            return new Dictionary<Guid, Money>();
        }

        var wanted = invoiceIds.ToHashSet();
        var sums = await context.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => wanted.Contains(allocation.InvoiceId))
            .GroupBy(allocation => allocation.InvoiceId)
            .Select(group => new { InvoiceId = group.Key, Amount = group.Sum(allocation => allocation.Amount.Amount) })
            .ToListAsync(cancellationToken);

        return sums.ToDictionary(sum => sum.InvoiceId, sum => Money.Rupees(sum.Amount));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, Money>> TakenByModeAsync(Guid cashierSessionId, CancellationToken cancellationToken = default)
    {
        var sums = await context.Payments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(payment => payment.CashierSessionId == cashierSessionId)
            .GroupBy(payment => payment.ModeCode)
            .Select(group => new { ModeCode = group.Key, Amount = group.Sum(payment => payment.Amount.Amount) })
            .ToListAsync(cancellationToken);

        return sums.ToDictionary(sum => sum.ModeCode, sum => Money.Rupees(sum.Amount), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public void Add(Payment payment) => context.Payments.Add(payment);

    /// <inheritdoc />
    public void AddReceipt(Receipt receipt) => context.Receipts.Add(receipt);

    /// <inheritdoc />
    public Task<Receipt?> FindReceiptAsync(Guid receiptId, Guid organisationId, CancellationToken cancellationToken = default)
        => context.Receipts.SingleOrDefaultAsync(receipt => receipt.Id == receiptId && receipt.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public Task<Receipt?> FindReceiptForPaymentAsync(Guid paymentId, Guid organisationId, CancellationToken cancellationToken = default)
        => context.Receipts.SingleOrDefaultAsync(receipt => receipt.PaymentId == paymentId && receipt.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public Task<Receipt?> FindReceiptByBarcodeAsync(string barcodePayload, Guid organisationId, CancellationToken cancellationToken = default)
        => context.Receipts.SingleOrDefaultAsync(receipt => receipt.BarcodePayload == barcodePayload && receipt.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public Task<long> AllocateAsync(string sequenceKey, string scope, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A receipt number is drawn inside the transaction that records the payment.");

        return sequences.NextAsync(sequenceKey, scope, transaction.GetDbTransaction(), cancellationToken);
    }

    /// <inheritdoc />
    public Task LockOrderInvoicesAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();

        // Every invoice of the order, drafts included: a draft being posted is already held by its
        // posting, so this waits for the post to commit and then reads it as posted. FOR NO KEY UPDATE,
        // so the hold neither fires an immutability trigger nor blocks a reader.
        return context.Database.ExecuteSqlAsync($"SELECT id FROM billing.invoices WHERE order_id = {orderId} FOR NO KEY UPDATE", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Guid?> LockOrderInvoicesOfAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();

        var held = await context.Database
            .SqlQuery<Guid>($"SELECT order_id AS \"Value\" FROM billing.invoices WHERE order_id = (SELECT order_id FROM billing.invoices WHERE id = {invoiceId}) FOR NO KEY UPDATE")
            .ToListAsync(cancellationToken);

        return held.Count > 0 ? held[0] : null;
    }

    /// <inheritdoc />
    public async Task<Result<TOutcome>> InAllocationTransactionAsync<TOutcome>(
        Guid orderId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        Func<CancellationToken, Task<bool>> committed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(committed);

        var strategy = context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteInTransactionAsync(
                async token =>
                {
                    context.ChangeTracker.Clear();
                    await LockOrderInvoicesAsync(orderId, token);

                    var outcome = await work(token);
                    if (outcome.IsFailure)
                    {
                        throw new AttemptRefusedException(outcome.Error);
                    }

                    return outcome;
                },
                async token =>
                {
                    // Asked only when the commit's outcome is unknown, over the database and not over the
                    // attempt's own tracked graph, which still holds what it tried to write.
                    context.ChangeTracker.Clear();
                    return await committed(token);
                },
                cancellationToken);
        }
        catch (AttemptRefusedException refused)
        {
            return Result.Failure<TOutcome>(refused.Error);
        }
    }

    /// <inheritdoc />
    public Task<TResult> ReadConsistentlyAsync<TResult>(Func<CancellationToken, Task<TResult>> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);

        var strategy = context.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async token =>
        {
            // REPEATABLE READ is a snapshot in PostgreSQL: every statement in the transaction sees the
            // database as it stood at the first, and nothing here writes, so nothing here can conflict.
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token);
            var result = await read(token);
            await transaction.CommitAsync(token);
            return result;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: BillingDbContext.OnePaymentPerModeReferenceIndex,
            })
        {
            return Result.Failure(BillingErrors.PaymentReferenceDuplicated);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: BillingDbContext.OnePaymentPerClientKeyIndex,
            })
        {
            // The request's twin got in first, past the idempotency record: the row's own guard.
            return Result.Failure(BillingErrors.PaymentDuplicated);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: BillingDbContext.OneReceiptPerNumberIndex or BillingDbContext.OneReceiptPerBarcodeIndex or BillingDbContext.OneReceiptPerPaymentIndex,
            })
        {
            // Unreachable while the sequence row is held and the payload is minted per command; answered
            // as a conflict rather than a five hundred if it ever is.
            return Result.Failure(BillingErrors.ReceiptNumberTaken);
        }
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The order's invoices are held inside the transaction that allocates against them.");
        }
    }

    private sealed class AttemptRefusedException(Error error) : Exception(error.Code)
    {
        public Error Error { get; } = error;
    }
}
