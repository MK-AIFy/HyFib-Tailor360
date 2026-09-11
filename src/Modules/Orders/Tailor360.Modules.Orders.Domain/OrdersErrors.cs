using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain;

/// <summary>
/// Every failure the Orders module reports, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The <c>code</c> of each error is a published contract: the progressive web application branches on
/// it, and a screen's wording is chosen from it. Keeping the whole set in one file is what makes it
/// reviewable — a reader can see at a glance that two failures do not share a code, and that no code
/// says more about an order than the caller was entitled to learn.
/// </para>
/// <para>
/// Nothing here interpolates personal data into a message. A customer's name, telephone number,
/// address or measurement never appears in a problem detail, a log line or an audit summary
/// (<c>docs/nfr/data-classification.md</c> section 5.2), so the messages name fields, statuses and
/// operational references — a garment job number, a configured reason code — and never values. The
/// field name travels in the error's <c>target</c>, which is what the client turns into a field
/// error; the field's <em>value</em> never travels at all.
/// </para>
/// <para>
/// A refused transition is an <see cref="Error"/> returned inside a <c>Result</c> and never an
/// exception. An exception in this module means a defect — a null argument the type system said could
/// not be null — and not a shop-floor situation the counter has to be told about.
/// </para>
/// <para>
/// <strong>A status reaches a message as a word, never as a member name.</strong> The refusals that
/// name a status take the enumeration rather than a string, so "InProduction" cannot reach the person
/// at the counter and a <em>garment</em> status cannot reach an <em>order</em>'s message — which
/// <c>docs/prd/state-transitions.md</c> section 2 warns about by name, because <c>on_hold</c> is a
/// garment job status and an order is never in it.
/// </para>
/// </remarks>
public static class OrdersErrors
{
    /* General ----------------------------------------------------------------------------------- */

    /// <summary>A required value was missing or blank.</summary>
    /// <param name="field">The field the caller must supply.</param>
    public static Error Required(string field) => Error.Validation(
        "orders.value-required",
        "A required value was not supplied.",
        field);

    /// <summary>A value was longer than the column that holds it.</summary>
    /// <param name="field">The field that was too long.</param>
    /// <param name="maximum">The longest value the field accepts.</param>
    public static Error TooLong(string field, int maximum) => Error.Validation(
        "orders.value-too-long",
        $"That value is longer than the {maximum} characters this field holds.",
        field);

    /// <summary>A value outside the set its field accepts arrived on a public factory.</summary>
    /// <remarks>
    /// <para>
    /// Every choice this module stores is an enumeration, and an enumeration in C# accepts any number a
    /// cast can produce — which is exactly what a deserialiser does with a value it did not recognise.
    /// A number outside the named set is not a weaker answer but an uninterpretable one: the two gates,
    /// the production ordering and the <strong>Measurements needed</strong> queue all test named members,
    /// so an unnamed one is silently ignored rather than enforced, and it is <em>persisted</em> that way.
    /// </para>
    /// <para>
    /// Generic and field-carrying like <see cref="Required"/> and <see cref="TooLong"/> rather than one
    /// code per enumeration: a client can only ever act on it in one way, which is to send a value the
    /// field names. The field travels in the error's target; the value never travels at all.
    /// </para>
    /// </remarks>
    /// <param name="field">The field carrying the value.</param>
    public static Error NotUnderstood(string field) => Error.Validation(
        "orders.value-not-understood",
        "That value is not one this field understands.",
        field);

    /// <summary>A transition that is recorded against a reason arrived without one.</summary>
    /// <remarks>
    /// The transitions that demand one are listed in <c>docs/prd/state-transitions.md</c> section 8:
    /// revise an order, hold, resume, reschedule, cancel a garment job and cancel an order. The reason
    /// is never optional and is never defaulted by the client.
    /// </remarks>
    public static Error ReasonRequired { get; } = Error.Validation(
        "orders.reason-required",
        "This change is recorded against a reason. Say why in a sentence.",
        "reason");

    /// <summary>A hold or a cancellation arrived without the configured reason code.</summary>
    /// <remarks>
    /// Separate from <see cref="ReasonRequired"/> because the two ask for different things. The code
    /// is chosen from the branch's configured list and is what a report groups by; the sentence is
    /// what the next person to open the job reads. Neither substitutes for the other.
    /// </remarks>
    public static Error ReasonCodeRequired { get; } = Error.Validation(
        "orders.reason-code-required",
        "Choose one of the configured reason codes for this action.",
        "reasonCode");

    /// <summary>The record changed between being read and being written.</summary>
    public static Error ConcurrentChange { get; } = Error.Conflict(
        "orders.concurrent-change",
        "Somebody else changed this while you had it open.");

