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

    /// <summary>Two drafts were started for the same organisation in the same moment.</summary>
    public static readonly Error DraftNumberConflict = Error.Conflict(
        "catalog.draft-number-conflict",
        "Another catalogue version was started at the same moment and took the next version number. "
        + "Nothing was created; ask again and the draft will take the number after it.");

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
    /// <returns>The error.</returns>
    public static Error DurationOutOfRange(string field, int maximum) => Error.Validation(
        "catalog.duration-out-of-range",
        $"An expected duration is between one and {maximum} working days. It is the default "
        + "due-date offset, never a promise to the customer.",
        field);

    /// <summary>A design group code is not <c>lower_snake_case</c> of the permitted length, or is a reserved word.</summary>
    /// <param name="field">The request field.</param>
    /// <returns>The error.</returns>
    public static Error GroupCodeNotWellFormed(string field) => Error.Validation(
        "catalog.design-group-code-not-well-formed",
        "A design group code is lower snake case — small letters, digits and underscores, beginning "
        + $"with a letter, between {Design.DesignCode.MinimumLength} and {Design.DesignCode.MaximumLength} "
        + "characters — and may not be none, default, all or unknown.",
        field);

    /// <summary>A design option code is not <c>UPPER_SNAKE_CASE</c> of the permitted length, or is a forbidden word.</summary>
    /// <param name="field">The request field.</param>
    /// <returns>The error.</returns>
    public static Error OptionCodeNotWellFormed(string field) => Error.Validation(
        "catalog.design-option-code-not-well-formed",
        "A design option code is upper snake case — capital letters, digits and underscores, beginning "
        + $"with a letter, between {Design.DesignCode.MinimumLength} and {Design.DesignCode.MaximumLength} "
        + "characters. NONE is reserved for 'the customer chose not to have this' and may be used; "
        + "DEFAULT, ALL and UNKNOWN may not.",
        field);

    /// <summary>An illustration reference is not <c>sheet_key#group_code.OPTION_CODE</c>.</summary>
    /// <param name="field">The request field.</param>
    /// <returns>The error.</returns>
    public static Error IllustrationKeyNotWellFormed(string field) => Error.Validation(
        "catalog.illustration-key-not-well-formed",
        "An illustration reference names the sheet, the group and the option, as "
        + "'design_blouse_sleeve_v1#sleeve_style.CAP'. The group is part of it because an option code "
        + "is unique only within its group and several groups share one sheet.",
        field);

    /// <summary>An illustration reference is well formed but anchored on another group or option.</summary>
    /// <param name="field">The request field.</param>
    /// <returns>The error.</returns>
    public static Error IllustrationKeyNotForThisOption(string field) => Error.Validation(
        "catalog.illustration-key-not-for-this-option",
        "An illustration reference is anchored on the option it illustrates — the part after '#' is this "
        + "group's code, a dot, and this option's code — so the picker cannot show one option's drawing "
        + "for another.",
        field);

    /// <summary>A time impact is beyond what a service's own duration may be.</summary>
    /// <param name="field">The request field.</param>
    /// <param name="maximum">The largest impact accepted either way.</param>
    /// <returns>The error.</returns>
    public static Error TimeImpactOutOfRange(string field, int maximum) => Error.Validation(
        "catalog.time-impact-out-of-range",
        $"A time impact is between -{maximum} and {maximum} working days.",
        field);

    /// <summary>A rule operand does not fit its form.</summary>
    /// <param name="field">The request field.</param>
    /// <param name="detail">What does not fit.</param>
    /// <returns>The error.</returns>
    public static Error OperandMalformed(string field, string detail) => Error.Validation(
        "catalog.rule-operand-malformed",
        $"That is not one of the rule forms the catalogue understands. {detail}",
        field);

    /// <summary>A requires or excludes rule names no consequent.</summary>
    /// <param name="field">The request field.</param>
    /// <returns>The error.</returns>
    public static Error ConsequentRequired(string field) => Error.Validation(
        "catalog.rule-consequent-required",
        "A requires or excludes rule names the options it obliges or forbids.",
        field);

    /// <summary>A note or requires-attachment rule names a consequent it has no use for.</summary>
    /// <param name="field">The request field.</param>
    /// <returns>The error.</returns>
    public static Error ConsequentNotAllowed(string field) => Error.Validation(
        "catalog.rule-consequent-not-allowed",
        "A note attaches an instruction and a requires-attachment rule asks for a reference image; "
        + "neither names an option on its right-hand side.",
        field);

    /// <summary>A rule reads a group that is not one of its category's in this version.</summary>
    /// <param name="field">The request field.</param>
    /// <returns>The error.</returns>
    public static Error RuleGroupNotInCategory(string field) => Error.Validation(
        "catalog.rule-group-not-in-category",
        "A rule reads only the design groups of its own category in this catalogue version. A garment "
        + "belongs to exactly one category, so a rule across categories can never fire.",
        field);

    /// <summary>The design group named is not in this version.</summary>
    public static readonly Error DesignGroupNotFound = Error.NotFound(
        "catalog.design-group-not-found",
        "That design option group is not in this catalogue version.");

    /// <summary>The design option named is not in this version.</summary>
    public static readonly Error DesignOptionNotFound = Error.NotFound(
        "catalog.design-option-not-found",
        "That design option is not in this catalogue version.");

    /// <summary>The rule named is not in this version.</summary>
    public static readonly Error DesignRuleNotFound = Error.NotFound(
        "catalog.design-rule-not-found",
        "That design rule is not in this catalogue version.");
}
