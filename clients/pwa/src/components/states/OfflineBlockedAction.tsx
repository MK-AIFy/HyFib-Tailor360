import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Alert } from '../primitives/Alert'
import { Button } from '../primitives/Button'
import { useNetworkState } from './useNetworkState'
import './states.css'

export interface OfflineBlockedActionProps {
  /**
   * What was refused, in the person's words: "Taking a payment", "Posting the invoice", "Issuing
   * material". It is read straight into the sentence, so it starts with a capital and reads as the
   * subject of "needs a connection".
   */
  readonly action: string
  /** Anything else worth saying — which part of the journey can still be done offline. */
  readonly children?: ReactNode
  /**
   * Tries the action again. Offered only once the connection is back, because a Try again that
   * cannot work teaches people to press it twice.
   */
  readonly onRetry?: () => void
  readonly retryLabel?: string
  /** Overrides the detected connection, for a story, a test, or a shell that already holds it. */
  readonly online?: boolean
  readonly className?: string
}

/**
 * An action that needs a connection and will not be queued.
 *
 * The title is fixed by plan Section 4.6 and reads **"Needs connection — this will not be queued"**,
 * word for word. That sentence carries the product's most important offline promise: billing,
 * payment and inventory reconciliation are online-only, and nothing in this application silently
 * accepts money into a queue. Checklist item A11Y-OF-02 is the manual instrument for it — does the
 * blocked action say it needs a connection, does it say it will **not** be queued, and does the
 * typed input stay on the screen.
 *
 * The third of those is not something this component can do; it is something it must not undo. It
 * renders beside the form rather than replacing it, and it says so in words, because a person who
 * has just been refused cannot tell from a blank screen whether what they typed survived.
 *
 * It is not `RetryableError`. That one is for a request that was sent and failed; this one is for a
 * request that was never sent, because the device knows it has no connection.
 */
export function OfflineBlockedAction({
  action,
  children,
  onRetry,
  retryLabel,
  online,
  className,
}: OfflineBlockedActionProps) {
  const intl = useIntl()
  const detected = useNetworkState()
  const connected = online ?? detected.online

  const retry =
    onRetry === undefined || !connected ? null : (
      <Button iconName="refresh" onClick={onRetry} variant="primary">
        {retryLabel ?? intl.formatMessage({ id: 'states.blocked.retry' })}
      </Button>
    )

  return (
    <Alert
      actions={retry}
      className={cx(className)}
      live="polite"
      title={intl.formatMessage({ id: 'states.blocked.title' })}
      tone="warning"
    >
      <p className="state-line">{intl.formatMessage({ id: 'states.blocked.body' }, { action })}</p>
      <p className="state-line">{intl.formatMessage({ id: 'states.blocked.inputKept' })}</p>
      {children}
    </Alert>
  )
}