    /// <summary>The caller is working in no branch, so there is no branch to record this against.</summary>
    /// <remarks>
    /// Branch scope is evaluated, never inferred (<c>CLAUDE.md</c> section 4 rule 2). An order, a
    /// draft and an estimate all belong to the branch that took them, and the branch is also the
    /// sequence key every display number is allocated from.
    /// </remarks>
    public static Error NoBranchInContext { get; } = Error.Forbidden(
        "orders.no-branch-in-context",
        "This action happens at a branch, and your session is not working in one.");

    /* Display numbers --------------------------------------------------------------------------- */

    /// <summary>A display number was composed from something that is not a branch code.</summary>
    public static Error BranchCodeNotWellFormed { get; } = Error.Validation(
        "orders.branch-code-not-well-formed",
        "A branch code is upper-case letters and digits with no spaces or punctuation.",
        "branchCode");

    /// <summary>A financial-year token was not the shape a display number carries.</summary>
    /// <remarks>
    /// The token is the two-digit year the financial year opens in followed by the two-digit year it
    /// closes in — <c>2627</c> for 2026-27 — which <c>docs/architecture/conventions.md</c> section 2.3
    /// records as <strong>proposed, to be confirmed</strong> under COD-03. A transposed year is caught
    /// here rather than printed on a document.
    /// </remarks>
    public static Error FinancialYearNotWellFormed { get; } = Error.Validation(
        "orders.financial-year-not-well-formed",
        "A financial year reads as the two-digit year it opens in followed by the two-digit year it "
        + "closes in, such as 2627 for 2026-27.",
        "financialYear");

    /// <summary>A display-number sequence was below the first position.</summary>
    /// <param name="field">The field carrying the sequence.</param>
    public static Error SequenceOutOfRange(string field) => Error.Validation(
        "orders.sequence-out-of-range",
        "A display-number sequence starts at one and is never reused.",
        field);

    /// <summary>A garment's position within its order was below the first position.</summary>
    public static Error JobIndexOutOfRange { get; } = Error.Validation(
        "orders.job-index-out-of-range",
        "A garment's position within its order starts at one.",
        "jobIndex");

    /// <summary>A printed order, estimate or garment number could not be read back into its parts.</summary>
    /// <param name="field">The field carrying the number.</param>
    public static Error DisplayNumberMalformed(string field) => Error.Validation(
        "orders.display-number-malformed",
        "That is not a number this system issues. Check it against the printed document.",
        field);

    /* Snapshots --------------------------------------------------------------------------------- */

    /// <summary>A priced result arrived without one of the three versions it was calculated from.</summary>
    /// <remarks>
    /// INV-ORD-02: a confirmed order records exactly one catalogue version, one price-list version and
    /// one tax configuration version. A figure that cannot be recomputed from its own snapshot is a
    /// defect, and a missing version is what would make it one.
    /// </remarks>
    /// <param name="field">The version that is missing.</param>
    public static Error ConfigurationVersionMissing(string field) => Error.Validation(
        "orders.configuration-version-missing",
        "A priced result records the catalogue, price-list and tax configuration versions it was "
        + "calculated from. One of them is missing.",
        field);

    /// <summary>An amount on a priced result was below zero.</summary>
    /// <remarks>
    /// The document round-off is the one exception and may be negative: rounding a total down is
    /// shown, never absorbed (<c>docs/architecture/conventions.md</c> section 1.2). A discount is
    /// carried as a positive amount and subtracted, so a negative discount is a defect rather than a
    /// surcharge.
    /// </remarks>
    /// <param name="field">The amount that was negative.</param>
    public static Error AmountNegative(string field) => Error.Validation(
        "orders.amount-negative",
        "That amount cannot be below zero.",
        field);

    /// <summary>A priced result mixed currencies across its amounts.</summary>
    public static Error CurrencyMismatch { get; } = Error.Validation(
        "orders.currency-mismatch",
        "Every amount on one document is in the same currency.",
        "totals");

    /// <summary>A priced result carried CGST or SGST alongside IGST.</summary>
    /// <remarks>
    /// INV-INV-04: place of supply decides the scheme, so a supply is either within the state or
    /// between states and never both. Applied at document level because that is the level Orders
    /// stores; Billing applies it per line.
    /// </remarks>
    public static Error BothTaxSchemesPresent { get; } = Error.Validation(
        "orders.both-tax-schemes-present",
        "A supply is either within the state or between states, never both, so CGST and SGST are "
        + "never carried alongside IGST.",
        "totals");

