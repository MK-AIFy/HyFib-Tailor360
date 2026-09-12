using System.Globalization;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Domain.Invoicing;

/// <summary>
/// The display numbers Billing allocates at posting (<c>docs/architecture/conventions.md</c> section 3.2):
/// <c>INV-&lt;branch&gt;-&lt;FY&gt;-000001</c> for an invoice — interim, confirmed with the accountant under
/// issue #42 — <c>CN-</c> / <c>DN-</c> for the notes and <c>RCPT-</c> for a receipt. The financial year is written as Orders writes it
/// on an order number, four digits for the year it opens in and the year it closes in (<c>2627</c> for
/// 2026-27), so a counter reading an invoice beside its order sees one convention.
/// </summary>
public static class DocumentNumbers
{
    /// <summary>The invoice prefix.</summary>
    public const string InvoicePrefix = "INV";

    /// <summary>The credit-note prefix.</summary>
    public const string CreditNotePrefix = "CN";

    /// <summary>The debit-note prefix.</summary>
    public const string DebitNotePrefix = "DN";

    /// <summary>The receipt prefix: <c>RCPT</c> for the number, as the go-live plan and the walkthroughs write it; <c>R-</c> is the barcode's namespace.</summary>
    public const string ReceiptPrefix = "RCPT";

    /// <summary>The sequence an invoice number is drawn from.</summary>
    public const string InvoiceSequence = "invoice";

    /// <summary>The sequence a credit-note number is drawn from.</summary>
    public const string CreditNoteSequence = "credit-note";

    /// <summary>The sequence a debit-note number is drawn from.</summary>
    public const string DebitNoteSequence = "debit-note";

    /// <summary>The sequence a receipt number is drawn from: gapless like the invoice's, inside the payment's transaction.</summary>
    public const string ReceiptSequence = "receipt";

    /// <summary>The separator between the parts.</summary>
    public const char Separator = '-';

    /// <summary>The digits of the running number.</summary>
    public const int SequenceDigits = 6;

    /// <summary>A branch code is upper-case letters and digits, at most this many.</summary>
    public const int MaximumBranchCodeLength = 16;

    /// <summary>The longest number any prefix, branch and running number compose to.</summary>
    public const int MaximumLength = 40;

    /// <summary>
    /// The financial year a branch-local date falls in, as the four-digit token a number carries:
    /// the two closing digits of the year it opens in, then of the year it closes in.
    /// </summary>
    /// <param name="branchLocalDate">The date, in the branch's own calendar.</param>
    public static string FinancialYearToken(DateOnly branchLocalDate)
    {
        var opens = IndiaTimeZone.FinancialYearStarting(branchLocalDate);
        return string.Create(CultureInfo.InvariantCulture, $"{opens % 100:D2}{(opens + 1) % 100:D2}");
    }

    /// <summary>
    /// The scope a sequence is numbered within: the organisation, the branch and the financial year. The
    /// organisation is part of it because a branch code is unique only within its organisation, and a
    /// statutory series shared by two organisations' <c>MAIN</c> branches would show each of them gaps.
    /// </summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchCode">The branch's code, as the register writes it.</param>
    /// <param name="financialYearToken">The financial year, from <see cref="FinancialYearToken"/>.</param>
    public static string SequenceScope(Guid organisationId, string branchCode, string financialYearToken)
        => string.Create(CultureInfo.InvariantCulture, $"{organisationId:N}:{branchCode}{Separator}{financialYearToken}");

    /// <summary>Folds and checks a branch code as the register writes it.</summary>
    /// <param name="branchCode">The code.</param>
    public static Result<string> NormaliseBranchCode(string? branchCode)
    {
        var folded = branchCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(folded))
        {
            return Result.Failure<string>(BillingErrors.Required("branchCode"));
        }

        return folded.Length <= MaximumBranchCodeLength && folded.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character))
            ? Result.Success(folded)
            : Result.Failure<string>(BillingErrors.BranchCodeNotWellFormed);
    }

    /// <summary>Composes a number from its parts.</summary>
    /// <param name="prefix">The document prefix.</param>
    /// <param name="branchCode">The branch code, normalised.</param>
    /// <param name="financialYearToken">The financial year token.</param>
    /// <param name="sequence">The running number, from one.</param>
    public static Result<string> Compose(string prefix, string branchCode, string financialYearToken, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(financialYearToken);

        var branch = NormaliseBranchCode(branchCode);
        if (branch.IsFailure)
        {
            return branch;
        }

        if (sequence < 1)
        {
            return Result.Failure<string>(BillingErrors.SequenceOutOfRange);
        }

        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}{Separator}{branch.Value}{Separator}{financialYearToken}{Separator}{sequence.ToString(CultureInfo.InvariantCulture).PadLeft(SequenceDigits, '0')}");

        return value.Length > MaximumLength
            ? Result.Failure<string>(BillingErrors.TooLong("documentNumber", MaximumLength))
            : Result.Success(value);
    }
}
