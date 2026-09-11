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
/// </remarks>
public sealed record ReadyGateBlock
{
    /// <summary>
    /// The longest reference the column holds. Matches <see cref="ReadyGateInputs.MaximumReferenceLength"/>.
    /// </summary>
    public const int MaximumReferenceLength = 200;

    private ReadyGateBlock(ReadyGatePredicate predicate, string? reference)
    {
        Predicate = predicate;
        Reference = reference;
    }

    /// <summary>Which predicate failed. The member name is the reason code.</summary>
    public ReadyGatePredicate Predicate { get; }

    /// <summary>
    /// What it names, where it names something: a phase code, a defect code, an evidence key, a hold reason code,
    /// a sibling job number or a reconciliation case reference. <strong>Never personal data</strong> — security
    /// rule 7 keeps customer names, measurements and contact details out of anything a screen, a log or a trace
    /// can carry.
    /// </summary>
    public string? Reference { get; }

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

        return trimmed is { Length: > MaximumReferenceLength }
            ? Result.Failure<ReadyGateBlock>(OrdersErrors.TooLong("reference", MaximumReferenceLength))
            : Result.Success(new ReadyGateBlock(predicate, trimmed));
    }

    /// <summary>
    /// Builds a block the gate itself reached. Internal, and called only where the predicate is a literal of this
    /// assembly and the reference has already been bounded by <see cref="ReadyGateInputs"/> or by the value
    /// object it came from.
    /// </summary>
    /// <param name="predicate">Which predicate failed.</param>
    /// <param name="reference">What it names, trimmed to null when blank.</param>
    /// <returns>The block.</returns>
    internal static ReadyGateBlock Of(ReadyGatePredicate predicate, string? reference)
        => new(predicate, Trimmed(reference));

    /// <summary>A blank reference is no reference rather than an empty one on the screen.</summary>
    private static string? Trimmed(string? reference)
    {
        var trimmed = reference?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