    /// <summary>A measurement snapshot value was neither a measured value nor a chosen option.</summary>
    /// <param name="field">The measurement field key the value answers.</param>
    public static Error MeasuredValueAmbiguous(string field) => Error.Validation(
        "orders.measured-value-ambiguous",
        "A measurement field holds either a measured value or a chosen option, and exactly one of "
        + "the two.",
        field);

    /// <summary>Two values in one measurement snapshot answered the same field key.</summary>
    /// <param name="field">The field key answered twice.</param>
    public static Error DuplicateMeasurementKey(string field) => Error.Validation(
        "orders.duplicate-measurement-key",
        "Two values answer the same measurement field.",
        field);

    /// <summary>A measured reading was zero or below.</summary>
    /// <remarks>
    /// A length is a positive quantity, and the snapshot is <strong>immutable after confirmation</strong>
    /// (INV-JOB-01): a reading of zero or below frozen onto a garment job is printed on the job card for
    /// ever, with no correction path short of cancelling the order and placing a new one. The upper end
    /// is deliberately not bounded here — what counts as unusual is a band on the template version
    /// Customers owns, and an unusual reading the tailor accepted is carried as acknowledged rather than
    /// refused. The field key travels as the target; the reading itself never travels at all
    /// (<c>docs/nfr/data-classification.md</c> section 5.2).
    /// </remarks>
    /// <param name="field">The measurement field key the reading answers.</param>
    public static Error MeasurementNotPositive(string field) => Error.Validation(
        "orders.measurement-not-positive",
        "A measured value is a length greater than zero.",
        field);

    /// <summary>Two selections in one design snapshot answered the same option group.</summary>
    /// <param name="field">The group code answered twice.</param>
    public static Error DuplicateDesignGroup(string field) => Error.Validation(
        "orders.duplicate-design-group",
        "Two selections answer the same design option group.",
        field);

    /// <summary>A design selection carried an illustration with no alternative text.</summary>
    /// <remarks>
    /// Alternative text is mandatory on every illustration (<c>docs/prd/design-options.md</c>
    /// section 7). A snapshot carrying a picture nobody can read is a job card that does not work for
    /// the person holding it.
    /// </remarks>
    public static Error IllustrationAlternativeTextRequired { get; } = Error.Validation(
        "orders.illustration-alternative-text-required",
        "An illustration carries alternative text describing it, so the job card still reads for "
        + "somebody who cannot see the picture.",
        "illustrationAlternativeText");

    /* Drafts ------------------------------------------------------------------------------------ */

    /// <summary>No order draft matches the identifier, or the caller may not see it.</summary>
    /// <remarks>
    /// One error for both cases on purpose. Telling a caller that a draft exists but is not theirs is
    /// the identifier-editing oracle the authorisation design closes
    /// (<c>tests/Tailor360.IntegrationTests/Authorization/matrix.yaml</c>).
    /// </remarks>
    public static Error DraftNotFound { get; } = Error.NotFound(
        "orders.draft-not-found",
        "No order draft matches that identifier.");

    /// <summary>The draft has already become an order.</summary>
    /// <remarks>
    /// A draft is consumed exactly once, which is what makes a confirmation retried after a lost
    /// answer reach the order it already made rather than make a second one
    /// (<c>docs/prd/state-transitions.md</c> section 2.1).
    /// </remarks>
    public static Error DraftAlreadyConfirmed { get; } = Error.Conflict(
        "orders.draft-already-confirmed",
        "That draft has already become an order. Open the order instead.");

    /// <summary>The draft outlived the window a draft is kept for.</summary>
    /// <remarks>
    /// Refused whether or not the retention job has swept the row yet, so a draft the shop stopped
    /// looking at days ago cannot quietly become a commitment.
    /// </remarks>
    public static Error DraftExpired { get; } = Error.Conflict(
        "orders.draft-expired",
        "That draft is older than the window drafts are kept for. Start a new one.");

    /// <summary>A draft was started with a window of no time at all.</summary>
    public static Error DraftLifetimeNotPositive { get; } = Error.Validation(
        "orders.draft-lifetime-not-positive",
        "A draft is kept for a window longer than no time at all.",
        "lifetime");

    /// <summary>A draft carrying no garment section was confirmed.</summary>
    public static Error DraftHasNoGarments { get; } = Error.Conflict(
        "orders.draft-has-no-garments",
        "An order is a commitment about garments. Add at least one before confirming.");

    /// <summary>A garment section, or a dependency's prerequisite, is not on this draft.</summary>
    public static Error GarmentNotOnDraft { get; } = Error.NotFound(
        "orders.garment-not-on-draft",
        "No garment on this draft matches that identifier.");

