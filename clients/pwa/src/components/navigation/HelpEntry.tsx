import { NavLink } from 'react-router'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from '../primitives/Icon'
import './HelpEntry.css'

/**
 * Where in the shell the help entry is being rendered.
 *
 * Only the appearance changes. The name, the glyph, the destination and the position *within* the
 * shell chrome are the same in all three, which is what 3.2.6 asks for: help in the same relative
 * order on every screen, not in the same pixel on every device.
 */
export type HelpEntryPlacement = 'side' | 'bar' | 'inline'

/**
 * The address of the help screen.
 *
 * A constant rather than a prop default so that every shell reaches the same place. The route itself
 * arrives with the application routes; until then this is a destination the navigation knows and the
 * router does not, which a `NavLink` handles without complaint.
 */
export const HELP_HREF = '/help'

/** The address of the support contact screen. */
export const SUPPORT_HREF = '/help/support'

export interface HelpEntryProps {
  readonly placement?: HelpEntryPlacement
  /** Overrides the help destination — a branch with its own written procedure, say. */
  readonly href?: string
  /**
   * Also renders the support contact beside the help link.
   *
   * Section 4.5 and checklist item A11Y-10 name three things that must sit together and stay
   * together: help, the support contact and the "how do I…" link. A screen that shows only two of
   * them on one page and three on another has already broken consistency, so the choice is made once
   * per shell rather than per screen.
   */
  readonly showSupport?: boolean
  readonly className?: string
}

/**
 * The help entry point.
 *
 * WCAG 3.2.6 Consistent Help asks that help occur in the same relative order on every page that has
 * it. Checklist items A11Y-10 and A11Y-85 turn that into two questions a person walking a journey
 * has to be able to answer yes to on every screen: is it here, and is it called the same thing as it
 * was on the last one?
 *
 * The only way to guarantee both is for there to be exactly one component and exactly one string.
 * There is no `label` prop — deliberately: a screen that could rename it could break the criterion
 * without anybody noticing until an audit. The name comes from `navigation.help.label`, and every
 * shell renders this same component in its own reserved slot: the foot of the rail on a desktop, the
 * header on a tablet, the header on a phone (never the bottom bar, whose five places belong to the
 * destinations a Tailor uses forty times a shift).
 */
export function HelpEntry({
  placement = 'inline',
  href = HELP_HREF,
  showSupport = false,
  className,
}: HelpEntryProps) {
  const intl = useIntl()

  return (
    <div className={cx('help-entry', className)} data-placement={placement}>
      <NavLink className="help-entry__link" to={href} data-help-entry="true">
        <Icon name="help" />
        <span className="help-entry__label">
          {intl.formatMessage({ id: 'navigation.help.label' })}
        </span>
      </NavLink>
      {showSupport ? (
        <NavLink className="help-entry__link help-entry__link--support" to={SUPPORT_HREF}>
          <Icon name="phone" />
          <span className="help-entry__label">
            {intl.formatMessage({ id: 'navigation.support.label' })}
          </span>
        </NavLink>
      ) : null}
    </div>
  )
}
