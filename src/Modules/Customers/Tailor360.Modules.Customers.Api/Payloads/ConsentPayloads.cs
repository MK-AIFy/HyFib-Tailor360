using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Domain.Consent;

namespace Tailor360.Modules.Customers.Api.Payloads;

/// <summary>
/// Everything a customer has said about the shop's consent purposes, on the wire.
/// </summary>
/// <remarks>
/// The whole register comes back, not only what she has answered. A purpose she has never been asked
/// about is the counter's prompt to ask her, and one that cannot be asked about — retired, or with no
/// wording published — says so rather than simply failing when somebody tries.
/// </remarks>
/// <param name="Purposes">The register, each with her answers newest first.</param>
public sealed record CustomerConsentPayload(IReadOnlyList<ConsentPurposePayload> Purposes)
{
    /// <summary>Projects the consent state onto the wire.</summary>
    /// <param name="consent">The state.</param>
    /// <returns>The payload.</returns>
    public static CustomerConsentPayload From(CustomerConsent consent)
    {
        ArgumentNullException.ThrowIfNull(consent);

        return new CustomerConsentPayload([.. consent.Purposes.Select(ConsentPurposePayload.From)]);
    }
}

/// <summary>One purpose and where this customer stands on it.</summary>
/// <param name="Key">The stable key other modules and records name it by.</param>
/// <param name="Name">The name a counter screen shows.</param>
/// <param name="Description">What agreeing to it allows, in a sentence that can be read aloud.</param>
/// <param name="IsRetired">True once the purpose is no longer asked about.</param>
/// <param name="CurrentWordingVersion">
/// The highest wording version published, or zero when none has been.
/// </param>
/// <param name="CanBeAnswered">
/// Whether an answer may be recorded now. False for a retired purpose and false for one with no
/// published wording, because a consent record names the version the customer was asked under and
/// there would be nothing to name.
/// </param>
/// <param name="Status">
/// Where she stands: <c>NeverAsked</c>, <c>Granted</c>, <c>Declined</c> or <c>Withdrawn</c>. The same
/// four the published <c>IConsentQuery</c> answers with, so a screen and a consuming module cannot
/// read the same record two ways.
/// </param>
/// <param name="Answers">
/// Every answer she has given about this purpose, newest first, so that <c>answers[0]</c> is the one
/// that stands. Empty when nobody has asked.
/// </param>
public sealed record ConsentPurposePayload(
    string Key,
    string Name,
    string? Description,
    bool IsRetired,
    int CurrentWordingVersion,
    bool CanBeAnswered,
    string Status,
    IReadOnlyList<ConsentAnswerPayload> Answers)
{
    /// <summary>The status of a purpose nobody has been asked about.</summary>
    public const string NeverAsked = "NeverAsked";

    /// <summary>Projects one purpose's state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The payload.</returns>
    public static ConsentPurposePayload From(ConsentPurposeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new ConsentPurposePayload(
            state.Key,
            state.Name,
            state.Description,
            state.IsRetired,
            state.CurrentWordingVersion,
            !state.IsRetired && state.CurrentWordingVersion >= ConsentWording.FirstVersion,
            state.Answers.Count == 0 ? NeverAsked : state.Answers[0].Decision.ToString(),
            [.. state.Answers.Select(ConsentAnswerPayload.From)]);
    }
}

/// <summary>One answer as it was recorded.</summary>
/// <param name="RecordId">
/// The record. Media stores it against every object it kept on the strength of this answer, which is
/// why it is on the wire rather than internal.
/// </param>
/// <param name="PurposeKey">The purpose she was answering about.</param>
/// <param name="Decision"><c>Granted</c>, <c>Declined</c> or <c>Withdrawn</c>.</param>
/// <param name="WordingVersion">The version of the wording she was asked under.</param>
/// <param name="RecordedAt">When it was recorded, in UTC.</param>
/// <param name="Source">How the answer reached the system — "counter, verbal", for instance.</param>
/// <param name="RecordedBy">The member of staff who recorded it.</param>
/// <param name="BranchId">The branch it was taken at, where it was taken at one.</param>
public sealed record ConsentAnswerPayload(
    Guid RecordId,
    string PurposeKey,
    string Decision,
    int WordingVersion,
    DateTimeOffset RecordedAt,
    string Source,
    Guid? RecordedBy,
    Guid? BranchId)
{
    /// <summary>Projects one answer.</summary>
    /// <param name="answer">The answer.</param>
    /// <returns>The payload.</returns>
    public static ConsentAnswerPayload From(ConsentAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return new ConsentAnswerPayload(
            answer.RecordId,
            answer.PurposeKey,
            answer.Decision.ToString(),
            answer.WordingVersion,
            answer.RecordedAt,
            answer.Source,
            answer.RecordedBy,
            answer.BranchId);
    }
}

/// <summary>An answer to record.</summary>
/// <remarks>
/// The wording version is deliberately absent. It is read from the register at the moment the answer
/// is recorded, because a client that could name a version could record an answer against words the
/// customer was never read.
/// </remarks>
/// <param name="PurposeKey">The purpose, by its stable key.</param>
/// <param name="Decision"><c>Granted</c>, <c>Declined</c> or <c>Withdrawn</c>.</param>
/// <param name="Source">
/// How the answer reached the system — "counter, verbal", for instance. Required: an answer whose
/// provenance nobody recorded is weaker evidence than one that says where it came from.
/// </param>
public sealed record RecordConsentRequest(string? PurposeKey, string? Decision, string? Source);