    /// <summary>A garment section was added with an identifier the draft already carries.</summary>
    public static Error GarmentAlreadyOnDraft { get; } = Error.Conflict(
        "orders.garment-already-on-draft",
        "That garment is already on this draft.");

    /// <summary>A garment reached confirmation with no measurement decision taken.</summary>
    /// <remarks>
    /// Confirmation is refused with a field error listing every garment in this state, which the
    /// application builds from the draft rather than guessing (<c>docs/IMPLEMENTATION_PLAN.md</c>
    /// issue #32b).
    /// </remarks>
    public static Error GarmentMeasurementNotDecided { get; } = Error.Validation(
        "orders.garment-measurement-not-decided",
        "Every garment needs either a confirmed measurement or an explicit reuse of an earlier one "
        + "before the order can be confirmed.",
        "garments");

    /// <summary>A garment chose to reuse a measurement without naming the version to reuse.</summary>
    public static Error MeasurementReuseNeedsVersion { get; } = Error.Validation(
        "orders.measurement-reuse-needs-version",
        "Reusing an earlier measurement needs the measurement that is being reused.",
        "measurementVersionId");

    /// <summary>A garment named a measurement version under an intent that does not reuse one.</summary>
    public static Error MeasurementVersionNotExpected { get; } = Error.Validation(
        "orders.measurement-version-not-expected",
        "A measurement is named only when an earlier one is being reused.",
        "measurementVersionId");

    /// <summary>A garment was declared to depend on itself.</summary>
    public static Error DependencyOnItself { get; } = Error.Validation(
        "orders.dependency-on-itself",
        "A garment cannot wait for itself.",
        "prerequisite");

    /// <summary>A dependency named a garment outside the same draft or the same confirmation.</summary>
    /// <remarks>
    /// INV-JOB-09 binds garments <em>of one order</em>. A dependency reaching across orders would be
    /// a promise the delivery queue could never resolve, because the two orders are delivered on
    /// their own dates to their own customers.
    /// </remarks>
    public static Error DependencyNotInSameOrder { get; } = Error.Validation(
        "orders.dependency-not-in-same-order",
        "A garment can only depend on another garment of the same order.",
        "prerequisite");

    /// <summary>The same prerequisite and kind were declared twice.</summary>
    public static Error DuplicateDependency { get; } = Error.Conflict(
        "orders.duplicate-dependency",
        "That dependency has already been declared.");

    /// <summary>A dependency was withdrawn that was never declared.</summary>
    public static Error DependencyNotDeclared { get; } = Error.NotFound(
        "orders.dependency-not-declared",
        "No such dependency was declared on this garment.");

    /* Estimates --------------------------------------------------------------------------------- */

    /// <summary>No estimate matches the identifier, or the caller may not see it.</summary>
    public static Error EstimateNotFound { get; } = Error.NotFound(
        "orders.estimate-not-found",
        "No estimate matches that identifier.");

    /// <summary>The estimate is no longer outstanding, so it cannot be superseded or converted.</summary>
    public static Error EstimateNotIssued { get; } = Error.Conflict(
        "orders.estimate-not-issued",
        "That estimate is no longer outstanding.");

    /// <summary>A newer estimate has already replaced this one.</summary>
    /// <remarks>
    /// A reissue supersedes rather than edits, so the superseded document still renders with the
    /// checksum it was stored under (<c>docs/prd/state-transitions.md</c> section 2.2).
    /// </remarks>
    public static Error EstimateAlreadySuperseded { get; } = Error.Conflict(
        "orders.estimate-already-superseded",
        "A newer estimate has replaced that one. Work from the newest estimate.");

    /// <summary>The draft was already confirmed into an order from this estimate.</summary>
    public static Error EstimateAlreadyConverted { get; } = Error.Conflict(
        "orders.estimate-already-converted",
        "That estimate has already been confirmed into an order.");

    /// <summary>An estimate was told to convert against an order made from a different draft.</summary>
    /// <remarks>
    /// <c>docs/prd/state-transitions.md</c> section 2.2 defines the conversion relative to the draft the
    /// estimate prices — "order confirmed <strong>from the draft</strong>". Spending an estimate on
    /// another draft's order would also strand this one: a converted estimate is never superseded, so the
    /// quote still genuinely outstanding for the draft would have no command left that could retire it.
    /// </remarks>
    public static Error EstimateNotForThisDraft { get; } = Error.Validation(
        "orders.estimate-not-for-this-draft",
        "That estimate prices a different order draft from the one this order was confirmed from.",
        "orderDraftId");

