using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Deduplication;

/// <summary>
/// What a person decided about one scored duplicate suspicion.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A score never decides anything.</strong> <see cref="DuplicateScoring"/> raises the
/// suspicion and explains it; this records what a human then did about it —
/// <c>docs/prd/workflows/blouse.md</c>: "A merge is irreversible and authorised; it is never performed
/// automatically by a score." Both outcomes are worth keeping. Knowing that somebody looked at two
/// records and judged them different people is what stops the same pair being raised for ever, and it
/// is the evidence <c>docs/prd/exceptions.md</c> EX-01 asks for when a second record is created
/// deliberately.
/// </para>
/// <para>
/// <strong>The reasons are stored by name.</strong> A row written today is read after the enumeration
/// has grown, and storing ordinals would let a later reordering silently re-explain a decision
/// somebody already took. This is the same choice the communication preferences made for channels.
/// </para>
/// <para>
/// <strong>There is deliberately no uniqueness over the pair and no trigger.</strong> The same two
/// records can be raised, dismissed, raised again and finally merged, and each of those is a separate
/// decision by a separate person on a separate day. Erasure (#57) must also be able to remove these
/// rows outright, because <see cref="SubjectCustomerId"/> and <see cref="CandidateCustomerId"/> say
/// that two named people were once thought to be one — which is an assertion about them, not about
/// the shop's own operations, and so is not evidence the shop is entitled to keep for ever.
/// </para>
/// </remarks>
public sealed class DuplicateCandidateDecision
{
    private readonly List<DuplicateReason> _reasons = [];

    private DuplicateCandidateDecision()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private DuplicateCandidateDecision(
        Guid id,
        Guid organisationId,
        Guid subjectCustomerId,
        Guid candidateCustomerId,
        DuplicateMatch match,
        DuplicateDecision decision,
        Guid? mergeId,
        Guid? branchId,
        DateTimeOffset decidedAt,
        Guid? decidedBy)
    {
        Id = id;
        OrganisationId = organisationId;
        SubjectCustomerId = subjectCustomerId;
        CandidateCustomerId = candidateCustomerId;
        Confidence = match.Confidence;
        _reasons.AddRange(match.Reasons);
        Decision = decision;
        MergeId = mergeId;
        BranchId = branchId;
        DecidedAt = decidedAt;
        DecidedBy = decidedBy;
    }

    /// <summary>Identity of the decision. A UUIDv7.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation both records belong to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>
    /// The record the decision was taken from: the one just created, or the one chosen to survive.
    /// </summary>
    public Guid SubjectCustomerId { get; private set; }

    /// <summary>The record that resembled it.</summary>
    public Guid CandidateCustomerId { get; private set; }

    /// <summary>How strongly the two resembled each other when the decision was taken.</summary>
    public DuplicateConfidence Confidence { get; private set; }

    /// <summary>Why, in the terms the counter was shown.</summary>
    public IReadOnlyCollection<DuplicateReason> Reasons => _reasons;

    /// <summary>What the person decided.</summary>
    public DuplicateDecision Decision { get; private set; }

    /// <summary>The merge this decision produced, where it produced one.</summary>
    public Guid? MergeId { get; private set; }

    /// <summary>The branch the decision was taken at, where the session was working in one.</summary>
    public Guid? BranchId { get; private set; }

    /// <summary>When it was taken, in UTC.</summary>
    public DateTimeOffset DecidedAt { get; private set; }

    /// <summary>Who took it.</summary>
    public Guid? DecidedBy { get; private set; }

    /// <summary>
    /// Records that somebody read the candidates and created a new record anyway.
    /// </summary>
    /// <param name="id">Identity of the decision, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation both records belong to.</param>
    /// <param name="subjectCustomerId">The record that was created.</param>
    /// <param name="candidateCustomerId">The existing record it resembled.</param>
    /// <param name="match">The score and its reasons, as they were shown.</param>
    /// <param name="branchId">The branch it was created at.</param>
    /// <param name="decidedAt">The instant, from <c>IClock</c>.</param>
    /// <param name="decidedBy">The actor.</param>
    /// <returns>The decision, or the reason it was refused.</returns>
    public static Result<DuplicateCandidateDecision> CreatedNewAnyway(
        Guid id,
        Guid organisationId,
        Guid subjectCustomerId,
        Guid candidateCustomerId,
        DuplicateMatch match,
        Guid? branchId,
        DateTimeOffset decidedAt,
        Guid? decidedBy)
        => Create(
            id,
            organisationId,
            subjectCustomerId,
            candidateCustomerId,
            match,
            DuplicateDecision.CreatedNewAnyway,
            mergeId: null,
            branchId,
            decidedAt,
            decidedBy);

