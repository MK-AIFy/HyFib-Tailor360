using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// Every failure measurement-template administration reports, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="CustomersErrors"/> because the audience is different. Those errors are about a
/// customer and are read at a counter with somebody waiting; these are about configuration and are read by an
/// administrator building a template, who needs to know which field of which version is wrong and why. The codes
/// are a published contract either way: the administration screen branches on them.
/// </para>
/// <para>
/// No message carries a measurement. A template is configuration and holds none, but the same discipline applies
/// as everywhere else in this module — a problem detail names the field and the rule, never a value
/// (<c>docs/nfr/data-classification.md</c> section 5.2).
/// </para>
/// </remarks>
public static class MeasurementErrors
{
    /// <summary>A required value was missing or blank.</summary>
    /// <param name="field">The field the caller must supply.</param>
    public static Error Required(string field) => Error.Validation(
        "measurements.value-required",
        "A required value was not supplied.",
        field);

    /// <summary>A value was longer than the column that holds it.</summary>
    /// <param name="field">The field that was too long.</param>
    /// <param name="maximum">The longest value the field accepts.</param>
    public static Error TooLong(string field, int maximum) => Error.Validation(
        "measurements.value-too-long",
        $"That value is longer than the {maximum} characters this field holds.",
        field);

    /// <summary>A display order was negative.</summary>
    /// <param name="field">The thing carrying the order.</param>
    public static Error DisplayOrderNegative(string field) => Error.Validation(
        "measurements.display-order-negative",
        "Display order counts up from zero.",
        field);

    /// <summary>A field key was not lower snake case.</summary>
    /// <param name="key">The malformed key.</param>
    public static Error FieldKeyMalformed(string key) => Error.Validation(
        "measurements.field-key-malformed",
        $"'{key}' is not a field key. A key is lower snake case, starts with a letter and is between "
        + $"{FieldKey.MinimumLength} and {FieldKey.MaximumLength} characters — for example 'front_neck_depth'.",
        "key");

    /// <summary>A template code was not upper snake case.</summary>
    /// <param name="code">The malformed code.</param>
    public static Error TemplateCodeMalformed(string code) => Error.Validation(
        "measurements.template-code-malformed",
        $"'{code}' is not a template code. A code is upper snake case, starts with a letter and is between "
        + $"{MeasurementTemplate.MinimumCodeLength} and {MeasurementTemplate.MaximumCodeLength} characters — for "
        + "example 'MT_BLOUSE_PATTERN'.",
        "code");

    /// <summary>Two fields of one version claimed the same key.</summary>
    /// <param name="key">The repeated key.</param>
    public static Error DuplicateFieldKey(string key) => Error.Validation(
        "measurements.duplicate-field-key",
        $"'{key}' is used by more than one field in this version. A key is what a captured value is filed under, "
        + "so it identifies exactly one field.",
        $"fields[{key}].key");

    /// <summary>An inch step was not one a tape is divided into.</summary>
    /// <param name="fraction">The denominator asked for.</param>
    public static Error PrecisionNotPermitted(int fraction) => Error.Validation(
        "measurements.precision-not-permitted",
        $"An inch step of 1/{fraction} cannot be read off a tape. Use one of "
        + $"{string.Join(", ", FieldPrecision.PermittedInchFractions.Order().Select(value => $"1/{value}"))}.",
        "precision.inchFraction");

    /// <summary>A centimetre precision asked for more decimals than the measurement carries.</summary>
    /// <param name="decimals">The decimals asked for.</param>
    public static Error DecimalsNotPermitted(int decimals) => Error.Validation(
        "measurements.decimals-not-permitted",
        $"{decimals} decimal places is finer than a tape can be read. Centimetre fields carry at most "
        + $"{FieldPrecision.MaximumCentimetreDecimals}.",
        "precision.centimetreDecimals");