    /// <summary>An estimate was told to supersede itself.</summary>
    public static Error EstimateCannotSupersedeItself { get; } = Error.Validation(
        "orders.estimate-cannot-supersede-itself",
        "An estimate cannot replace itself.",
        "supersedingEstimateId");

    /// <summary>An estimate was issued with a validity date earlier than its issue date.</summary>
    public static Error EstimateValidityBeforeIssue { get; } = Error.Validation(
        "orders.estimate-validity-before-issue",
        "An estimate cannot stop being valid before the day it was issued.",
        "validUntil");

    /// <summary>A checksum was recorded for a document that already holds one.</summary>
    /// <remarks>
    /// Recorded once and never replaced, so a superseded estimate keeps the checksum its stored
    /// document was written with and a reader can still prove the two match.
    /// </remarks>
    public static Error EstimateArtefactAlreadyRecorded { get; } = Error.Conflict(
        "orders.estimate-artefact-already-recorded",
        "That estimate already holds the document it was issued with, and a stored document is "
        + "never replaced.");

    /* Orders ------------------------------------------------------------------------------------ */

    /// <summary>No order matches the identifier, or the caller may not see it.</summary>
    public static Error OrderNotFound { get; } = Error.NotFound(
        "orders.order-not-found",
        "No order matches that identifier.");

    /// <summary>A confirmation carried no garment specifications.</summary>
    public static Error OrderHasNoGarmentJobs { get; } = Error.Validation(
        "orders.order-has-no-garment-jobs",
        "An order is confirmed with at least one garment.",
        "garments");

    /// <summary>The requested status change is not legal from the order's current status.</summary>
    /// <remarks>
    /// Takes the two statuses rather than two strings, so a <em>garment</em> status can no longer be
    /// passed as an order's intent: an order is never "on hold" (state-transitions.md section 2), and a
    /// refusal that says it was is one the counter cannot act on.
    /// </remarks>
    /// <param name="from">The status the order is in.</param>
    /// <param name="to">The status the caller asked for.</param>
    public static Error StatusTransitionNotAllowed(OrderStatus from, OrderStatus to) => Error.Conflict(
        "orders.status-transition-not-allowed",
        $"An order cannot move from {Words(from)} to {Words(to)}.");

    /// <summary>
    /// Something was attempted against an order that has reached the end of its lifecycle.
    /// </summary>
    /// <remarks>
    /// The refusal every job-scoped command on a cancelled or closed order gives. It names the order's
    /// own status and no target at all, because the caller was moving a <em>garment</em> and the order
    /// has no status it was being asked to move to. Shares the code of
    /// <see cref="StatusTransitionNotAllowed(OrderStatus, OrderStatus)"/>: the same thing refused, said
    /// to somebody who asked a different question.
    /// </remarks>
    /// <param name="status">The status the order has reached.</param>
    public static Error OrderNoLongerOpen(OrderStatus status) => Error.Conflict(
        "orders.status-transition-not-allowed",
        $"This order is {Words(status)}, so nothing further can be recorded against it.");

    /// <summary>A revision was attempted once a garment job had left <c>confirmed</c>.</summary>
    /// <remarks>
    /// INV-ORD-05 and INV-JOB-02: the workflow version is pinned at start of production and the order
    /// stops being re-priceable from that moment. The only route afterwards is an alteration request
    /// (issue #34).
    /// </remarks>
    public static Error RevisionRefusedAfterProduction { get; } = Error.Conflict(
        "orders.revision-refused-after-production",
        "Work has started on this order, so it can no longer be revised. Raise an alteration "
        + "instead.");

    /// <summary>A prohibited financial, stock or custody state stands against the cancellation.</summary>
    /// <remarks>
    /// INV-ORD-06 and <c>docs/prd/exceptions.md</c> EX-08: cancellation is <strong>blocked, not
    /// forced</strong>. The compensating flows — the credit note, the returned material, the custody
    /// correction — run first, and nothing is deleted to make room for them. The message names the
    /// blocking state's code, which is operational configuration and not personal data.
    /// </remarks>
    /// <param name="prohibitedState">
    /// The code of the state that blocks it. A configured code, trimmed and bounded by
    /// <c>Order.MaximumReasonCodeLength</c> before it reaches here, so a sentence — or anything read off
    /// a customer record — cannot travel into a problem detail.
    /// </param>
    public static Error CancellationBlocked(string prohibitedState) => Error.Conflict(
        "orders.cancellation-blocked",
        $"This order cannot be cancelled while {prohibitedState} stands. That has to be settled "
        + "first; cancellation is blocked rather than forced.");

