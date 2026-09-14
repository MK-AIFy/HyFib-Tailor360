import { ApiError } from '../auth/apiClient'
import type { MessageKey } from '../i18n/en-IN'

/**
 * The refusals the billing routes answer with, in the shop's words.
 *
 * Only the ones a person at the counter or in the office can act on are named; anything else falls
 * through to the generic sentence for its status, which `AuthProblemAlert` already renders with the
 * correlation reference for support. A code is never shown.
 */
const CODE_MESSAGES: Readonly<Record<string, MessageKey>> = {
  'billing.cashier-session-required': 'billing.problem.noOpenSession',
  'billing.payment-mode-not-available': 'billing.problem.modeNotAvailable',
  'billing.reference-required': 'billing.problem.referenceRequired',
  'billing.reference-looks-like-a-card': 'billing.problem.referenceLooksLikeCard',
  'billing.payment-reference-duplicated': 'billing.problem.referenceDuplicated',
  'billing.order-at-another-branch': 'billing.problem.orderAtAnotherBranch',
  'billing.order-not-known': 'billing.problem.orderNotKnown',
  'billing.order-not-yet-confirmed': 'billing.problem.orderNotConfirmed',
  'billing.order-cancelled': 'billing.problem.orderCancelled',
  'billing.no-advance-held': 'billing.problem.noAdvanceHeld',
  'billing.allocation-exceeds-invoice': 'billing.problem.allocationExceedsInvoice',
  'billing.allocation-invoice-not-of-order': 'billing.problem.allocationInvoiceNotOfOrder',
  'billing.invoice-not-found': 'billing.problem.invoiceNotFound',
  // The one code the barcode lookup answers for four indistinguishable cases — another branch's
  // invoice, another organisation's, a broken check character, a payload matching nothing — by
  // design (matrix row 379, ResolveInvoiceBarcode's own description). `billing.value-required` with
  // field `branch` is the route's only other answer, and is handled locally by the lookup field
  // itself rather than here, because that code is shared with unrelated validation failures
  // elsewhere in Billing and is not safe to map to one sentence for every screen.
  'billing.document-not-found': 'billing.problem.barcodeNotFound',
  // The worker has not rendered the artefact yet (INV-INV-08): an operational alert, never a reason
  // to post again. The same code names both an invoice's own document and a note's.
  'billing.document-not-available': 'billing.problem.documentNotAvailable',
  'billing.copies-out-of-range': 'billing.problem.copiesOutOfRange',
  'billing.cashier-session-not-yours': 'billing.problem.sessionNotYours',
  'billing.cashier-session-already-closed': 'billing.problem.sessionAlreadyClosed',
  'billing.cashier-session-already-open': 'billing.problem.sessionAlreadyOpen',
  'billing.variance-reason-required': 'billing.problem.varianceReasonRequired',
  'billing.reconciliation-approval-by-same-user': 'billing.problem.reconciliationSameUser',
  'billing.reconciliation-approval-not-required': 'billing.problem.reconciliationNotRequired',
  'billing.reconciliation-batch-already-approved': 'billing.problem.reconciliationAlreadyApproved',
  'billing.dispatch-exception-balance-exceeded': 'billing.problem.dispatchBalanceExceeded',
  'billing.dispatch-exception-job-not-of-order': 'billing.problem.dispatchJobNotOfOrder',
  'billing.dispatch-exception-amount-not-positive': 'billing.problem.dispatchAmountNotPositive',
  // Drafting, discarding and posting an invoice (#345).
  'billing.calculation-not-found': 'billing.problem.calculationNotFound',
  'billing.customer-not-found': 'billing.problem.customerNotFound',
  'billing.order-revised-since-draft': 'billing.problem.orderRevisedSinceDraft',
  'billing.job-already-invoiced': 'billing.problem.jobAlreadyInvoiced',
  // Sends the person to re-price rather than implying the invoice — or the price list — is broken.
  'billing.snapshot-mismatch': 'billing.problem.snapshotMismatch',
  'billing.totals-mismatch': 'billing.problem.totalsMismatch',
  'billing.calculation-for-another-branch': 'billing.problem.calculationForAnotherBranch',
  'billing.branch-not-known': 'billing.problem.branchNotKnown',
  'billing.invoice-not-editable': 'billing.problem.invoiceNotEditable',
  // The precondition failure a stale ETag answers with — refetch and say so, never retry blindly.
  'billing.invoice-changed': 'billing.problem.invoiceChanged',
  'billing.job-cancelled': 'billing.problem.jobCancelled',
  'billing.job-repeated': 'billing.problem.jobRepeated',
  'billing.line-not-a-garment-job': 'billing.problem.lineNotAGarmentJob',
  'billing.configuration-missing': 'billing.problem.configurationMissing',
  'billing.reason-required': 'billing.problem.reasonRequired',
  'billing.reason-not-well-formed': 'billing.problem.reasonNotWellFormed',
  // Cancelling an invoice, and issuing credit and debit notes (#354).
  'billing.invoice-not-posted': 'billing.problem.invoiceNotPosted',
  'billing.invoice-already-cancelled': 'billing.problem.invoiceAlreadyCancelled',
  // Points at the credit note as the remaining route — a credit note posts the same reversal
  // whichever day it happens on (OD-23's recorded reasoning).
  'billing.cancellation-window-closed': 'billing.problem.cancellationWindowClosed',
  // `billing.note-line-not-on-invoice` and `billing.note-exceeds-line` are also read against the
  // offending row's own field key (`lines[<garmentJobId>].taxableValue`) by AdjustmentNoteRoute
  // itself, so the row that failed is obvious — the banner mapping here is the fallback for a
  // refusal this screen did not expect on any particular row.
  'billing.note-line-not-on-invoice': 'billing.problem.noteLineNotOnInvoice',
  'billing.note-exceeds-line': 'billing.problem.noteExceedsLine',
  'billing.note-value-not-well-formed': 'billing.problem.noteValueNotWellFormed',
  // The pricing administration foundation (#237). The GSTIN and state-code refusals are rendered as
  // field errors beside their own control rather than through this map's generic alert — see
  // `GstRegistrationsRoute.tsx` — but the sentence is still looked up here, once, so both places agree.
  'billing.gstin-not-well-formed': 'pricing.problem.gstinNotWellFormed',
  'billing.gstin-state-mismatch': 'pricing.problem.gstinStateMismatch',
  'billing.state-code-not-well-formed': 'pricing.problem.stateCodeNotWellFormed',
  'billing.dates-not-ordered': 'pricing.problem.datesNotOrdered',
  'billing.registration-overlaps': 'pricing.problem.registrationOverlaps',
  'billing.registration-changed': 'pricing.problem.registrationChanged',
  'billing.registration-not-found': 'pricing.problem.registrationNotFound',
  // The price-list register (#252). `billing.value-required` names one of several fields through the
  // problem's own `errors` map — the register reads that map itself to say which control is missing,
  // and this sentence is what it attaches there, so the code is not repeated per field.
  'billing.branch-not-found': 'pricing.problem.branchNotFound',
  'billing.price-list-not-found': 'pricing.problem.priceListNotFound',
  'billing.price-list-changed': 'pricing.problem.priceListChanged',
  'billing.draft-number-conflict': 'pricing.problem.draftNumberConflict',
  // Drafting and editing a tax configuration version and its tax codes (E09-F01-5). The field-level
  // ones — code, classification, rate, component — are read against their own control or component
  // row by `TaxConfigurationEditorRoute` and `TaxCodeForm`, via `billingProblemField` below; the
  // sentence is still looked up here, once, so both places and the page-level fallback agree.
  // `billing.code-not-unique`, `billing.code-not-well-formed` and `billing.value-required` are
  // shared with the price-list register above and mapped once, here.
  'billing.version-not-editable': 'pricing.problem.versionNotEditable',
  'billing.version-changed': 'pricing.problem.versionChanged',
  'billing.version-not-found': 'pricing.problem.versionNotFound',
  'billing.code-not-unique': 'pricing.problem.codeNotUnique',
  'billing.code-not-well-formed': 'pricing.problem.codeNotWellFormed',
  'billing.classification-not-well-formed': 'pricing.problem.classificationNotWellFormed',
  'billing.rate-out-of-range': 'pricing.problem.rateOutOfRange',
  'billing.rate-not-well-formed': 'pricing.problem.rateNotWellFormed',
  'billing.component-duplicated': 'pricing.problem.componentDuplicated',
  'billing.component-not-well-formed': 'pricing.problem.componentNotWellFormed',
  'billing.value-required': 'pricing.problem.valueRequired',
  'billing.value-too-long': 'pricing.problem.valueTooLong',
  // 'billing.reason-not-well-formed' is already mapped above, to the shared sentence every module
  // uses for the same code.
}

/** The code of a failure, when the server sent one. */
export function billingProblemCode(failure: unknown): string | undefined {
  return failure instanceof ApiError ? failure.code : undefined
}

/** The message for a failure the billing screens know how to explain, or undefined. */
export function billingProblemMessage(failure: unknown): MessageKey | undefined {
  const code = billingProblemCode(failure)
  return code === undefined ? undefined : CODE_MESSAGES[code]
}

/**
 * The field a validation failure named, when the server sent one.
 *
 * Read from the RFC 9457 `errors` map's one key — every domain check in this module fails on the
 * first thing wrong, so there is never more than one. This is distinct from `billingProblemCode`:
 * the code says *which sentence* to show (`billing.rate-out-of-range` reads the same whichever
 * component it is about), and the field says *where* — `code`, `classification`, or an indexed
 * `rates[2].ratePercent` a screen matches back to the component row it sent at that position.
 */
export function billingProblemField(failure: unknown): string | undefined {
  const errors = failure instanceof ApiError ? failure.problem?.errors : undefined
  const [field] = Object.keys(errors ?? {})
  return field
}
