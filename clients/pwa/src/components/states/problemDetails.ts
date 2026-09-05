import type { MessageKey } from '../../i18n/en-IN'
import type { ValidationProblemDetails } from '../../design-system/foundations/FieldProps'

/**
 * Turning a failed request into a sentence a person can act on.
 *
 * docs/nfr/accessibility-localisation.md section 8.2 is the rule: server problem details (RFC 9457)
 * are rendered in plain language, carry the correlation identifier for support, and never expose a
 * stack trace. This module is that rule as code — a `.ts` sibling rather than part of the component,
 * because a mapping from a status code to a message identifier is data a test should be able to
 * assert without rendering anything.
 *
 * The `/api/version` endpoint is the only server this application talks to today; #53 defines the
 * problem-details contract properly and #51 adds the 426 update prompt. The shape below is the
 * subset every one of those responses will carry, extended from the validation shape the forms
 * family already reads so that a screen never has two ideas of what a problem is.
 */
export interface ProblemDetails extends ValidationProblemDetails {
  /** The specific occurrence, per RFC 9457. */
  readonly instance?: string
  /**
   * The correlation identifier the server put on the request, quoted back to support. It is the one
   * technical string this design system ever shows a person, and it exists so that "it did not work"
   * becomes a line a technical reviewer can find in a log.
   */
  readonly correlationId?: string
}

/**
 * Why a request failed, in the terms a person needs rather than the terms HTTP uses.
 *
 * `network` is the one that has no status code at all: the request never left the device, or the
 * connection dropped before an answer came back. It is deliberately distinct from `offline`, which
 * is not a failure — `OfflineBlockedAction` handles a connection that is known to be absent, and
 * this handles one that failed while it was believed to be there.
 */
export const REQUEST_FAILURE_CAUSES = [
  'network',
  'timeout',
  'rateLimited',
  'server',
  'conflict',
  'notFound',
  'unknown',
] as const

export type RequestFailureCause = (typeof REQUEST_FAILURE_CAUSES)[number]

/** The plain-language sentence for each cause. */
export const FAILURE_CAUSE_MESSAGES: Record<RequestFailureCause, MessageKey> = {
  network: 'states.problem.network',
  timeout: 'states.problem.timeout',
  rateLimited: 'states.problem.rateLimited',
  server: 'states.problem.server',
  conflict: 'states.problem.conflict',
  notFound: 'states.problem.notFound',
  unknown: 'states.problem.unknown',
}

/**
 * The cause a status code stands for.
 *
 * 408 and 504 are timeouts, 429 is the rate-limit policy catalogue of plan Section 4.4 doing its
 * job, 409 and 412 are the `ETag`/`If-Match` concurrency tokens of #53, 404 and 410 are gone, and
 * every 5xx is the server's problem rather than the person's — which is why the message says so.
 * Anything else, including a 400 that is not a validation failure, is `unknown`: guessing at a
 * cause produces a confident sentence that is wrong, which is worse than an honest one.
 */
export function failureCauseForStatus(status: number | undefined): RequestFailureCause {
  if (status === undefined) {
    return 'network'
  }
  if (status === 408 || status === 504) {
    return 'timeout'
  }
  if (status === 429) {
    return 'rateLimited'
  }
  if (status === 409 || status === 412) {
    return 'conflict'
  }
  if (status === 404 || status === 410) {
    return 'notFound'
  }
  if (status >= 500) {
    return 'server'
  }
  return 'unknown'
}

/**
 * Anything that looks like a stack trace, an exception class or a query, so it can be dropped.
 *
 * A well-behaved server sends a human sentence in `detail`. A misconfigured one sends the exception
 * message, and in Development a whole stack. Section 8.2 forbids showing that to a person on the
 * shop floor, and forbidding it by rule alone means it ships the first time a server is
 * misconfigured — so the rule is a function, and the function is tested.
 */
const MACHINE_TEXT =
  /(\n\s*at\s)|(Exception\b)|(\bStack ?trace\b)|(\b[A-Za-z][A-Za-z0-9]*\.[A-Za-z][A-Za-z0-9]*\.[A-Za-z][A-Za-z0-9]*\b)|(SELECT\s.+\sFROM\s)/i

/** The longest server sentence that is still a sentence rather than a dump. */
const MAX_DETAIL_LENGTH = 240

/**
 * The server's own `detail`, when it is safe and useful to show, and undefined otherwise.
 *
 * Shown *underneath* the product's plain-language sentence, never instead of it: the person always
 * gets a sentence this application wrote, and the server's own words are an addition for the cases
 * where the server knows something the client cannot — "the branch is closed for the day", "this
 * invoice was already posted at 4:31 PM".
 */
export function plainLanguageDetail(detail: string | undefined): string | undefined {
  if (detail === undefined) {
    return undefined
  }
  const trimmed = detail.trim()
  if (trimmed.length === 0 || trimmed.length > MAX_DETAIL_LENGTH) {
    return undefined
  }
  if (MACHINE_TEXT.test(trimmed)) {
    return undefined
  }
  return trimmed
}
