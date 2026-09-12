using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain;

/// <summary>
/// Every refusal the Billing module can give, with a stable code the client and the tests key on.
/// </summary>
/// <remarks>
/// Messages say what to do next rather than what went wrong internally, because they are what a
/// problem detail carries to an administrator's screen. No message ever carries an amount, a GSTIN
/// or a customer's name: the field is named, the value is not (<c>docs/nfr/data-classification.md</c>).
/// </remarks>
public static class BillingErrors
{
    /// <summary>A required value was not supplied.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error Required(string field) => Error.Validation(
        "billing.value-required",
        "A required value was not supplied.",
        field);

    /// <summary>A value is longer than its field holds.</summary>
    /// <param name="field">The field.</param>
    /// <param name="maximum">The longest value accepted.</param>
    /// <returns>The error.</returns>
    public static Error TooLong(string field, int maximum) => Error.Validation(
        "billing.value-too-long",
        $"That value is longer than the {maximum} characters this field holds.",
        field);

    /// <summary>A code is not upper snake case.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error CodeNotWellFormed(string field) => Error.Validation(
        "billing.code-not-well-formed",
        "A code is upper snake case — capital letters, digits and underscores, beginning with a letter, "
        + $"between {BillingCode.MinimumLength} and {BillingCode.MaximumLength} characters.",
        field);

    /// <summary>A code is already used in this version.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error CodeNotUnique(string field) => Error.Validation(
        "billing.code-not-unique",
        "That code is already used in this version. A code identifies one thing, and it is what price "
        + "lists, invoices and exports refer to.",
        field);

    /// <summary>An HSN or SAC classification is not a run of digits.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error ClassificationNotWellFormed(string field) => Error.Validation(
        "billing.classification-not-well-formed",
        $"An HSN or SAC code is between {TaxCodeDetails.MinimumClassificationLength} and "
        + $"{TaxCodeDetails.MaximumClassificationLength} digits.",
        field);

    /// <summary>A tax rate is outside the range a percentage can hold.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error RateOutOfRange(string field) => Error.Validation(
        "billing.rate-out-of-range",
        "A rate is a percentage between 0 and 100 with at most three decimal places.",
        field);

    /// <summary>The same tax component is listed twice on one code.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error ComponentDuplicated(string field) => Error.Validation(
        "billing.component-duplicated",
        "A tax code carries each component — CGST, SGST, IGST, cess — at most once.",
        field);

    /// <summary>A component kind is not one the system knows.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error ComponentNotWellFormed(string field) => Error.Validation(
        "billing.component-not-well-formed",
        "A tax component is one of Cgst, Sgst, Igst or Cess.",
        field);

    /// <summary>A rate is not a non-negative amount of at most four decimal places.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error RateNotWellFormed(string field) => Error.Validation(
        "billing.rate-not-well-formed",
        "A rate is a non-negative amount with at most four decimal places.",
        field);

    /// <summary>An amount is not a non-negative value of at most two decimal places.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error AmountNotWellFormed(string field) => Error.Validation(
        "billing.amount-not-well-formed",
        "An amount is a non-negative value in rupees with at most two decimal places.",
        field);

    /// <summary>A unit is not a short lower-case word.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error UnitNotWellFormed(string field) => Error.Validation(
        "billing.unit-not-well-formed",
        "A unit is a short lower-case word such as each, metre or hour.",
        field);

    /// <summary>A discount rule's counter maximum exceeds its approved maximum.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error DiscountBoundsNotOrdered(string field) => Error.Validation(
        "billing.discount-bounds-not-ordered",
        "What the counter may give on its own cannot exceed what anyone may give with approval.",
        field);

    /// <summary>The price list is not one of the caller's organisation.</summary>
    public static readonly Error PriceListNotFound = Error.NotFound(
        "billing.price-list-not-found",
        "That price list is not one of this organisation's.");

    /// <summary>The price list moved since the caller read it.</summary>
    public static readonly Error PriceListChanged = Error.PreconditionFailed(
        "billing.price-list-changed",
        "The price list changed since it was read. Reload it and try again.");

    /// <summary>The item is not in this version.</summary>
    public static readonly Error ItemNotFound = Error.NotFound(
        "billing.item-not-found",
        "That price-list item is not in this version.");

    /// <summary>The discount rule is not in this version.</summary>
    public static readonly Error DiscountRuleNotFound = Error.NotFound(
        "billing.discount-rule-not-found",
        "That discount rule is not in this version.");

    /// <summary>A pair of dates is the wrong way round.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error DatesNotOrdered(string field) => Error.Validation(
        "billing.dates-not-ordered",
        "The last day is before the first.",
        field);

    /// <summary>A GSTIN does not have the registered shape.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error GstinNotWellFormed(string field) => Error.Validation(
        "billing.gstin-not-well-formed",
        "A GSTIN is fifteen characters — a two-digit state code, a ten-character PAN, an entity number, "
        + "the letter Z and a check character — and its check character must agree with the rest. Copy it "
        + "from the registration certificate.",
        field);

    /// <summary>A GSTIN's state code prefix and the state code given disagree.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error GstinStateMismatch(string field) => Error.Validation(
        "billing.gstin-state-mismatch",
        "The first two characters of a GSTIN are the state code, and they differ from the state code given.",
        field);

    /// <summary>A state code is not two digits.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The error.</returns>
    public static Error StateCodeNotWellFormed(string field) => Error.Validation(
        "billing.state-code-not-well-formed",
        "A GST state code is two digits, as printed on the registration certificate.",
        field);

    /// <summary>Two registrations of one branch would be in force on the same day.</summary>
    public static readonly Error RegistrationOverlaps = Error.Conflict(
        "billing.registration-overlaps",
        "The branch already has a registration in force on one or more of those days. End the earlier one "
        + "the day before this one begins.");

    /// <summary>An amendment tried to move a registration to another branch.</summary>
    public static readonly Error BranchImmutable = Error.Validation(
        "billing.branch-immutable",
        "A registration belongs to the branch it was recorded for; invoices were issued under it there. "
        + "Record the other branch's registration separately.",
        "branchId");

    /// <summary>A branch identifier names no branch of the caller's organisation.</summary>
    /// <param name="field">The field that named it.</param>
    public static Error BranchNotFound(string field) => Error.Validation(
        "billing.branch-not-found",
        "That is not a branch of this organisation.",
        field);

    /// <summary>The registration is not one of the caller's organisation.</summary>
    public static readonly Error RegistrationNotFound = Error.NotFound(
        "billing.registration-not-found",
        "That GST registration is not one of this organisation's.");

    /// <summary>The registration moved since the caller read it.</summary>
    public static readonly Error RegistrationChanged = Error.PreconditionFailed(
        "billing.registration-changed",
        "The registration changed since it was read. Reload it and try again.");

    /// <summary>The version is not one of the caller's organisation.</summary>
    public static readonly Error VersionNotFound = Error.NotFound(
        "billing.version-not-found",
        "That version is not one of this organisation's.");

    /// <summary>A published or retired version was asked to change.</summary>
    public static readonly Error VersionNotEditable = Error.Conflict(
        "billing.version-not-editable",
        "Only a draft can be changed. A published version is what every invoice since was calculated on; "
        + "clone it to a new draft, make the change there and publish that.");

    /// <summary>The version moved since the caller read it.</summary>
    public static readonly Error VersionChanged = Error.PreconditionFailed(
        "billing.version-changed",
        "The version changed since it was read. Reload it and try again.");

    /// <summary>Two drafts started in the same moment claimed the same number.</summary>
    public static readonly Error DraftNumberConflict = Error.Conflict(
        "billing.draft-number-conflict",
        "Another draft was started at the same moment and took this number. Nothing was created; start "
        + "the draft again.");

    /// <summary>Something other than a draft was asked to publish.</summary>
    public static readonly Error VersionNotPublishable = Error.Conflict(
        "billing.version-not-publishable",
        "Only a draft can be published.");

    /// <summary>Something other than the published version was asked to retire.</summary>
    public static readonly Error VersionNotRetirable = Error.Conflict(
        "billing.version-not-retirable",
        "Only the published version can be retired, and it is retired by publishing its successor.");

    /// <summary>Two administrators published different drafts in the same moment.</summary>
    public static readonly Error PublishConflict = Error.Conflict(
        "billing.publish-conflict",
        "Another version was published at the same moment. Read what is published now before deciding "
        + "whether this draft is still wanted.");

    /// <summary>Two administrators published versions of different lists pricing the same branch in the same moment.</summary>
    public static readonly Error BranchPublishConflict = Error.Conflict(
        "billing.branch-publish-conflict",
        "Another list's version pricing one of these branches was published at the same moment. A branch is "
        + "priced by one published version; read what is published now before deciding whether this draft is "
        + "still wanted.");

    /// <summary>A publication was refused by its checks.</summary>
    public static readonly Error PublishValidationFailed = Error.Validation(
        "billing.publish-validation-failed",
        "The version cannot be published while the checks below fail.");

    /// <summary>A reason was not given where the action needs one.</summary>
    public static readonly Error ReasonRequired = Error.Validation(
        "billing.reason-required",
        "Say why. The reason is recorded with the change.",
        "reason");

    /// <summary>The tax code is not in this version.</summary>
    public static readonly Error TaxCodeNotFound = Error.NotFound(
        "billing.tax-code-not-found",
        "That tax code is not in this version.");

    /// <summary>A calculation was already stored under the reference.</summary>
    public static readonly Error SnapshotExists = Error.Conflict(
        "billing.snapshot-exists",
        "A calculation is already stored under that reference.");

    /// <summary>A reference was asked again with a different request.</summary>
    public static readonly Error SnapshotConflict = Error.Conflict(
        "billing.snapshot-conflict",
        "A different calculation is stored under that reference. A reference names one calculation; ask "
        + "for a new one under a new reference.");

    /// <summary>A reason held a control character, which the record cannot carry.</summary>
    /// <param name="field">The field.</param>
    public static Error ReasonNotWellFormed(string field) => Error.Validation(
        "billing.reason-not-well-formed",
        "A reason is plain text.",
        field);

    /// <summary>Something a calculation needs is not configured.</summary>
    /// <param name="what">What is missing, in the administrator's words.</param>
    /// <param name="field">The field of the request it was looked up for.</param>
    public static Error ConfigurationMissing(string what, string field) => Error.Validation(
        "billing.configuration-missing",
        $"{what} Nothing can be priced until it is published.",
        field);

    /// <summary>A request carried no line.</summary>
    public static readonly Error LinesRequired = Error.Validation(
        "billing.lines-required",
        "There is nothing to price.",
        "lines");

    /// <summary>Two lines carried one key.</summary>
    /// <param name="field">The line.</param>
    public static Error LineKeyDuplicated(string field) => Error.Validation(
        "billing.line-key-duplicated",
        "Each line carries its own key; two lines came back under one.",
        field);

    /// <summary>A quantity was zero or negative.</summary>
    /// <param name="field">The line's quantity.</param>
    public static Error QuantityNotPositive(string field) => Error.Validation(
        "billing.quantity-not-positive",
        "A quantity is a positive number of at most one million, to four decimal places.",
        field);

    /// <summary>A line named an item the version does not hold or has retired.</summary>
    /// <param name="code">The item code.</param>
    /// <param name="field">The field naming it.</param>
    public static Error ItemNotPriced(string code, string field) => Error.Validation(
        "billing.item-not-priced",
        $"'{code}' is not an active item of the price-list version in force.",
        field);

    /// <summary>A line's surcharge list named an item that is not a surcharge.</summary>
    /// <param name="code">The item code.</param>
    /// <param name="field">The field naming it.</param>
    public static Error ItemNotASurcharge(string code, string field) => Error.Validation(
        "billing.item-not-a-surcharge",
        $"'{code}' is not a surcharge item. A service or a material is priced as a line of its own, never added on "
        + "top of another.",
        field);

    /// <summary>A surcharge item is taxed under a different code from the line's base item.</summary>
    /// <param name="code">The surcharge item code.</param>
    /// <param name="field">The field naming it.</param>
    public static Error SurchargeTaxedDifferently(string code, string field) => Error.Validation(
        "billing.surcharge-taxed-differently",
        $"'{code}' carries a different tax code from the line's item. A line is taxed under one code; "
        + "something taxed differently is its own line.",
        field);

    /// <summary>A line named a discount rule the version does not hold or has retired.</summary>
    /// <param name="code">The rule code.</param>
    /// <param name="field">The field naming it.</param>
    public static Error DiscountRuleNotInForce(string code, string field) => Error.Validation(
        "billing.discount-rule-not-in-force",
        $"'{code}' is not an active discount rule of the price-list version in force.",
        field);

    /// <summary>A discount was more than its rule allows anyone to give.</summary>
    /// <param name="field">The discount's value.</param>
    public static Error DiscountAboveMaximum(string field) => Error.Validation(
        "billing.discount-above-maximum",
        "The discount is more than its rule allows anyone to give.",
        field);

    /// <summary>A discount was more than the line.</summary>
    /// <param name="field">The discount's value.</param>
    public static Error DiscountExceedsLine(string field) => Error.Validation(
        "billing.discount-exceeds-line",
        "The discount is more than the line it is taken from.",
        field);

    /// <summary>A discount above the counter's threshold, or an override beyond the version's, was asked without the permission.</summary>
    public static readonly Error ApprovalRequired = Error.Forbidden(
        "billing.approval-required",
        "That discount or override is above what may be given without billing.override_price.");

    /// <summary>An override rate was negative.</summary>
    /// <param name="field">The override's rate.</param>
    public static Error OverrideRateNotWellFormed(string field) => Error.Validation(
        "billing.override-rate-not-well-formed",
        "An override rate is zero or more, with at most four decimal places.",
        field);

    /// <summary>A published tax code's code may not change.</summary>
    public static Error PublishedCodeChanged(string field) => Error.Validation(
        "billing.published-code-changed",
        "This code has been published under another spelling, and a published code never changes: "
        + "invoices already carry it. Add a new code and retire this one instead.",
        field);
}
