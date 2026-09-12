import { useIntl } from 'react-intl'
import { AuthProblemAlert } from '../auth/AuthProblemAlert'
import { ApiError } from '../auth/apiClient'
import { Alert } from '../components/primitives/Alert'
import { measurementProblemMessage } from './measurementProblems'

export interface MeasurementProblemAlertProps {
  readonly failure: unknown
}

/**
 * A refusal from the capture routes, in the shop's words where the screen knows them.
 *
 * `AuthProblemAlert` knows the authentication vocabulary and the generic sentence for each status;
 * it does not know that `409 measurements.draft-expired` means "start again from the customer"
 * rather than "somebody else changed this". The codes this screen can explain are explained here,
 * with the correlation reference kept for support; everything else falls through unchanged.
 */
export function MeasurementProblemAlert({ failure }: MeasurementProblemAlertProps) {
  const intl = useIntl()
  const id = measurementProblemMessage(failure)

  if (id === undefined) {
    return <AuthProblemAlert failure={failure} />
  }

  const correlationId = failure instanceof ApiError ? failure.problem?.correlationId : undefined

  return (
    <Alert live="assertive" tone="danger">
      <p className="state-line">{intl.formatMessage({ id })}</p>
      {correlationId === undefined ? null : (
        <p className="state-line state-line--reference">
          {intl.formatMessage({ id: 'states.retryable.reference' }, { correlationId })}
        </p>
      )}
    </Alert>
  )
}