    /// <summary>A numeric field declared no precision in any unit.</summary>
    public static readonly Error PrecisionMissing = Error.Validation(
        "measurements.precision-missing",
        "A numeric field is entered in inches, in centimetres or in both, so it declares a step for at least one.",
        "precision");

    /// <summary>A field declared a precision for a unit it is not shown in.</summary>
    /// <param name="key">The field.</param>
    /// <param name="unit">The unit.</param>
    public static Error PrecisionNotAllowedForUnit(string key, DisplayUnit unit) => Error.Validation(
        "measurements.precision-not-allowed-for-unit",
        $"'{key}' declares a step for {unit} but is not shown in it. A step nobody can enter against is a rule "
        + "that will not be applied.",
        $"fields[{key}].precision");

    /// <summary>A field's minimum was above its maximum.</summary>
    /// <param name="key">The field.</param>
    public static Error BoundsOutOfOrder(string key) => Error.Validation(
        "measurements.bounds-out-of-order",
        $"'{key}' has a minimum above its maximum, so every value would be refused.",
        $"fields[{key}].bounds");

    /// <summary>A warning threshold sat outside the hard bounds.</summary>
    /// <param name="key">The field.</param>
    /// <param name="which">Which threshold.</param>
    public static Error WarningOutsideBounds(string key, string which) => Error.Validation(
        "measurements.warning-outside-bounds",
        $"'{key}' has a {which} threshold outside its hard bounds, so it can never be reached: the value is "
        + "refused before anybody is asked to confirm it.",
        $"fields[{key}].bands.{which}");

    /// <summary>The warning band's lower threshold was above its upper one.</summary>
    /// <param name="key">The field.</param>
    public static Error WarningBandOutOfOrder(string key) => Error.Validation(
        "measurements.warning-band-out-of-order",
        $"'{key}' warns below a value that is above the value it warns above, so every measurement would be "
        + "queried.",
        $"fields[{key}].bands");

    /// <summary>A choice option's code was not storable.</summary>
    /// <param name="key">The field.</param>
    /// <param name="code">The malformed code.</param>
    public static Error ChoiceCodeMalformed(string key, string code) => Error.Validation(
        "measurements.choice-code-malformed",
        $"'{code}' is not an option code. A code is upper case with digits and underscores — for example "
        + "'ELASTIC' or '12_14'.",
        $"fields[{key}].options");

    /// <summary>A choice field offered nothing to choose.</summary>
    /// <param name="key">The field.</param>
    public static Error ChoiceFieldHasNoOptions(string key) => Error.Validation(
        "measurements.choice-field-has-no-options",
        $"'{key}' is a choice field and offers no options, so it can never be answered.",
        $"fields[{key}].options");

    /// <summary>A numeric field carried choice options.</summary>
    /// <param name="key">The field.</param>
    public static Error NumericFieldHasOptions(string key) => Error.Validation(
        "measurements.numeric-field-has-options",
        $"'{key}' is measured, not chosen, so it carries no options.",
        $"fields[{key}].options");

    /// <summary>A rule carried more clauses than one rule may.</summary>
    /// <param name="key">The field.</param>
    /// <param name="maximum">The most clauses allowed.</param>
    public static Error TooManyClauses(string key, int maximum) => Error.Validation(
        "measurements.too-many-clauses",
        $"The rule on '{key}' has more than {maximum} clauses. A rule that long is describing something the "
        + "template should carry as its own field.",
        $"fields[{key}].rule");

    /// <summary>A clause compared against nothing.</summary>
    /// <param name="key">The field.</param>
    /// <param name="operand">The operand with no values.</param>
    public static Error ClauseHasNoValues(string key, string operand) => Error.Validation(
        "measurements.clause-has-no-values",
        $"The rule on '{key}' compares '{operand}' against no values, so it can never match.",
        $"fields[{key}].rule");