    /// <summary>One confirmation carried two garments that cannot both be true.</summary>
    /// <param name="field">The field the two garments collided on.</param>
    public static Error DuplicateGarmentJob(string field) => Error.Validation(
        "orders.duplicate-garment-job",
        "Two garments in one confirmation carry the same identity, the same position, the same "
        + "number or the same dependency.",
        field);

    /// <summary>
    /// A confirmation numbered its garments with a gap, or from something other than one.
    /// </summary>
    /// <remarks>
    /// The job index is the garment's <em>position within its order</em> and it is printed on the job
    /// card as <c>J-…-000001-05</c>. Positions 5 and 9 on a two-garment order print a set the counter
    /// cannot hand over and a workboard cannot sort, and confirmation is irreversible
    /// (state-transitions.md section 7), so it is refused here rather than discovered on paper.
    /// </remarks>
    public static Error GarmentJobIndicesNotContiguous { get; } = Error.Validation(
        "orders.garment-job-indices-not-contiguous",
        "Garments are numbered from one with no gaps, in the order they appear on the order.",
        "jobIndex");

    /// <summary>
    /// One confirmation mixed two catalogue, price-list or tax configuration versions.
    /// </summary>
    /// <remarks>
    /// INV-ORD-02: a confirmed order records <strong>exactly one</strong> catalogue version, one
    /// price-list version and one tax configuration version. A single
    /// <c>Snapshots.PriceSnapshot</c> can guarantee that it names <em>a</em> version; only the order can
    /// guarantee that the whole confirmation names <em>one</em>, which is why this refusal lives on the
    /// aggregate.
    /// </remarks>
    /// <param name="field">The version that disagreed.</param>
    public static Error ConfigurationVersionNotShared(string field) => Error.Validation(
        "orders.configuration-version-not-shared",
        "Every garment on one order is priced and validated under the same catalogue, price-list and "
        + "tax configuration version. This one carries more than one.",
        field);

    /// <summary>
    /// The design copy frozen onto a garment answers a different category or service type.
    /// </summary>
    /// <remarks>
    /// INV-JOB-01 freezes the snapshot permanently, so a job whose row says <c>blouse</c> while the
    /// design copy the job card renders from says <c>shirt</c> is a disagreement no later reader can
    /// resolve and no command can correct.
    /// </remarks>
    /// <param name="field">The key that disagreed.</param>
    public static Error DesignSnapshotNotForThisGarment(string field) => Error.Validation(
        "orders.design-snapshot-not-for-this-garment",
        "The design copy for this garment answers a different category or service type from the one "
        + "the garment was taken under.",
        field);

    /// <summary>
    /// Two or more garments of one confirmation wait for each other in a circle.
    /// </summary>
    /// <remarks>
    /// INV-JOB-09: a <c>finish_before</c> dependency blocks the dependent garment's first phase, so a
    /// circle is a set of garments none of which can ever start. Confirmation is irreversible
    /// (state-transitions.md section 7) and the only remedy afterwards is a cancellation and a new
    /// order, which is exactly why the one-garment case is refused here too
    /// (<see cref="DependencyOnItself"/>).
    /// </remarks>
    public static Error DependencyCycle { get; } = Error.Validation(
        "orders.dependency-cycle",
        "These garments wait for each other in a circle, so none of them could ever be started.",
        "prerequisite");

    /// <summary>A garment job number was minted from a different order number.</summary>
    /// <remarks>
    /// The job number carries the order number it belongs to, so this is checked structurally rather
    /// than by comparing text: a number minted for another order cannot reach a job card of this one.
    /// </remarks>
    public static Error GarmentJobNumberNotOfThisOrder { get; } = Error.Validation(
        "orders.garment-job-number-not-of-this-order",
        "That garment number belongs to a different order.",
        "jobNumber");

    /* Garment jobs ------------------------------------------------------------------------------ */

    /// <summary>The named garment job is not on this order.</summary>
    public static Error GarmentJobNotFound { get; } = Error.NotFound(
        "orders.garment-job-not-found",
        "No garment on this order matches that identifier.");

    /// <summary>The requested status change is not legal from the garment job's current status.</summary>
    /// <param name="from">The status the garment job is in.</param>
    /// <param name="to">The status the caller asked for.</param>
    public static Error JobStatusTransitionNotAllowed(GarmentJobStatus from, GarmentJobStatus to)
        => Error.Conflict(
            "orders.job-status-transition-not-allowed",
            $"A garment cannot move from {Words(from)} to {Words(to)}.");

