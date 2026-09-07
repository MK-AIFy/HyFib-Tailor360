import { useId } from 'react'
import type { ReactNode } from 'react'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from '../primitives/Icon'
import type { IconName } from '../primitives/icons'
import type { StateHeadingLevel, StateLiveness, StateTone } from './stateVariants'
import './states.css'

export interface StateRegionProps {
  /** The tone chooses a colour pair. The glyph and the words carry the meaning (1.4.1). */
  readonly tone: StateTone
  /** The glyph. Decorative — the heading beside it is what is announced. */
  readonly iconName: IconName
  /** What has happened, as a heading, so the screen stays navigable by heading. */
  readonly title: string
  /** What to do next. An empty state without this is a screen that looks broken (A11Y-88). */
  readonly children?: ReactNode
  /** The controls that answer the state: Try again, Go back, Add the first one. */
  readonly actions?: ReactNode
  /** How assistive technology is told. See `StateLiveness`. */
  readonly live?: StateLiveness
  /**
   * The heading level, which the caller knows and this component does not. Not defaulted for the
   * same reason `Card` does not default one: a guessed level is how a heading list stops being
   * usable (1.3.1, checklist item A11Y-25).
   */
  readonly headingLevel?: StateHeadingLevel
  /** Marks the region busy while something is loading, so its contents are not read as final. */
  readonly busy?: boolean
  /** Fills the width and centres, for a state that owns the whole screen rather than one panel. */
  readonly full?: boolean
  readonly className?: string
}

/**
 * The shape every screen state shares: a glyph, a heading, a sentence, and a way forward.
 *
 * One component behind `EmptyState`, `LoadingState`, `ErrorState` and `Forbidden` because DoD item 7
 * asks for all five states of every screen and section 4.12 of docs/nfr/a11y-checklist.md asks the
 * same questions of each: does it say **what** happened, does it say **what to do next**, and is
 * there a keyboard-reachable way on. Four independent implementations would answer those three
 * questions four different ways, and the fourth one would forget the heading.
 *
 * It is never a toast and never floats: docs/nfr/accessibility-localisation.md section 6 forbids a
 * toast for anything actionable, and a state that describes a whole region belongs in that region.
 */
export function StateRegion({
  tone,
  iconName,
  title,
  children,
  actions,
  live = 'off',
  headingLevel = 2,
  busy = false,
  full = false,
  className,
}: StateRegionProps) {
  const titleId = useId()
  const Heading = `h${String(headingLevel)}` as 'h2'

  // role="status" carries an implicit aria-live="polite"; role="alert" an implicit assertive one.
  // Neither is set for `off`, because a state that was on the screen when it rendered is read in
  // document order like everything else and a live region would announce it a second time.
  const liveAttributes =
    live === 'off'
      ? {}
      : {
          role: live === 'assertive' ? ('alert' as const) : ('status' as const),
          'aria-labelledby': titleId,
        }

  return (
    <div
      {...liveAttributes}
      className={cx('state-region', className)}
      data-tone={tone}
      data-full={full ? 'true' : undefined}
      aria-busy={busy ? true : undefined}
    >
      <Icon className="state-region__icon" name={iconName} />
      <Heading className="state-region__title" id={titleId}>
        {title}
      </Heading>
      {children === undefined ? null : <div className="state-region__body">{children}</div>}
      {actions === undefined ? null : <div className="state-region__actions">{actions}</div>}
    </div>
  )
}