    /// <summary>A rule operand was not a readable name.</summary>
    /// <param name="key">The field.</param>
    /// <param name="operand">The malformed operand.</param>
    public static Error RuleOperandMalformed(string key, string operand) => Error.Validation(
        "measurements.rule-operand-malformed",
        $"The rule on '{key}' reads '{operand}', which is not a field key.",
        $"fields[{key}].rule");

    /// <summary>A rule read the field it governs.</summary>
    /// <param name="key">The field.</param>
    public static Error RuleReadsItself(string key) => Error.Validation(
        "measurements.rule-reads-itself",
        $"The rule on '{key}' reads '{key}'. A field whose visibility depends on its own value can never settle: "
        + "hidden it has no value, and without a value it is shown.",
        $"fields[{key}].rule");

    /// <summary>A rule named a field this version does not have.</summary>
    /// <param name="key">The field carrying the rule.</param>
    /// <param name="referenced">The key it read.</param>
    public static Error UnknownRuleField(string key, string referenced) => Error.Validation(
        "measurements.unknown-rule-field",
        $"The rule on '{key}' reads '{referenced}', which is not a field of this version.",
        $"fields[{key}].rule");

    /// <summary>Two or more fields' rules depend on each other in a loop.</summary>
    /// <param name="keys">The fields in the cycle, in the order they were walked.</param>
    public static Error CyclicCondition(IEnumerable<string> keys) => Error.Validation(
        "measurements.cyclic-condition",
        $"The visibility rules on {string.Join(" → ", keys)} form a loop, so none of them can be settled.",
        "fields");

    /// <summary>A required field could never be shown.</summary>
    /// <param name="key">The field.</param>
    public static Error RequiredFieldHiddenUnconditionally(string key) => Error.Validation(
        "measurements.required-field-never-shown",
        $"'{key}' is required and is hidden by a rule that always matches, so a capture could never be completed.",
        $"fields[{key}].rule");

    /// <summary>Alternative text was missing from a field carrying a diagram.</summary>
    /// <param name="key">The field.</param>
    public static Error DiagramAltMissing(string key) => Error.Validation(
        "measurements.diagram-alt-missing",
        $"'{key}' references a diagram with no alternative text. The text describes the measuring path in words "
        + "and is what a screen reader and a printed sheet rely on.",
        $"fields[{key}].diagramAlt");

    /// <summary>A version had no fields at all.</summary>
    public static readonly Error VersionHasNoFields = Error.Validation(
        "measurements.version-has-no-fields",
        "A template version with no fields would capture nothing. Add the fields before submitting it.",
        "fields");

    /// <summary>The template does not exist, or the caller may not see it.</summary>
    public static readonly Error TemplateNotFound = Error.NotFound(
        "measurements.template-not-found",
        "That measurement template does not exist.");

    /// <summary>The version does not exist within its template.</summary>
    public static readonly Error VersionNotFound = Error.NotFound(
        "measurements.version-not-found",
        "That template version does not exist.");

    /// <summary>The field does not exist within its version.</summary>
    public static readonly Error FieldNotFound = Error.NotFound(
        "measurements.field-not-found",
        "That field does not exist in this template version.");

    /// <summary>An edit was attempted against a version that is no longer a draft.</summary>
    public static readonly Error VersionNotEditable = Error.Conflict(
        "measurements.version-not-editable",
        "Only a draft is edited. Clone this version to a new draft and change that.");

    /// <summary>A submission was attempted against a version that is not a draft.</summary>
    public static readonly Error VersionNotSubmittable = Error.Conflict(
        "measurements.version-not-submittable",
        "Only a draft is submitted for review.");

    /// <summary>An approval was attempted against a version that is not in review.</summary>
    public static readonly Error VersionNotApprovable = Error.Conflict(
        "measurements.version-not-approvable",
        "Only a version in review is approved. Submit the draft first.");

    /// <summary>A version was approved twice.</summary>
    public static readonly Error VersionAlreadyApproved = Error.Conflict(
        "measurements.version-already-approved",
        "This version has already been approved. Who approved it and when is part of its record, so a second "
        + "approval is not recorded over the first.");