    /// <summary>
    /// A command that does not move the garment's status was refused by the status it is in.
    /// </summary>
    /// <remarks>
    /// Rescheduling is the case: the promised date moves and the status does not, so naming a target
    /// status would invent a transition the lifecycle does not have (state-transitions.md section 3.2).
    /// Shares the code of <see cref="JobStatusTransitionNotAllowed"/>, because it is the same refusal
    /// said about a command rather than about a move.
    /// </remarks>
    /// <param name="from">The status the garment job is in.</param>
    /// <param name="action">What was attempted, as a past participle — "rescheduled".</param>
    public static Error JobActionNotAllowed(GarmentJobStatus from, string action) => Error.Conflict(
        "orders.job-status-transition-not-allowed",
        $"A garment cannot be {action} while it is {Words(from)}.");

    /// <summary>
    /// A ready-for-delivery result was worked out before the garment moved, so it no longer describes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gate is a function of facts gathered outside this module (state-transitions.md section 9.1),
    /// and a garment can be held, cancelled or handed over between the gathering and the applying. A
    /// verdict of <em>ready</em> that reaches a garment which is no longer in production is therefore
    /// stale, and applying it would materialise <c>ready_state = true</c> on a garment the status says is
    /// not ready — the exact disagreement section 9.1's table of reason codes exists to prevent
    /// (INV-JOB-07, CI-03).
    /// </para>
    /// <para>
    /// Refused rather than downgraded: this type cannot recompute the six predicates, so the honest
    /// answer is that the verdict has to be worked out again against what is true now.
    /// </para>
    /// </remarks>
    public static Error ReadyGateOutcomeStale { get; } = Error.Conflict(
        "orders.ready-gate-outcome-stale",
        "That ready-for-delivery result was worked out before this garment last moved. Work it out "
        + "again.");

    /// <summary>Production was started on a garment that already carries a pinned workflow version.</summary>
    /// <remarks>
    /// INV-JOB-02: the pinned version never changes, even when a newer workflow version is published
    /// while the garment is being made. A garment half made under two versions is one nobody can say
    /// was finished.
    /// </remarks>
    public static Error WorkflowVersionAlreadyPinned { get; } = Error.Conflict(
        "orders.workflow-version-already-pinned",
        "This garment already has its process version fixed, and a fixed version never changes.");

    /// <summary>A <c>finish_before</c> prerequisite has not been established as finished.</summary>
    /// <remarks>
    /// INV-JOB-09. The message names the sibling garment number, which is operational data printed on
    /// a job card and is not personal data.
    /// </remarks>
    /// <param name="prerequisiteJobNumber">The garment that has to be finished first.</param>
    public static Error PrerequisiteNotFinished(string prerequisiteJobNumber) => Error.Conflict(
        "orders.prerequisite-not-finished",
        $"Garment {prerequisiteJobNumber} has to be finished before work starts on this one.");

    /// <summary>A prohibited financial, stock or custody state stands against the cancellation.</summary>
    /// <param name="prohibitedState">
    /// The code of the state that blocks it, trimmed and bounded by <c>Order.MaximumReasonCodeLength</c>
    /// before it reaches here, as <see cref="CancellationBlocked"/> is.
    /// </param>
    public static Error JobCancellationBlocked(string prohibitedState) => Error.Conflict(
        "orders.job-cancellation-blocked",
        $"This garment cannot be cancelled while {prohibitedState} stands. That has to be settled "
        + "first; cancellation is blocked rather than forced.");

    /// <summary>A ready-for-delivery result was applied to a garment it was not worked out for.</summary>
    /// <remarks>
    /// INV-JOB-07: the gate is the only writer of ready state, and a verdict is about exactly one
    /// garment. Applying one garment's verdict to another would make the single-writer guarantee
    /// meaningless.
    /// </remarks>
    public static Error ReadyGateOutcomeForAnotherJob { get; } = Error.Validation(
        "orders.ready-gate-outcome-for-another-job",
        "That ready-for-delivery result was worked out for a different garment.",
        "garmentJobId");

    /// <summary>
    /// A ready-for-delivery result was applied without the rest of the <c>deliver_together</c> parcel it was
    /// worked out inside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// INV-JOB-09: a <c>deliver_together</c> dependency binds its garments at the ready gate and in the delivery
    /// queue, so the parcel moves together or not at all. <c>ReadyGate.EvaluateSet</c> judges every member in one
    /// evaluation and records the rest of the parcel on each verdict; applying one of them on its own, while a
    /// partner stands somewhere else, is half a parcel on the delivery queue bound to a garment still being made.
    /// </para>
    /// <para>
    /// Not a refusal of the verdict itself, which may be perfectly good — it is a refusal to record it apart from
    /// the garments it was reached with. The remedy is to apply the whole evaluation. The message names the
    /// garment job number, which is operational data printed on a job card and is not personal data.
    /// </para>
    /// </remarks>
    /// <param name="boundGarmentJobNumber">The garment that goes to the customer with this one.</param>
    public static Error ReadyGateWouldSplitParcel(string boundGarmentJobNumber) => Error.Conflict(
        "orders.ready-gate-parcel-split",
        $"Garment {boundGarmentJobNumber} goes to the customer with this one, so its ready-for-delivery "
        + "result has to be worked out and recorded in the same step.");

