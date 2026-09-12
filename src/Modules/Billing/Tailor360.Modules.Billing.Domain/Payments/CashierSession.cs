using System.Globalization;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Payments;

/// <summary>
/// A cashier's shift at a branch, from the float going into the drawer to the count that closes it
/// (<c>docs/architecture/invariants.md</c> section 4.12). One open session per cashier per branch is a
/// partial unique index, not a check here (INV-CSH-01); closing is a conditional update, so a double
/// close is a conflict rather than a second close record; a closed session never changes (INV-CSH-05):
/// a later correction is a new adjusting record, and where money moved, a compensating payment.
/// </summary>
/// <remarks>
/// The close computes the expected total per mode from what the session recorded — the handler hands
/// those in, because the payments are the store's to sum — and records the counted totals and the
/// denomination sheet. A variance beyond the threshold needs a reason (INV-CSH-03); its approval by a
/// different user is a separate, audited command (INV-CSH-04, E09-F03-3). Amounts are held as
/// <see cref="Money"/> so that a session's figures can never be added to another currency's.
/// </remarks>
public sealed class CashierSession
{
    /// <summary>The longest reason a variance may carry — the same as an invoice's.</summary>
    public const int MaximumReasonLength = Invoice.MaximumReasonLength;

    /// <summary>
    /// The largest amount a session may carry in any figure: what a <c>numeric(18, 4)</c> column holds,
    /// fourteen integer digits, so a figure the ledger cannot store is refused here rather than by the
    /// database as an error nobody can read.
    /// </summary>
    public const decimal MaximumAmount = 99_999_999_999_999.99m;

    private readonly List<CashierSessionCount> _counts = [];
    private readonly List<CashierSessionModeTotal> _modeTotals = [];

    private CashierSession()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CashierSession(Guid id, Guid organisationId, Guid branchId, Guid cashierId, Money openingFloat, DateTimeOffset now)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CashierId = cashierId;
        Status = CashierSessionStatus.Open;
        OpeningFloat = openingFloat;
        ExpectedTotal = Money.Zero;
        CountedTotal = Money.Zero;
        Variance = Money.Zero;
        OpenedAt = now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch whose drawer this is.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The cashier accountable for it. Only they close it.</summary>
    public Guid CashierId { get; private set; }

    /// <summary>Open or closed.</summary>
    public CashierSessionStatus Status { get; private set; }

    /// <summary>The cash put in the drawer at opening.</summary>
    public Money OpeningFloat { get; private set; }

    /// <summary>The sum over every mode of what the session should hold; zero until closed.</summary>
    public Money ExpectedTotal { get; private set; }

    /// <summary>The sum over every mode of what was counted; zero until closed.</summary>
    public Money CountedTotal { get; private set; }

    /// <summary>Counted minus expected, over every mode; zero until closed.</summary>
    public Money Variance { get; private set; }

    /// <summary>Why the count differs from the expectation, where it does.</summary>
    public string? VarianceReason { get; private set; }

    /// <summary>When it opened.</summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>When it closed; null while open.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Who closed it.</summary>
    public Guid? ClosedBy { get; private set; }

    /// <summary>The denomination sheet, written at close.</summary>
    public IReadOnlyCollection<CashierSessionCount> Counts => _counts;

    /// <summary>Expected against counted per mode, written at close.</summary>
    public IReadOnlyCollection<CashierSessionModeTotal> ModeTotals => _modeTotals;

    /// <summary>
    /// The reconciliation batch opened when this session closed (INV-CSH-06); null until then. Set by
    /// EF Core's relationship fixup when the close adds the batch to the same unit of work, exactly as
    /// <see cref="Payment.Reversal"/> is — nothing here ever assigns it.
    /// </summary>
    public ReconciliationBatch? ReconciliationBatch { get; private set; }

    /// <summary>When the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the row last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether payments may still be recorded in it.</summary>
    public bool IsOpen => Status == CashierSessionStatus.Open;

