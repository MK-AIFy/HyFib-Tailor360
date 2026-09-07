import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { StateRegion } from './StateRegion'
import type { StateHeadingLevel, StateLiveness } from './stateVariants'

export interface LoadingStateProps {
  /**
   * What is being loaded, in words: "the delivery queue", "this customer's measurements".
   *
   * Required, and required in the caller's words rather than defaulted to "Loading…". Checklist
   * item A11Y-44 wants the busy state announced when it starts, and "loading" on its own tells a
   * person who cannot see the screen nothing about which part of it is busy.
   */
  readonly what: string
  readonly live?: StateLiveness
  readonly headingLevel?: StateHeadingLevel
  readonly full?: boolean
  readonly className?: string
}

/**
 * Something is on its way.
 *
 * Two rules make this more than a spinner. The region carries `aria-busy`, so its contents are not
 * read as final while it is waiting; and the glyph's rotation is decoration only — under
 * `prefers-reduced-motion: reduce` it stops, and the sentence is what remains, which is exactly what
 * checklist item A11Y-73 asks: with motion off, nothing is lost.
 *
 * The outcome is announced by whatever replaces this — the loaded content, an `EmptyState` or an
 * `ErrorState`, each of which announces politely. That pairing is the second half of A11Y-44, and it
 * is why this component has no "done" state of its own to get out of step with the screen.
 */
export function LoadingState({
  what,
  live = 'polite',
  headingLevel = 2,
  full = false,
  className,
}: LoadingStateProps) {
  const intl = useIntl()

  return (
    <StateRegion
      busy
      className={cx('state-region--loading', className)}
      full={full}
      headingLevel={headingLevel}
      iconName="refresh"
      live={live}
      title={intl.formatMessage({ id: 'states.loading.label' }, { what })}
      tone="neutral"
    />
  )
}