    /// <summary>A template asked to open the wizard in a unit nobody enters lengths in.</summary>
    public static readonly Error DefaultUnitNotEnterable = Error.Validation(
        "measurements.default-unit-not-enterable",
        "A template opens in inches or centimetres. Counts are the unit of a single field, not of a wizard.",
        "defaultDisplayUnit");

    /// <summary>A publication was attempted against a version that is not approved.</summary>
    public static readonly Error VersionNotPublishable = Error.Conflict(
        "measurements.version-not-publishable",
        "A version is reviewed and approved before it is published.");

    /// <summary>A retirement was attempted against a version that is not published.</summary>
    public static readonly Error VersionNotRetirable = Error.Conflict(
        "measurements.version-not-retirable",
        "Only the published version is retired, and only once.");

    /// <summary>The person who submitted a version tried to publish it as well.</summary>
    public static readonly Error SubmitterCannotPublish = Error.Forbidden(
        "measurements.submitter-cannot-publish",
        "The administrator who submitted a template version does not also approve it. Ask another administrator "
        + "to review it.");

    /// <summary>Publish-time validation found something that has to be fixed first.</summary>
    public static readonly Error PublishValidationFailed = Error.Validation(
        "measurements.publish-validation-failed",
        "This version cannot be published yet. The findings say what to correct.",
        "fields");

    /// <summary>Another publication won the race.</summary>
    public static readonly Error PublishConflict = Error.Conflict(
        "measurements.publish-conflict",
        "Another version of this template was published while this one was being prepared. Review it and clone "
        + "it if the change is still wanted.");

    /// <summary>Two drafts of one template were started in the same moment.</summary>
    public static readonly Error VersionNumberConflict = Error.Conflict(
        "measurements.version-number-conflict",
        "Another draft of this template was started at the same moment and took the next version number. Nothing "
        + "was created; ask again and the draft will take the number after it.");

    /// <summary>The version was changed by somebody else since it was read.</summary>
    public static readonly Error VersionChanged = Error.Conflict(
        "measurements.version-changed",
        "This template version was changed by somebody else since it was read. Read it again and make the change "
        + "against the current state.");

    /// <summary>A template code was claimed twice in one organisation.</summary>
    /// <remarks>
    /// Raised by the database rather than by a read-then-write, so the code is not to hand at the point the
    /// violation surfaces — and a message that guessed at it would be worse than one that does not.
    /// </remarks>
    public static readonly Error TemplateCodeTaken = Error.Conflict(
        "measurements.template-code-taken",
        "That code already names a measurement template in this organisation. Codes identify a template to the "
        + "catalogue, so each one belongs to exactly one.");

    /* Capture — what a value may be, and what a draft may do (issue #121). ---------------------------- */

    /// <summary>A number was supplied for a field that is chosen.</summary>
    /// <param name="key">The field.</param>
    public static Error ValueIsNotMeasured(string key) => Error.Validation(
        "measurements.value-is-not-measured",
        "This field is chosen from a list rather than measured, so it takes an option and not a number.",
        key);

    /// <summary>An option was supplied for a field that is measured.</summary>
    /// <param name="key">The field.</param>
    public static Error ValueIsNotAChoice(string key) => Error.Validation(
        "measurements.value-is-not-a-choice",
        "This field is measured rather than chosen, so it takes a number and not an option.",
        key);

    /// <summary>An option code the template version does not offer.</summary>
    /// <param name="key">The field.</param>
    /// <param name="code">The code that was sent.</param>
    public static Error ChoiceNotOffered(string key, string code) => Error.Validation(
        "measurements.choice-not-offered",
        $"'{code}' is not one of the choices this field offers on the version being captured against.",
        key);

    /// <summary>A unit the field does not accept.</summary>
    /// <param name="key">The field.</param>
    /// <param name="unit">The unit that was used.</param>
    public static Error UnitNotOffered(string key, DisplayUnit unit) => Error.Validation(
        "measurements.unit-not-offered",
        $"This field is not measured in {unit.ToString().ToLowerInvariant()}s.",
        key);

