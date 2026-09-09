using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain;

/// <summary>
/// Every failure the Catalog module reports, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The <c>code</c> of each error is a published contract: the administration screens branch on it and
/// choose their wording from it. Keeping the set in one file is what makes it reviewable — a reader
/// can see at a glance that two failures do not share a code.
/// </para>
/// <para>
/// Nothing here is personal data. The catalogue holds no customer information at all, so the usual
/// caution about messages applies only in its weak form: a message names a code, a field or a rule,
/// never a value a caller was not entitled to learn.
/// </para>
/// </remarks>
public static class CatalogErrors
{
    /// <summary>A required value was missing or blank.</summary>
    /// <param name="field">The field the caller must supply.</param>
    public static Error Required(string field) => Error.Validation(
        "catalog.value-required",
        "A required value was not supplied.",
        field);

    /// <summary>A value was longer than the column that holds it.</summary>
    /// <param name="field">The field that was too long.</param>
    /// <param name="maximum">The longest value the field accepts.</param>
    public static Error TooLong(string field, int maximum) => Error.Validation(
        "catalog.value-too-long",
        $"That value is longer than the {maximum} characters this field holds.",
        field);

    /// <summary>A code broke the shape the convention fixes.</summary>
    /// <param name="field">The field carrying the code.</param>
    public static Error CodeNotWellFormed(string field) => Error.Validation(
        "catalog.code-not-well-formed",
        "A code is upper snake case — capital letters, digits and underscores, beginning with a "
        + $"letter, between {CatalogCode.MinimumLength} and {CatalogCode.MaximumLength} characters — "
        + "and may not be NONE, DEFAULT, ALL or UNKNOWN, which filters and exports already use.",
        field);

    /// <summary>Two records in one version claimed the same code.</summary>
    /// <param name="field">The field carrying the code.</param>
    public static Error CodeNotUnique(string field) => Error.Validation(
        "catalog.code-not-unique",
        "That code is already used in this catalogue version. A code identifies one thing, and it is "
        + "what price lists, reports and exports refer to.",
        field);

    /// <summary>A parent category was named that this version does not hold.</summary>
    public static readonly Error ParentNotFound = Error.Validation(
        "catalog.parent-not-found",
        "The parent category is not in this catalogue version. A category's parent is always another "
        + "category of the same version, because a version is one coherent snapshot of the hierarchy.",
        "parentCategoryId");

    /// <summary>Re-parenting would have made the hierarchy circular.</summary>
    public static readonly Error HierarchyWouldCycle = Error.Validation(
        "catalog.hierarchy-would-cycle",
        "That parent is the category itself or one of its own descendants, which would make the "
        + "hierarchy circular and every walk of it endless.",
        "parentCategoryId");

    /// <summary>A category was named that this version does not hold.</summary>
    public static readonly Error CategoryNotFound = Error.NotFound(
        "catalog.category-not-found",
        "That category is not in this catalogue version.");

    /// <summary>A service type was named that this version does not hold.</summary>
    public static readonly Error ServiceTypeNotFound = Error.NotFound(
        "catalog.service-type-not-found",
        "That service type is not in this catalogue version.");

    /// <summary>A catalogue version was named that does not exist for this organisation.</summary>
    public static readonly Error VersionNotFound = Error.NotFound(
        "catalog.version-not-found",
        "That catalogue version does not exist.");

    /// <summary>No published version exists yet.</summary>
    public static readonly Error NoPublishedVersion = Error.NotFound(
        "catalog.no-published-version",
        "No catalogue version has been published, so there is nothing orderable yet. An administrator "
        + "publishes the first version before intake can offer anything.");

    /// <summary>An edit was attempted against a version that is no longer a draft.</summary>
    public static readonly Error VersionNotEditable = Error.Conflict(
        "catalog.version-not-editable",
        "Only a draft may be edited. A published version is immutable: clone it to a new draft, make "
        + "the change there and publish that.");

