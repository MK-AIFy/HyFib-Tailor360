import { useIntl } from 'react-intl'
import './NavBadge.css'

export interface NavBadgeProps {
  /** How many things want attention. A zero renders nothing at all. */
  readonly count: number
}

/**
 * The count on a navigation destination.
 *
 * A number and a sentence, never a coloured dot. A dot says "something here" only to somebody who
 * can see it and already knows what it means, which makes it a status conveyed by colour alone —
 * forbidden outright by docs/nfr/accessibility-localisation.md section 4.1. So the digit is visible
 * and "{n} items need attention" is rendered for assistive technology, which also gives the
 * destination's accessible name the count without the caller having to build a sentence.
 *
 * Large counts are capped at "99+" visually, but the spoken sentence carries the true number: a
 * Tailor Master glancing at the bar needs to know it is a lot, and a screen reader user needs to know
 * it is a hundred and seven.
 */
export function NavBadge({ count }: NavBadgeProps) {
  const intl = useIntl()

  if (count <= 0) {
    return null
  }

  return (
    <span className="nav-badge">
      <span className="nav-badge__count" aria-hidden="true">
        {count > 99 ? '99+' : count}
      </span>
      <span className="visually-hidden">
        {intl.formatMessage({ id: 'navigation.badge' }, { count })}
      </span>
    </span>
  )
}
