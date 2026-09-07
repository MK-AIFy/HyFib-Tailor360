import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { StateRegion } from './StateRegion'
import type { StateHeadingLevel, StateLiveness } from './stateVariants'
import type { IconName } from '../primitives/icons'

export interface EmptyStateProps {
  /** What is empty, as a heading. Defaults to "Nothing here yet". */
  readonly title?: string
  /**
   * What to do next. Checklist item A11Y-88 is precise about this: an empty state that says only
   * "no results" reads exactly like a screen that has not finished loading, and an empty state is
   * where a screen-reader journey usually stops. Say what would put something here.
   */
  readonly children?: ReactNode
  /** The control that answers it — "Add the first customer", "Clear the filters". */
  readonly actions?: ReactNode
  readonly iconName?: IconName
  /**
   * Defaults to `polite`, because an empty state almost always replaces a loading state and is
   * therefore an update the person has not been told about. Pass `off` for a region that is empty
   * from the first paint and is read in document order anyway.
   */
  readonly live?: StateLiveness
  readonly headingLevel?: StateHeadingLevel
  readonly full?: boolean
  readonly className?: string
}

/**
 * Nothing to show, and nothing wrong.
 *
 * The distinction from `ErrorState` is the whole point of having both. An empty queue and a queue
 * that failed to load look identical on a shop floor — both are a blank rectangle — and the person
 * standing at the counter has to know whether to wait, to act, or to fetch somebody. So an empty
 * state names itself, says what would put something in it, and offers the control that does.
 */
export function EmptyState({
  title,
  children,
  actions,
  iconName = 'list',
  live = 'polite',
  headingLevel = 2,
  full = false,
  className,
}: EmptyStateProps) {
  const intl = useIntl()

  return (
    <StateRegion
      actions={actions}
      className={cx(className)}
      full={full}
      headingLevel={headingLevel}
      iconName={iconName}
      live={live}
      title={title ?? intl.formatMessage({ id: 'states.empty.title' })}
      tone="neutral"
    >
      {children ?? intl.formatMessage({ id: 'states.empty.body' })}
    </StateRegion>
  )
}
