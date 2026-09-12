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