    /// <summary>A value that is not on the field's own step.</summary>
    /// <param name="key">The field.</param>
    /// <param name="unit">The unit it was entered in.</param>
    /// <remarks>
    /// A tape reads to a fraction of an inch or a tenth of a centimetre. A value between two steps was not read
    /// off a tape, so it is a typing mistake or a unit mistake, and either is worth refusing at confirmation.
    /// </remarks>
    public static Error ValueOffStep(string key, DisplayUnit unit) => Error.Validation(
        "measurements.value-off-step",
        $"That is not a value this field can be read to in {unit.ToString().ToLowerInvariant()}s. Round it to the "
        + "nearest step the field offers.",
        key);

    /// <summary>A value outside the field's hard bounds.</summary>
    /// <param name="key">The field.</param>
    public static Error ValueOutOfBounds(string key) => Error.Validation(
        "measurements.value-out-of-bounds",
        "That measurement is outside what this field can hold. Check the tape and the unit.",
        key);

    /// <summary>An unusual value nobody accepted.</summary>
    /// <param name="key">The field.</param>
    /// <remarks>
    /// Not a refusal of the number — the confirmation band never refuses (<c>ValidationBands</c>). It is a refusal
    /// of a confirmation that never asked: an unusual measurement may be recorded, but somebody has to have said
    /// so, because that acknowledgement is what the trail carries.
    /// </remarks>
    public static Error ValueNeedsAcknowledgement(string key) => Error.Validation(
        "measurements.value-needs-acknowledgement",
        "That measurement is unusual for this field. It can be recorded, but somebody has to confirm they meant "
        + "it.",
        key);

    /// <summary>A value for a field the template version does not have.</summary>
    /// <param name="key">The field that was sent.</param>
    public static Error UnknownField(string key) => Error.Validation(
        "measurements.unknown-field",
        "The template version being captured against has no such field. It may have been removed since the draft "
        + "was started; read the version again.",
        key);

    /// <summary>A value sent to the wrong wizard step.</summary>
    /// <param name="key">The field.</param>
    /// <param name="groupName">The step it was sent with.</param>
    /// <remarks>
    /// A section save replaces its whole step, so accepting a stray field from another one would silently clear
    /// whatever that step held. Refused rather than merged.
    /// </remarks>
    public static Error FieldNotInSection(string key, string groupName) => Error.Validation(
        "measurements.field-not-in-section",
        $"That field does not belong to the '{groupName}' step on this template version.",
        key);

    /// <summary>Two values for one field.</summary>
    /// <param name="key">The field.</param>
    public static Error DuplicateValue(string key) => Error.Validation(
        "measurements.duplicate-value",
        "That field was answered twice in one request.",
        key);

    /// <summary>A required field that is shown and was not answered.</summary>
    /// <param name="key">The field.</param>
    public static Error MissingRequiredValue(string key) => Error.Validation(
        "measurements.value-missing",
        "This field is required on the version being captured against and was not measured.",
        key);

    /// <summary>The draft cannot become a measurement while something about it is wrong.</summary>
    /// <remarks>
    /// One code the client branches on; the findings themselves come back from the check the wizard is already
    /// asking, so a person is told about every wrong field at once rather than one per press.
    /// </remarks>
    public static readonly Error ConfirmationValidationFailed = Error.Validation(
        "measurements.confirmation-validation-failed",
        "These measurements cannot be confirmed yet. Check the fields the wizard has marked and try again.");

    /// <summary>The measurement being reused is another customer's, or another garment's.</summary>
    /// <remarks>
    /// Refused rather than filtered, because a screen showing only numbers cannot tell a person that half of
    /// them came from somebody else.
    /// </remarks>
    public static readonly Error ReuseSourceDoesNotMatch = Error.Validation(
        "measurements.reuse-source-does-not-match",
        "Those measurements were taken for a different customer or a different template, so they cannot be "
        + "reused here.");

