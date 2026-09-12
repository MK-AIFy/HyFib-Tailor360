using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>Turns what the engine produced into what an invoice keeps. One mapping, in one place.</summary>
public static class InvoiceLines
{
    /// <summary>
    /// The lines, each resolved to a garment job of the order by its key. A caller keys a line by the
    /// garment job's identity — that is the convention Orders follows — so a key that is not one of the
    /// order's jobs is refused rather than guessed at.
    /// </summary>
    public static Result<IReadOnlyList<InvoicedLine>> From(PricingResult result, OrderFact order)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(order);

        var lines = new List<InvoicedLine>(result.Lines.Count);
        foreach (var line in result.Lines)
        {
            if (!Guid.TryParse(line.LineKey, out var jobId) || order.FindJob(jobId) is not { } job)
            {
                return Result.Failure<IReadOnlyList<InvoicedLine>>(BillingErrors.LineNotAGarmentJob($"lines[{line.LineKey}].lineKey"));
            }

            if (job.IsCancelled)
            {
                return Result.Failure<IReadOnlyList<InvoicedLine>>(BillingErrors.JobCancelled($"lines[{line.LineKey}].lineKey"));
            }

            lines.Add(new InvoicedLine(
                jobId,
                line.LineKey,
                line.ItemCode,
                line.Description,
                line.Quantity,
                line.CatalogueRate,
                line.AppliedRate,
                line.Base,
                [.. line.Surcharges.Select(surcharge => new InvoicedSurcharge(surcharge.ItemCode, surcharge.Description, surcharge.Rate, surcharge.Amount))],
                line.Discount?.RuleCode,
                line.Discount?.Kind,
                line.Discount?.Value,
                line.Discount?.Amount ?? Platform.Abstractions.Money.Money.Zero,
                line.Gross,
                line.TaxableValue,
                line.TaxCode,
                line.Classification,
                line.TaxCodeKind,
                [.. line.Taxes.Select(tax => new InvoicedTax(tax.Kind, tax.RatePercent, tax.Amount))],
                line.TaxTotal,
                line.LineTotal,
                line.Variance));
        }

        return Result.Success<IReadOnlyList<InvoicedLine>>(lines);
    }

    /// <summary>The totals as the invoice keeps them.</summary>
    /// <summary>
    /// The garment jobs a set of pricing lines would charge for, refused on the same grounds
    /// <see cref="From"/> refuses them — before anything is priced or stored.
    /// </summary>
    /// <param name="lines">The lines about to be priced.</param>
    /// <param name="order">The order the invoice is for.</param>
    /// <returns>The jobs, in line order.</returns>
    public static Result<IReadOnlyList<Guid>> JobsOf(IReadOnlyList<PricingLineRequest> lines, OrderFact order)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(order);

        var jobs = new List<Guid>(lines.Count);
        foreach (var line in lines)
        {
            if (!Guid.TryParse(line.LineKey, out var jobId))
            {
                return Result.Failure<IReadOnlyList<Guid>>(BillingErrors.LineNotAGarmentJob($"lines[{line.LineKey}].lineKey"));
            }

            var checkedJob = CheckJob(jobId, order, $"lines[{line.LineKey}].lineKey");
            if (checkedJob.IsFailure)
            {
                return Result.Failure<IReadOnlyList<Guid>>(checkedJob.Error);
            }

            if (jobs.Contains(jobId))
            {
                return Result.Failure<IReadOnlyList<Guid>>(BillingErrors.JobRepeated($"lines[{line.LineKey}].lineKey"));
            }

            jobs.Add(jobId);
        }

        return Result.Success<IReadOnlyList<Guid>>(jobs);
    }

    /// <summary>A garment job the invoice charges for is a live job of the order, now — checked again at posting.</summary>
    /// <param name="garmentJobId">The job.</param>
    /// <param name="order">The order as Billing knows it.</param>
    /// <param name="field">The field named in a refusal.</param>
    public static Result CheckJob(Guid garmentJobId, OrderFact order, string field)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.FindJob(garmentJobId) is not { } job)
        {
            return Result.Failure(BillingErrors.LineNotAGarmentJob(field));
        }

        return job.IsCancelled ? Result.Failure(BillingErrors.JobCancelled(field)) : Result.Success();
    }

    public static InvoiceTotals TotalsOf(PricingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var totals = result.Totals;

        return new InvoiceTotals(
            totals.Subtotal, totals.DiscountTotal, totals.TaxableValue, totals.CentralTax, totals.StateTax,
            totals.IntegratedTax, totals.Cess, totals.RoundOff, totals.GrandTotal);
    }
}
