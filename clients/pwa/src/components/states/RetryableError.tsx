import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Alert } from '../primitives/Alert'
import { Button } from '../primitives/Button'
import {
  FAILURE_CAUSE_MESSAGES,
  failureCauseForStatus,
  plainLanguageDetail,
} from './problemDetails'
import type { ProblemDetails, RequestFailureCause } from './problemDetails'
import './states.css'

export interface RetryableErrorProps {
  /** What was being attempted: "Recording the payment", "Confirming the order". */
  readonly action: string
  /**
   * Resends the original request.
   *
   * The contract this component depends on and cannot enforce: the retry must resend the **same**
   * request with the **same** `Idempotency-Key`, and must not discard what the person typed
   * (plan Section 4.6, and 3.3.7 Redundant Entry). That is why the component says "trying again
   * sends the same request, so this cannot end up happening twice" — a sentence that is a lie if the
   * caller generates a fresh key, and a promise worth making if it does not.
   */
  readonly onRetry: () => void
  /** The retry is in flight. Keeps the control focusable and swallows a second press. */
  readonly retrying?: boolean
  /** The server's problem details, when there was a response at all. */
  readonly problem?: ProblemDetails
  /** Overrides the cause derived from `problem.status`. Pass `network` when nothing came back. */
  readonly cause?: RequestFailureCause
  /** Anything else the screen knows — which line failed, what has been kept. */
  readonly children?: ReactNode
  readonly className?: string
}

/**
 * A request that was sent and did not land, with a retry that is safe to press.
 *
 * Everything about how this reads comes from docs/nfr/accessibility-localisation.md section 8.2:
 * the failure is described in plain language, never as a code and never as a stack; the correlation
 * identifier is shown so that "it did not work" becomes a line a technical reviewer can find; and
 * the sentence says what is known rather than guessing. `problemDetails.ts` holds the mapping and
 * the rule that drops anything looking like machine text, so a misconfigured server cannot put an
 * exception message on a counter screen.
 *
 * It announces assertively. That is reserved in this product for two things, and this is the second
 * of them: a failure that has stopped the person mid-task (section 6). A Cashier who has just taken
 * a customer's card cannot be left to notice a polite region on their own.
 */
export function RetryableError({
  action,
  onRetry,
  retrying = false,
  problem,
  cause,
  children,
  className,
}: RetryableErrorProps) {
  const intl = useIntl()

  const resolved = cause ?? failureCauseForStatus(problem?.status)
  const serverDetail = plainLanguageDetail(problem?.detail)
  const correlationId = problem?.correlationId

  return (
    <Alert
      actions={
        <Button busy={retrying} iconName="refresh" onClick={onRetry} variant="primary">
          {intl.formatMessage({
            id: retrying ? 'states.retryable.retrying' : 'states.retryable.retry',
          })}
        </Button>
      }
      className={cx(className)}
      live="assertive"
      title={intl.formatMessage({ id: 'states.retryable.title' }, { action })}
      tone="danger"
    >
      <p className="state-line">{intl.formatMessage({ id: FAILURE_CAUSE_MESSAGES[resolved] })}</p>
      {serverDetail === undefined ? null : <p className="state-line">{serverDetail}</p>}
      <p className="state-line">{intl.formatMessage({ id: 'states.retryable.inputKept' })}</p>
      <p className="state-line">{intl.formatMessage({ id: 'states.retryable.safe' })}</p>
      {children}
      {correlationId === undefined ? null : (
        <p className="state-line state-line--reference">
          {intl.formatMessage({ id: 'states.retryable.reference' }, { correlationId })}
        </p>
      )}
    </Alert>
  )
}