    /// <summary>Opens a session with the float that went into the drawer.</summary>
    public static Result<CashierSession> Open(Guid id, Guid organisationId, Guid branchId, Guid cashierId, Money openingFloat, DateTimeOffset now)
    {
        if (openingFloat.IsNegative || !IsToThePaisa(openingFloat.Amount) || openingFloat.Amount > MaximumAmount)
        {
            return Result.Failure<CashierSession>(BillingErrors.AmountNotWellFormed("openingFloat"));
        }

        if (!string.Equals(openingFloat.Currency, Money.IndianRupee, StringComparison.Ordinal))
        {
            return Result.Failure<CashierSession>(BillingErrors.AmountNotWellFormed("openingFloat"));
        }

        return Result.Success(new CashierSession(id, organisationId, branchId, cashierId, openingFloat, now));
    }

    /// <summary>
    /// Closes the session against the count sheet and the counted totals. <paramref name="expectedByMode"/>
    /// names every mode the session may hold money in, with what it should hold — the float counted into
    /// cash — and decides which mode codes are known; a mode absent from the counts is counted as zero.
    /// </summary>
    /// <param name="counts">The denomination sheet.</param>
    /// <param name="counted">What was counted per mode other than cash; a cash line, if given, must agree with the sheet.</param>
    /// <param name="expectedByMode">The expected total per known mode, keyed by mode code.</param>
    /// <param name="reason">Why the count differs, where it does beyond the threshold.</param>
    /// <param name="varianceThreshold">The largest per-mode variance, over or short, that needs no reason.</param>
    /// <param name="now">When.</param>
    /// <param name="by">Who; must be the session's cashier.</param>
    public Result Close(
        IReadOnlyList<DenominationCount> counts,
        IReadOnlyList<ModeCount> counted,
        IReadOnlyDictionary<string, Money> expectedByMode,
        string? reason,
        Money varianceThreshold,
        DateTimeOffset now,
        Guid by)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(counted);
        ArgumentNullException.ThrowIfNull(expectedByMode);

        if (!IsOpen)
        {
            return Result.Failure(BillingErrors.CashierSessionAlreadyClosed);
        }

        if (by != CashierId)
        {
            return Result.Failure(BillingErrors.CashierSessionNotYours);
        }

        var sheet = CheckSheet(counts);
        if (sheet.IsFailure)
        {
            return Result.Failure(sheet.Error);
        }

        var lines = CheckCounted(counted, expectedByMode, sheet.Value);
        if (lines.IsFailure)
        {
            return Result.Failure(lines.Error);
        }

        var beyondThreshold = lines.Value.Any(line => Math.Abs(line.Counted - line.Expected) > varianceThreshold.Amount);
        var trimmedReason = reason?.Trim();
        if (beyondThreshold && string.IsNullOrEmpty(trimmedReason))
        {
            return Result.Failure(BillingErrors.VarianceReasonRequired);
        }

        if (trimmedReason is { Length: > MaximumReasonLength })
        {
            return Result.Failure(BillingErrors.TooLong("reason", MaximumReasonLength));
        }

        if (trimmedReason is not null && trimmedReason.Any(char.IsControl))
        {
            return Result.Failure(BillingErrors.ReasonNotWellFormed("reason"));
        }

        _counts.AddRange(sheet.Value.Select(count => CashierSessionCount.Of(Id, count.Denomination, count.Quantity)));
        _modeTotals.AddRange(lines.Value.Select(line => CashierSessionModeTotal.Of(Id, line.ModeCode, line.Expected, line.Counted)));
        ExpectedTotal = Money.Rupees(lines.Value.Sum(line => line.Expected));
        CountedTotal = Money.Rupees(lines.Value.Sum(line => line.Counted));
        Variance = CountedTotal - ExpectedTotal;
        VarianceReason = string.IsNullOrEmpty(trimmedReason) ? null : trimmedReason;
        Status = CashierSessionStatus.Closed;
        ClosedAt = now;
        ClosedBy = by;
        UpdatedAt = now;