    /// <summary>
    /// Records that somebody judged the two records to be one person and merged them.
    /// </summary>
    /// <param name="id">Identity of the decision, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation both records belong to.</param>
    /// <param name="subjectCustomerId">The record that survived.</param>
    /// <param name="candidateCustomerId">The record that was folded in.</param>
    /// <param name="match">The score and its reasons, computed against the pair as merged.</param>
    /// <param name="mergeId">The merge decision this belongs to.</param>
    /// <param name="branchId">The branch it was taken at.</param>
    /// <param name="decidedAt">The instant, from <c>IClock</c>.</param>
    /// <param name="decidedBy">The actor.</param>
    /// <returns>The decision, or the reason it was refused.</returns>
    public static Result<DuplicateCandidateDecision> Merged(
        Guid id,
        Guid organisationId,
        Guid subjectCustomerId,
        Guid candidateCustomerId,
        DuplicateMatch match,
        Guid mergeId,
        Guid? branchId,
        DateTimeOffset decidedAt,
        Guid? decidedBy)
    {
        if (mergeId == Guid.Empty)
        {
            return Result.Failure<DuplicateCandidateDecision>(CustomersErrors.Required("mergeId"));
        }

        return Create(
            id,
            organisationId,
            subjectCustomerId,
            candidateCustomerId,
            match,
            DuplicateDecision.Merged,
            mergeId,
            branchId,
            decidedAt,
            decidedBy);
    }

    private static Result<DuplicateCandidateDecision> Create(
        Guid id,
        Guid organisationId,
        Guid subjectCustomerId,
        Guid candidateCustomerId,
        DuplicateMatch match,
        DuplicateDecision decision,
        Guid? mergeId,
        Guid? branchId,
        DateTimeOffset decidedAt,
        Guid? decidedBy)
    {
        ArgumentNullException.ThrowIfNull(match);

        if (id == Guid.Empty)
        {
            return Result.Failure<DuplicateCandidateDecision>(CustomersErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<DuplicateCandidateDecision>(
                CustomersErrors.Required("organisationId"));
        }

        if (subjectCustomerId == Guid.Empty || candidateCustomerId == Guid.Empty)
        {
            return Result.Failure<DuplicateCandidateDecision>(
                CustomersErrors.Required("customerId"));
        }

        // A decision about a record and itself would be a defect in whatever raised it, and stored it
        // would make the pair look reviewed for ever.
        if (subjectCustomerId == candidateCustomerId)
        {
            return Result.Failure<DuplicateCandidateDecision>(
                CustomersErrors.CannotMergeIntoItself);
        }

        return Result.Success(new DuplicateCandidateDecision(
            id,
            organisationId,
            subjectCustomerId,
            candidateCustomerId,
            match,
            decision,
            mergeId,
            branchId,
            decidedAt,
            decidedBy));
    }
}

/// <summary>
/// What a person decided about a scored duplicate suspicion.
/// </summary>
/// <remarks>
/// Two values, because there are two things a person can do that are worth keeping: create the second
/// record anyway, or merge. Closing the screen without deciding is neither, and is not recorded — a
/// stored "dismissed" that nobody chose would be indistinguishable from a considered judgement.
/// </remarks>
public enum DuplicateDecision
{
    /// <summary>
    /// The candidates were read and a new record was created regardless: a namesake, a relative
    /// sharing a household number, or two people the counter can tell apart and the score cannot.
    /// </summary>
    CreatedNewAnyway = 0,

    /// <summary>The two records were judged to be one person and were merged, irreversibly.</summary>
    Merged = 1,
}
