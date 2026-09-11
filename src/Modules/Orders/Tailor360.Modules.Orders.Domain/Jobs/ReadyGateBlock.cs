using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// One failing ready-gate predicate, and the thing it names.
/// </summary>
/// <remarks>
/// <para>
/// This is what the delivery-queue screen shows instead of a bare refusal. INV-JOB-07 requires each predicate to
/// return its own reason code; a reason code on its own would still leave the reader asking <em>which</em> phase,
/// <em>which</em> sibling, <em>which</em> hold — so the reference travels with it.
/// </para>
/// <para>
/// <strong>Built through a factory rather than positionally</strong>, as every other value object in this module
/// is. <see cref="Predicate"/> is one of the six reason codes <c>docs/prd/state-transitions.md</c> section 9.1
/// names, and an enumeration in C# accepts any number a cast can produce — which is what a deserialiser does with
/// a value it did not recognise. A block is a persisted row on <c>garment_jobs.ready_state_blocks</c> and a line on
/// a screen, so an unnamed seventh reason code is one nobody can act on; <see cref="Create"/> refuses it rather
/// than storing it. <see cref="Of"/> is the internal route <see cref="ReadyGate"/> and <c>GarmentJob.Hold</c>
/// take, where the predicate is a literal in this assembly and cannot be anything else.
/// </para>
/// <para>
/// <strong>The reference is bounded here by what the boundary can carry, and that is the point.</strong> A block
/// is published to Custody, Billing and Reporting on every ready-state read as
/// <c>Orders.Contracts.ReadyStateBlock</c>, whose factory refuses a reference that is longer than
/// <see cref="MaximumReferenceLength"/>, parses as a GUID, or carries a character outside an ASCII letter, a
/// digit, a hyphen and an underscore — and refuses it by <em>throwing</em>. Until this type agreed with it, a
/// legitimately stored row could make <c>IOrderSnapshotQuery.GetJobAsync</c> throw at a module boundary, and the
/// projection worked around that by copying the contract's predicate and silently dropping any reference that
/// failed it: a rule duplicated in two assemblies, and a Tailor Master told which of six things blocked the
/// garment but not which phase. <see cref="IsCarriable"/> is the single rule, applied on the write path — here,
/// on <see cref="ReadyGateInputs"/> and on <c>GarmentJob</c>'s reason codes — so the two sides agree by
/// construction rather than by a copied predicate, and the projection publishes the reference unchanged.
/// </para>
/// <para>
/// <strong>Which of the two rules was in the wrong is worth recording.</strong> Section 9.1 names all six
/// references — an incomplete phase, a failed QC result "as the one reference a block carries rather than a list
/// of criteria and defect codes", what is missing, the hold reason code, the sibling job number, the open
/// reconciliation case — and not one of them is free text and not one of them is an identity, so the contract's
/// <em>shape</em> rule was right and this type's two hundred characters of anything were wrong. The
/// <em>length</em> was the other way round: the contract set forty, Catalog's code length, while the same field
/// also carries a garment job number, whose own column is forty-eight
/// (<see cref="DisplayNumbers.GarmentJobNumber.MaximumLength"/>). Forty-eight is therefore the shared bound, and
/// it is the contract that moved to meet it.
/// </para>
/// </remarks>
public sealed record ReadyGateBlock
{
    /// <summary>
    /// The longest reference a block carries.
    /// </summary>
    /// <remarks>
    /// The longer of the two things it can be: a configured code, which Catalog bounds at forty, and a garment
    /// job number, which <see cref="DisplayNumbers.GarmentJobNumber.MaximumLength"/> bounds at forty-eight. It
    /// matches <see cref="ReadyGateInputs.MaximumReferenceLength"/>, because every reference there is carried
    /// straight through onto a block, and <c>Orders.Contracts.ReadyStateBlock.MaximumReferenceLength</c>, because
    /// a block that could not be published would be one this module had stored and could not answer with.
    /// </remarks>
    public const int MaximumReferenceLength = 48;

    private ReadyGateBlock(ReadyGatePredicate predicate, string? reference)
    {
        Predicate = predicate;
        Reference = reference;
    }

    /// <summary>Which predicate failed. The member name is the reason code.</summary>
    public ReadyGatePredicate Predicate { get; }

    /// <summary>
    /// What it names, where it names something: a phase code, a defect code, an evidence <em>kind</em> code, a
    /// hold reason code, a sibling job number or a reconciliation case number. A configured code or a display
    /// number and nothing else — <strong>never an identity</strong>, so never a media object identifier, an
    /// evidence key or an object-storage key (security rule 9), and <strong>never personal data</strong>, so
    /// never a name, a measurement or a contact detail (security rule 7). <see cref="IsCarriable"/> is the rule,
    /// and it is enforced on every route in.
    /// </summary>
    public string? Reference { get; }