        return Result.Success();
    }

    private static Result<IReadOnlyList<DenominationCount>> CheckSheet(IReadOnlyList<DenominationCount> counts)
    {
        var seen = new HashSet<decimal>();
        for (var index = 0; index < counts.Count; index++)
        {
            var count = counts[index];
            var field = string.Create(CultureInfo.InvariantCulture, $"denominations[{index}]");
            if (!CashDenominations.IsKnown(count.Denomination))
            {
                return Result.Failure<IReadOnlyList<DenominationCount>>(BillingErrors.DenominationNotKnown(field + ".denomination"));
            }

            if (count.Quantity < 0)
            {
                return Result.Failure<IReadOnlyList<DenominationCount>>(BillingErrors.CountNotWellFormed(field + ".quantity"));
            }

            if (!seen.Add(count.Denomination))
            {
                return Result.Failure<IReadOnlyList<DenominationCount>>(BillingErrors.LineKeyDuplicated(field + ".denomination"));
            }
        }

        return Result.Success<IReadOnlyList<DenominationCount>>(counts.OrderByDescending(count => count.Denomination).ToList());
    }

    private static Result<IReadOnlyList<(string ModeCode, decimal Expected, decimal Counted)>> CheckCounted(
        IReadOnlyList<ModeCount> counted,
        IReadOnlyDictionary<string, Money> expectedByMode,
        IReadOnlyList<DenominationCount> sheet)
    {
        // The sheet cannot exceed the ledger's bound: ten denominations at int.MaxValue each come to
        // under 6e12, and the bound is 1e14.
        var cashCounted = sheet.Sum(count => count.Denomination * count.Quantity);
        var countedByMode = new Dictionary<string, decimal>(StringComparer.Ordinal);
        for (var index = 0; index < counted.Count; index++)
        {
            var line = counted[index];
            var field = string.Create(CultureInfo.InvariantCulture, $"modeTotals[{index}]");
            var code = line.ModeCode?.Trim() ?? string.Empty;
            if (!expectedByMode.ContainsKey(code))
            {
                return Result.Failure<IReadOnlyList<(string, decimal, decimal)>>(BillingErrors.PaymentModeNotKnown(field + ".modeCode"));
            }

            if (line.Counted < 0m || !IsToThePaisa(line.Counted) || line.Counted > MaximumAmount)
            {
                return Result.Failure<IReadOnlyList<(string, decimal, decimal)>>(BillingErrors.AmountNotWellFormed(field + ".counted"));
            }

            if (!countedByMode.TryAdd(code, line.Counted))
            {
                return Result.Failure<IReadOnlyList<(string, decimal, decimal)>>(BillingErrors.LineKeyDuplicated(field + ".modeCode"));
            }

            // The sheet is the cash count; a cash line that says otherwise is a mistake, not a second opinion.
            if (string.Equals(code, PaymentModeCodes.Cash, StringComparison.Ordinal) && line.Counted != cashCounted)
            {
                return Result.Failure<IReadOnlyList<(string, decimal, decimal)>>(BillingErrors.CashCountMismatch(field + ".counted"));
            }
        }

        var lines = new List<(string ModeCode, decimal Expected, decimal Counted)>();
        foreach (var (code, expected) in expectedByMode.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var isCash = string.Equals(code, PaymentModeCodes.Cash, StringComparison.Ordinal);
            decimal value = isCash ? cashCounted : countedByMode.GetValueOrDefault(code, 0m);
            lines.Add((code, expected.Amount, value));
        }

        return Result.Success<IReadOnlyList<(string ModeCode, decimal Expected, decimal Counted)>>(lines);
    }

    private static bool IsToThePaisa(decimal amount) => decimal.Round(amount, Money.DocumentScale) == amount;
}
