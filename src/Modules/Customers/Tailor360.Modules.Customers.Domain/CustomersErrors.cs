using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain;

/// <summary>
/// Every failure the Customers module reports, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The <c>code</c> of each error is a published contract: the progressive web application branches on
/// it, and a screen's wording is chosen from it. Keeping the whole set in one file is what makes it
/// reviewable — a reader can see at a glance that two failures do not share a code and that no code
/// says more about a record than the caller was entitled to learn.
/// </para>
/// <para>
/// Nothing here interpolates personal data into a message. A customer's name, telephone number or
/// address never appears in a problem detail, a log line or an audit summary
/// (<c>docs/nfr/data-classification.md</c> section 5.2), so the messages name fields and rules rather
/// than values.
/// </para>
/// </remarks>
public static class CustomersErrors
{
    /// <summary>A required value was missing or blank.</summary>
    /// <param name="field">The field the caller must supply.</param>
    public static Error Required(string field) => Error.Validation(
        "customers.value-required",
        "A required value was not supplied.",
        field);

    /// <summary>A value was longer than the column that holds it.</summary>
    /// <param name="field">The field that was too long.</param>
    /// <param name="maximum">The longest value the field accepts.</param>
    public static Error TooLong(string field, int maximum) => Error.Validation(
        "customers.value-too-long",
        $"That value is longer than the {maximum} characters this field holds.",
        field);

    /// <summary>A telephone number could not be read as a number that could be dialled.</summary>
    /// <param name="field">The field carrying the number.</param>
    public static Error PhoneNotUnderstood(string field) => Error.Validation(
        "customers.phone-not-understood",
        "That telephone number could not be read. Enter the digits as they would be dialled, "
        + "with or without the country code.",
        field);

    /// <summary>An email address could not be read as an address that could be written to.</summary>
    /// <remarks>
    /// Separate from <see cref="Required"/>, because the two say different things to the person at the
    /// counter: one means the field is empty and the other means what is in it will not work. Answering
    /// a mistyped address with "a required value was not supplied" sends somebody looking for a field
    /// they have already filled in.
    /// </remarks>
    /// <param name="field">The field carrying the address.</param>
    public static Error EmailNotUnderstood(string field) => Error.Validation(
        "customers.email-not-understood",
        "That email address could not be read. Check it for a stray space or a missing part.",
        field);

    /// <summary>A language tag was not one the application serves.</summary>
    /// <param name="field">The field carrying the tag.</param>
    public static Error LanguageNotSupported(string field) => Error.Validation(
        "customers.language-not-supported",
        "That language is not one this application serves.",
        field);

    /// <summary>No customer matches the identifier supplied, or the caller may not see it.</summary>
    /// <remarks>
    /// One error for both cases on purpose. Telling a caller that a record exists but is not theirs
    /// is the identifier-editing oracle the authorisation design closes
    /// (<c>tests/Tailor360.IntegrationTests/Authorization/matrix.yaml</c>).
    /// </remarks>
    public static Error CustomerNotFound { get; } = Error.NotFound(
        "customers.customer-not-found",
        "No customer matches that identifier.");

    /// <summary>The record changed between being read and being written.</summary>
    public static Error ConcurrentChange { get; } = Error.Conflict(
        "customers.concurrent-change",
        "Somebody else changed this customer while you had it open.");

    /// <summary>The requested status change is not legal from the record's current status.</summary>
    /// <param name="from">The status the record is in.</param>
    /// <param name="to">The status the caller asked for.</param>
    public static Error StatusTransitionNotAllowed(string from, string to) => Error.Conflict(
        "customers.status-transition-not-allowed",
        $"A customer record cannot move from {from} to {to}.");

    /// <summary>A correction, deactivation or reactivation arrived without a reason.</summary>
    public static Error ReasonRequired { get; } = Error.Validation(
        "customers.reason-required",
        "This change is recorded against a reason. Say why in a sentence.",
        "reason");

    /// <summary>The reason given was too short to mean anything or too long for the trail.</summary>
    /// <param name="minimum">The shortest reason accepted.</param>
    /// <param name="maximum">The longest reason accepted.</param>
    public static Error ReasonOutOfBounds(int minimum, int maximum) => Error.Validation(
        "customers.reason-out-of-bounds",
        $"A reason is between {minimum} and {maximum} characters.",
        "reason");

    /// <summary>Creating this record would duplicate one the caller has not looked at.</summary>
    /// <remarks>
    /// A refusal rather than a silent merge, and a refusal the caller can override by confirming they
    /// have read the candidates. Issue #26 is explicit that duplicate suggestions explain the match
    /// and require an authorised decision; deciding for the person is the failure this prevents.
    /// </remarks>
    public static Error DuplicatesNotReviewed { get; } = Error.Conflict(
        "customers.duplicates-not-reviewed",
        "One or more existing customers look like this person. Read them, then either open one or "
        + "confirm that this is somebody new.");

    /// <summary>A consent purpose key held a character that would not survive a URL or a log line.</summary>
    /// <param name="field">The field carrying the key.</param>
    public static Error ConsentPurposeKeyNotAllowed(string field) => Error.Validation(
        "customers.consent-purpose-key-not-allowed",
        "A consent purpose key is lower-case letters, digits and underscores.",
        field);

    /// <summary>A consent decision was a value that is none of the outcomes the system records.</summary>
    /// <remarks>
    /// Not a weaker answer — an uninterpretable one. The query that reads the latest record to decide
    /// whether a message may be sent would have nothing to say about it, and a row that cannot be read
    /// is not evidence.
    /// </remarks>
    /// <param name="field">The field carrying the decision.</param>
    public static Error ConsentDecisionNotUnderstood(string field) => Error.Validation(
        "customers.consent-decision-not-understood",
        "A consent decision is granted, declined or withdrawn.",
        field);

    /// <summary>The purpose is retired, so it is not asked about and its wording does not change.</summary>
    /// <param name="key">The purpose key.</param>
    public static Error ConsentPurposeRetired(string key) => Error.Conflict(
        "customers.consent-purpose-retired",
        $"The consent purpose '{key}' is retired. What customers already said about it stands; it is "
        + "no longer asked about, and its wording is not changed.");

    /// <summary>A communication channel was a value that is none of the ones the shop can send on.</summary>
    /// <param name="field">The field carrying the channels.</param>
    public static Error CommunicationChannelNotUnderstood(string field) => Error.Validation(
        "customers.communication-channel-not-understood",
        "A communication channel is one the shop can actually send on.",
        field);

    /// <summary>One end of a quiet-hours window was given without the other.</summary>
    /// <param name="field">The end that is missing.</param>
    public static Error QuietHoursIncomplete(string field) => Error.Validation(
        "customers.quiet-hours-incomplete",
        "Quiet hours need both a start and an end, or neither.",
        field);

    /// <summary>A quiet-hours window opened and closed at the same time, so it covers nothing.</summary>
    /// <param name="field">The field carrying the start.</param>
    public static Error QuietHoursEmpty(string field) => Error.Validation(
        "customers.quiet-hours-empty",
        "A quiet-hours window that starts and ends at the same time covers no time at all. To stop "
        + "messages altogether, allow no channel.",
        field);

    /// <summary>The caller is assigned to no branch, so there is no branch to create the record in.</summary>
    public static Error NoBranchInContext { get; } = Error.Forbidden(
        "customers.no-branch-in-context",
        "This action happens at a branch, and your session is not working in one.");
}