    /// <summary>The draft was already turned into a version.</summary>
    /// <remarks>
    /// INV-MSR-02. A conflict rather than a second version: a retried confirmation whose answer was lost must
    /// reach the version it already made, and never make another.
    /// </remarks>
    public static readonly Error DraftAlreadyConfirmed = Error.Conflict(
        "measurements.draft-already-confirmed",
        "These measurements have already been confirmed. Nothing was recorded twice; open the version they became.");

    /// <summary>The draft is past the moment it stops being work in progress.</summary>
    public static readonly Error DraftExpired = Error.Conflict(
        "measurements.draft-expired",
        "This draft is too old to confirm. Measurements go stale, so start again rather than record numbers "
        + "nobody can vouch for.");

    /// <summary>The draft was changed by somebody else since it was read.</summary>
    /// <remarks>
    /// Drafts are shared within a branch: two people may be measuring one customer between them. Last-writer-wins
    /// would silently drop half of a garment's measurements.
    /// </remarks>
    public static readonly Error DraftChanged = Error.Conflict(
        "measurements.draft-changed",
        "Somebody else on this branch changed these measurements since they were read. Read them again and make "
        + "the change against what is there now.");

    /// <summary>This branch is already measuring that customer against that template.</summary>
    /// <remarks>
    /// A branch measures one garment at a time against one template. Two open drafts would leave two people each
    /// filling in half of a different one, and the tailor receiving whichever was confirmed last.
    /// </remarks>
    public static readonly Error DraftAlreadyOpen = Error.Conflict(
        "measurements.draft-already-open",
        "This branch is already measuring that customer against that template. Open the measurements that are "
        + "already under way rather than starting a second set.");

    /// <summary>The caller is not working at a branch, so there is nowhere to file the measuring.</summary>
    /// <remarks>
    /// Measurements are taken where the customer is standing, and a draft belongs to the branch measuring. There
    /// is no sensible default: filing one under an arbitrary branch would put a garment on the wrong counter's
    /// list and hide it from the one actually measuring.
    /// </remarks>
    public static readonly Error BranchRequired = Error.Validation(
        "measurements.branch-required",
        "Measuring belongs to a branch, and this session is not working at one. Sign in at the branch taking the "
        + "measurements.");

    /// <summary>No draft of that identity in this organisation.</summary>
    public static readonly Error DraftNotFound = Error.NotFound(
        "measurements.draft-not-found",
        "No measurement draft of that identity was found.");

    /// <summary>No confirmed version of that identity in this organisation.</summary>
    public static readonly Error MeasurementNotFound = Error.NotFound(
        "measurements.measurement-not-found",
        "No confirmed measurements of that identity were found.");

    /// <summary>The customer has not consented to their measurements being kept.</summary>
    /// <remarks>
    /// INV-MSR-05, checked at confirmation rather than at draft time for the same reason as INV-MSR-04: a draft is
    /// not yet a record of anything. A consent withdrawn while a garment was being measured stops the record from
    /// being made, which is the whole point of the consent.
    /// </remarks>
    public static readonly Error MeasurementConsentMissing = Error.Conflict(
        "measurements.consent-missing",
        "This customer has not agreed to their measurements being kept. Record the agreement first; nothing was "
        + "stored.");

    /// <summary>A draft was started against a template version nothing may be captured against.</summary>
    public static readonly Error TemplateVersionNotPublished = Error.Conflict(
        "measurements.template-version-not-published",
        "Measurements are only ever captured against a published template version. This one is not published.");

    /// <summary>Retirement would leave work in progress with no template to render through.</summary>
    public static readonly Error RetirementWouldStrandOrders = Error.Conflict(
        "measurements.retirement-would-strand-orders",
        "A published catalogue version still points at this template version. Publish a successor template first, "
        + "or retire the catalogue version that references it.");
}