    /// <summary>
    /// A garment was handed over while a garment it was promised to travel with is not ready to go with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// INV-JOB-09's second half. <see cref="ReadyGateWouldSplitParcel"/> refuses a verdict recorded apart from
    /// the parcel it was reached inside; this refuses the handover itself, which is a different moment and a
    /// different remedy. A parcel promoted together can still come apart afterwards — a hold, and then a resume,
    /// close one garment's ready state and no other's — and by the time the split shows it is not a verdict that
    /// needs working out again but a garment that has to wait for its partner, or a branch decision to let it go
    /// without one.
    /// </para>
    /// <para>
    /// Its own code rather than <c>orders.ready-gate-parcel-split</c> because nothing here is wrong with the
    /// ready gate: both verdicts are current and correct, and telling the delivery staff at the door to have a
    /// ready-for-delivery result worked out again would send them to fix something that is not broken. A garment
    /// job number is operational data printed on a job card and is not personal data.
    /// </para>
    /// </remarks>
    /// <param name="boundGarmentJobNumber">The garment that would be left behind.</param>
    public static Error DeliveryWouldSplitParcel(string boundGarmentJobNumber) => Error.Conflict(
        "orders.delivery-would-split-parcel",
        $"Garment {boundGarmentJobNumber} goes to the customer with this one and is not ready yet, so this "
        + "garment cannot be handed over on its own.");

    /// <summary>
    /// One <c>deliver_together</c> parcel was evaluated under two different branch dispatch policies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whether partial delivery is permitted is a property of the <em>branch</em> — <c>whole_order</c>,
    /// <c>per_job</c> or <c>exception</c>, issue #48 — and not of a garment, so it is the same answer for every
    /// garment of one parcel. Supplied per garment because <c>ReadyGate.ReadyGateInputs</c> carries the facts for
    /// one garment, and a caller that answered it differently for two members of one parcel got an incoherent
    /// parcel the domain then honoured: the member marked partial carried no binding at all, so its verdict could
    /// be recorded on its own while its partner still stood in production.
    /// </para>
    /// <para>
    /// A separate refusal from <see cref="ReadyGateWouldSplitParcel"/> because it is a different situation said
    /// at a different moment: that one refuses to <em>record</em> half a parcel, and this one refuses to
    /// <em>work out</em> a parcel under two policies at once. Nothing is evaluated and nothing is written; the
    /// remedy is to gather the branch's policy once and supply it for the whole parcel.
    /// </para>
    /// </remarks>
    public static Error DispatchPolicyNotShared { get; } = Error.Validation(
        "orders.dispatch-policy-not-shared",
        "Every garment that goes to the customer together is worked out under one branch delivery policy. "
        + "This evaluation carries two.",
        "partialDeliveryPermitted");

    /* Status wording ---------------------------------------------------------------------------- */

    /// <summary>
    /// An order status as the person at the counter says it.
    /// </summary>
    /// <remarks>
    /// The stored statuses of <c>docs/prd/state-transitions.md</c> section 2 are <c>in_production</c> and
    /// the rest; the member names are <c>InProduction</c> and the rest. Neither is what somebody reads on
    /// a screen, so the words are written out once here rather than a member name being interpolated at
    /// twenty call sites.
    /// </remarks>
    private static string Words(OrderStatus status) => status switch
    {
        OrderStatus.Draft => "a draft",
        OrderStatus.Confirmed => "confirmed",
        OrderStatus.InProduction => "in production",
        OrderStatus.Ready => "ready for delivery",
        OrderStatus.Delivered => "delivered",
        OrderStatus.Closed => "closed",
        OrderStatus.Cancelled => "cancelled",
        _ => "in a state this system does not recognise",
    };

    /// <summary>A garment job status as the person at the counter says it.</summary>
    private static string Words(GarmentJobStatus status) => status switch
    {
        GarmentJobStatus.Confirmed => "confirmed",
        GarmentJobStatus.InProduction => "in production",
        GarmentJobStatus.OnHold => "on hold",
        GarmentJobStatus.Ready => "ready for delivery",
        GarmentJobStatus.Delivered => "delivered",
        GarmentJobStatus.Closed => "closed",
        GarmentJobStatus.Cancelled => "cancelled",
        _ => "in a state this system does not recognise",
    };
}