    /// <summary>
    /// Whether a reference is one a block may carry, and therefore one the published contract will accept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single statement of the rule. ARCH-001 forbids this project referencing
    /// <c>Tailor360.Modules.Orders.Contracts</c>, so the published type states the same rule in its own
    /// assembly — the arrangement <c>DisplayNumberFormat.MaximumBranchCodeLength</c> already uses for Identity's
    /// branch code — and a unit test holds the two together rather than a comment asking a reader to.
    /// </para>
    /// <para>
    /// No reference at all is carriable: a predicate that names nothing blocks just the same, and the screen
    /// shows the reason code alone rather than refusing to render.
    /// </para>
    /// </remarks>
    /// <param name="reference">The candidate, trimmed or not.</param>
    /// <returns>True when a block, and the contract built from it, will carry it.</returns>
    public static bool IsCarriable(string? reference)
    {
        var trimmed = reference?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (trimmed.Length > MaximumReferenceLength || Guid.TryParse(trimmed, out _))
        {
            return false;
        }

        return char.IsAsciiLetterOrDigit(trimmed[0])
               && trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    /// <summary>
    /// Builds a block from a predicate that may be anything a cast produced — a rehydrated row, or a caller
    /// outside this assembly.
    /// </summary>
    /// <param name="predicate">Which predicate failed. Must be one of the six section 9.1 names.</param>
    /// <param name="reference">What it names, trimmed to null when blank.</param>
    /// <returns>The block, or the reason it was refused.</returns>
    public static Result<ReadyGateBlock> Create(ReadyGatePredicate predicate, string? reference)
    {
        if (!Enum.IsDefined(predicate))
        {
            return Result.Failure<ReadyGateBlock>(OrdersErrors.NotUnderstood("predicate"));
        }

        var trimmed = Trimmed(reference);

        if (trimmed is { Length: > MaximumReferenceLength })
        {
            return Result.Failure<ReadyGateBlock>(OrdersErrors.TooLong("reference", MaximumReferenceLength));
        }

        return IsCarriable(trimmed)
            ? Result.Success(new ReadyGateBlock(predicate, trimmed))
            : Result.Failure<ReadyGateBlock>(OrdersErrors.ReferenceNotCarriable("reference"));
    }

    /// <summary>
    /// Builds a block the gate itself reached. Internal, and called only where the predicate is a literal of this
    /// assembly and the reference has already been validated by the value object it came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There are exactly six references that reach here, and every one is bounded before it does:
    /// <see cref="ReadyGateInputs"/> validates the incomplete phase code, the failed QC reference, the
    /// missing-evidence reference and the open custody case reference against <see cref="IsCarriable"/> when the
    /// application gathers them; <c>GarmentJob.ReasonCode</c> validates the hold reason code against the same
    /// rule; and a sibling's <c>GarmentJobNumber</c> is <c>J-&lt;branch&gt;-&lt;FY&gt;-000001-01</c>, which
    /// conforms by its own pattern and is bounded by the same forty-eight characters.
    /// </para>
    /// <para>
    /// So it <strong>throws</strong> rather than folding a reference it may not carry to null. A silent fold is
    /// what this change removed, and it is the wrong answer twice over: the reference is lost where nobody can
    /// see it, and the guarantee those call sites are supposed to give is never tested. A throw here is a
    /// producer defect inside this assembly, reported the way <c>Orders.Contracts.ReadyStateBlock.Of</c> reports
    /// the same class of defect, and it is unreachable while the sources hold — which a unit test asserts.
    /// </para>
    /// </remarks>
    /// <param name="predicate">Which predicate failed.</param>
    /// <param name="reference">What it names, trimmed to null when blank.</param>
    /// <returns>The block.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="reference"/> is not one <see cref="IsCarriable"/> accepts, which is a defect in the caller
    /// rather than a situation.
    /// </exception>
    internal static ReadyGateBlock Of(ReadyGatePredicate predicate, string? reference)
    {
        var trimmed = Trimmed(reference);

        if (!IsCarriable(trimmed))
        {
            throw new ArgumentException(
                "A ready-gate block reference is a configured code or a display number, and every reference the "
                + "gate builds one from is validated before it arrives here.",
                nameof(reference));
        }

        return new ReadyGateBlock(predicate, trimmed);
    }

    /// <summary>A blank reference is no reference rather than an empty one on the screen.</summary>
    private static string? Trimmed(string? reference)
    {
        var trimmed = reference?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
