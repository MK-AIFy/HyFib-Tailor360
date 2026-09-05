import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Button } from '../primitives/Button'
import { StateRegion } from './StateRegion'
import type { StateHeadingLevel, StateLiveness } from './stateVariants'

export interface ErrorStateProps {
  /** What could not be shown. Defaults to "This could not be shown". */
  readonly title?: string
  /** What it means and what to do. Defaults to the catalogue's plain-language sentence. */
  readonly children?: ReactNode
  /**
   * Loads the region again. Distinct from `RetryableError.onRetry`, which resends a command with
   * the same `Idempotency-Key`: this one repeats a read, which is safe by definition.
   */
  readonly onRetry?: () => void
  readonly retryLabel?: string
  /** Anything else the screen offers instead of, or beside, the retry. */
  readonly actions?: ReactNode
  readonly live?: StateLiveness
  readonly headingLevel?: StateHeadingLevel
  readonly full?: boolean
  readonly className?: string
}

/**
 * A region that could not be shown.
 *
 * This is the read-side failure: a queue, a dashboard tile or a customer record that did not load.
 * The write-side failure — a command that was sent and did not land — is `RetryableError`, and the
 * two are separate components because the sentence a person needs is different. A failed read can
 * always be repeated; a failed write can only be repeated safely when the same `Idempotency-Key`
 * goes with it, and saying "try again" without that guarantee is how a payment gets taken twice.
 *
 * It announces politely by default rather than assertively. An assertive region interrupts whatever
 * a screen reader is saying, and this product reserves that for the two cases section 6 of
 * docs/nfr/accessibility-localisation.md names: a rejected scan, and a failure that stopped the
 * person mid-task. A tile that did not load is neither.
 */
export function ErrorState({
  title,
  children,
  onRetry,
  retryLabel,
  actions,
  live = 'polite',
  headingLevel = 2,
  full = false,
  className,
}: ErrorStateProps) {
  const intl = useIntl()

  const retry =
    onRetry === undefined ? null : (
      <Button iconName="refresh" onClick={onRetry} variant="secondary">
        {retryLabel ?? intl.formatMessage({ id: 'states.error.retry' })}
      </Button>
    )

  return (
    <StateRegion
      actions={
        retry === null && actions === undefined ? undefined : (
          <>
            {retry}
            {actions}
          </>
        )
      }
      className={cx(className)}
      full={full}
      headingLevel={headingLevel}
      iconName="alert-circle"
      live={live}
      title={title ?? intl.formatMessage({ id: 'states.error.title' })}
      tone="danger"
    >
      {children ?? intl.formatMessage({ id: 'states.error.body' })}
    </StateRegion>
  )
}
