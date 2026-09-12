import { ApiError } from '../auth/apiClient'
import type { MessageKey } from '../i18n/en-IN'

/**
 * The refusals the capture routes answer with, in the shop's words.
 *
 * Only the ones a person at the counter can act on are named; anything else falls through to the
 * generic sentence for its status, which `AuthProblemAlert` already renders with the correlation
 * reference for support. A code is never shown.
 */
const CODE_MESSAGES: Readonly<Record<string, MessageKey>> = {
  'measurements.consent-missing': 'measurements.problem.consentMissing',
  'measurements.draft-changed': 'measurements.problem.draftChanged',
  'measurements.draft-expired': 'measurements.problem.draftExpired',
  'measurements.draft-already-confirmed': 'measurements.problem.alreadyConfirmed',
  'measurements.template-version-not-published': 'measurements.problem.templateNotPublished',
  'measurements.draft-not-found': 'measurements.problem.draftNotFound',
  'measurements.confirmation-validation-failed': 'measurements.problem.validationFailed',
  'measurements.branch-required': 'measurements.problem.branchRequired',
}

/** The code of a failure, when the server sent one. */
export function measurementProblemCode(failure: unknown): string | undefined {
  return failure instanceof ApiError ? failure.code : undefined
}

/** The message for a failure the capture screens know how to explain, or undefined. */
export function measurementProblemMessage(failure: unknown): MessageKey | undefined {
  const code = measurementProblemCode(failure)
  return code === undefined ? undefined : CODE_MESSAGES[code]
}
