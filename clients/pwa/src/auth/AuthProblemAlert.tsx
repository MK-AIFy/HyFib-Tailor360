import { useIntl } from 'react-intl'
import { Alert } from '../components/primitives/Alert'
import { FAILURE_CAUSE_MESSAGES, failureCauseForStatus } from '../components/states/problemDetails'
import { ApiError } from './apiClient'
import { authProblemMessage } from './authProblems'
import './auth.css'

/**
 * A failed authentication request, in words a person can act on.
 *
 * One component for every screen in this family, because the rule it follows is easy to break one
 * screen at a time: the sentence comes from this application's own catalogue, never from the
 * server's `detail`, and the correlation identifier is the only technical string a person is ever
 * shown. A screen that rendered `problem.detail` directly would put a stack trace on a counter the
 * first time an environment was misconfigured (docs/nfr/accessibility-localisation.md section 8.2).
 *
 * It announces assertively. That is reserved in this product for a failure that has stopped somebody
 * mid-task, and a refused sign-in is exactly that: there is nothing else on the screen to do.
 */
export interface AuthProblemAlertProps {
  /** The thrown value. Anything that is not a failure renders nothing. */
  readonly failure: unknown
  /** A heading for the message, when the screen wants one. */
  readonly title?: string
}

export function AuthProblemAlert({ failure, title }: AuthProblemAlertProps) {
  const intl = useIntl()

  if (failure === null || failure === undefined) {
    return null
  }

  const specific = authProblemMessage(failure)
  const status = failure instanceof ApiError ? failure.status : undefined
  const correlationId = failure instanceof ApiError ? failure.problem?.correlationId : undefined

  const sentence =
    specific === undefined
      ? intl.formatMessage({ id: FAILURE_CAUSE_MESSAGES[failureCauseForStatus(status)] })
      : intl.formatMessage({ id: specific.id }, specific.values)

  return (
    <Alert live="assertive" tone="danger" {...(title === undefined ? {} : { title })}>
      <p className="state-line">{sentence}</p>
      {correlationId === undefined ? null : (
        <p className="state-line state-line--reference">
          {intl.formatMessage({ id: 'states.retryable.reference' }, { correlationId })}
        </p>
      )}
    </Alert>
  )
}
