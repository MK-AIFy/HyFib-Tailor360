using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Customers.Application.Consent;

/// <summary>
/// Writes the consent trail.
/// </summary>
/// <remarks>
/// <para>
/// The order is the module's house rule and the reason is in <c>CustomerAudit</c>: the module's rows
/// are in the <c>customers</c> schema and the trail is in <c>platform</c>, so they are different
/// contexts and different transactions. <strong>Save the change first, then record it.</strong> The
/// trail may lag reality; it must never lead it.
/// </para>
/// <para>
/// <strong>What reaches the trail is exactly what section 5.3 permits.</strong>
/// <c>docs/nfr/data-classification.md</c> is specific: "the consent <em>decision</em> (purpose,
/// outcome, wording version) is audited; the wording text and the customer's contact details are not
/// logged". So the snapshots carry the purpose key, the outcome and the version, and not the words
/// she was read, nor the free text somebody typed to say where the answer came from.
/// </para>
/// </remarks>
internal static class ConsentAudit
{
    /// <summary>
    /// The entity type consent entries are recorded against.
    /// </summary>
    /// <remarks>
    /// The customer, not the consent record. An entry is read from the customer's timeline, and a
    /// consent record has no screen of its own to be the subject of.
    /// </remarks>
    public const string EntityType = "customers.customer";

    /// <summary>Records one answer and commits the entry.</summary>
    /// <param name="audit">The platform's audit writer.</param>
    /// <param name="action">The action constant.</param>
    /// <param name="customerId">The customer the answer is about.</param>
    /// <param name="summary">What happened, in words.</param>
    /// <param name="before">Where she stood before, or null when nobody had asked.</param>
    /// <param name="after">Where she stands now.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the entry is committed.</returns>
    public static async Task RecordAsync(
        IAuditWriter audit,
        string action,
        Guid customerId,
        string summary,
        ConsentAnswer? before,
        ConsentAnswer after,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(after);

        await audit.WriteAsync(
            new AuditEntry(
                action,
                EntityType,
                customerId,
                summary,
                Reason: null,
                Before: before is null ? null : ConsentTrailEntry.Of(before),
                After: ConsentTrailEntry.Of(after)),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// A consent answer as the trail describes it.
/// </summary>
/// <param name="PurposeKey">The purpose the answer is about.</param>
/// <param name="Decision">What she said.</param>
/// <param name="WordingVersion">The wording version she was asked under.</param>
/// <param name="RecordId">The record, so the entry and the evidence can be put side by side.</param>
internal sealed record ConsentTrailEntry(
    string PurposeKey,
    string Decision,
    int WordingVersion,
    Guid RecordId)
{
    /// <summary>Takes a trail entry from an answer.</summary>
    /// <param name="answer">The answer.</param>
    /// <returns>The entry.</returns>
    public static ConsentTrailEntry Of(ConsentAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return new ConsentTrailEntry(
            answer.PurposeKey,
            answer.Decision.ToString(),
            answer.WordingVersion,
            answer.RecordId);
    }
}