    /// <summary>Publication was attempted against a version that is not a draft.</summary>
    public static readonly Error VersionNotPublishable = Error.Conflict(
        "catalog.version-not-publishable",
        "Only a draft may be published.");

    /// <summary>Retirement was attempted against a version that is not published.</summary>
    public static readonly Error VersionNotRetirable = Error.Conflict(
        "catalog.version-not-retirable",
        "Only the published version may be retired. A draft is discarded rather than retired, and a "
        + "retired version is already retired.");

    /// <summary>A presentation correction was attempted against a draft.</summary>
    public static readonly Error CorrectionNeedsPublishedVersion = Error.Conflict(
        "catalog.correction-needs-published-version",
        "A presentation correction applies to a published version. A draft is edited directly.");

    /// <summary>Another publication won the race.</summary>
    public static readonly Error PublishConflict = Error.Conflict(
        "catalog.publish-conflict",
        "Another catalogue version was published while this one was being prepared. Review the "
        + "published version and clone it if the change is still wanted.");

    /// <summary>The version was changed by somebody else since it was read.</summary>
    public static readonly Error VersionChanged = Error.Conflict(
        "catalog.version-changed",
        "This catalogue version was changed by somebody else since it was read. Read it again and "
        + "make the change against the current state.");

    /// <summary>Publication was refused by a validator.</summary>
    /// <remarks>
    /// The findings themselves are field-level problem details built beside this, so the message says
    /// only that publication was refused. A caller sees the reasons; this is the code they branch on.
    /// </remarks>
    public static readonly Error PublishValidationFailed = Error.Validation(
        "catalog.publish-validation-failed",
        "This catalogue version cannot be published while it has validation errors.");

    /// <summary>Retirement was refused because work is still running against the version.</summary>
    public static readonly Error RetirementWouldBreakOrders = Error.PreconditionFailed(
        "catalog.retirement-would-break-orders",
        "Orders are still in progress against this catalogue version and no successor carries a "
        + "replacement for every service type they use. Publish a successor first; retiring stops new "
        + "orders, and it must never strand the ones already taken.");

    /// <summary>Active dates were given the wrong way round.</summary>
    /// <param name="field">The field carrying the later date.</param>
    public static Error ActiveDatesReversed(string field) => Error.Validation(
        "catalog.active-dates-reversed",
        "The end of the active period is before its start, which is a period that never opens.",
        field);

    /// <summary>A negative display order was given.</summary>
    /// <param name="field">The field carrying the order.</param>
    public static Error DisplayOrderNegative(string field) => Error.Validation(
        "catalog.display-order-negative",
        "Display order counts from zero upwards.",
        field);

    /// <summary>A validator could not answer, so the question was not settled either way.</summary>
    /// <remarks>
    /// Deliberately not a validation failure. "We asked Billing whether this price-list item exists and
    /// Billing did not answer" is not the same as "the item does not exist", and publishing a catalogue
    /// on the strength of a check that did not run is how a service reaches intake with no rate behind
    /// it. The command stops, and the administrator is told which module went quiet.
    /// </remarks>
    /// <param name="validator">The validator that failed, by name.</param>
    public static Error ValidatorUnavailable(string validator) => Error.Unavailable(
        "catalog.validator-unavailable",
        $"The '{validator}' checks could not be run, so this version has not been checked and will "
        + "not be published. Try again; if it persists, the module that owns those checks is unwell.");

    /// <summary>An expected duration was outside what a working calendar can express.</summary>
    /// <param name="field">The field carrying the duration.</param>
    /// <param name="maximum">The longest duration accepted.</param>
    public static Error DurationOutOfRange(string field, int maximum) => Error.Validation(
        "catalog.duration-out-of-range",
        $"An expected duration is between one and {maximum} working days. It is the default "
        + "due-date offset, never a promise to the customer.",
        field);
}

