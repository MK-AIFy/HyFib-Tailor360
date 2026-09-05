import { useId } from 'react'
import type { ReactNode } from 'react'
import { NavLink } from 'react-router'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from '../primitives/Icon'
import { NavBadge } from './NavBadge'
import type { NavigationItem, NavigationSection } from './navigationItems'
import './SideNav.css'

function SideNavLink({ item }: { readonly item: NavigationItem }) {
  return (
    <li className="side-nav__item">
      <NavLink className="side-nav__link" to={item.href} end={item.end ?? false}>
        <Icon name={item.icon} />
        <span className="side-nav__label">{item.label}</span>
        {item.badgeCount === undefined ? null : <NavBadge count={item.badgeCount} />}
      </NavLink>
    </li>
  )
}

function SideNavSection({ section }: { readonly section: NavigationSection }) {
  const headingId = useId()

  return (
    <div className="side-nav__section">
      {/*
       * A real heading, not a styled div. A desktop side navigation with six groups is walked by
       * heading far more often than it is tabbed through, and 1.3.1 wants the grouping that is
       * obvious visually to exist in the structure too. The list is named by the heading so a screen
       * reader announces which group it has entered.
       */}
      <h2 className="side-nav__section-title" id={headingId}>
        {section.label}
      </h2>
      <ul className="side-nav__list" aria-labelledby={headingId}>
        {section.items.map((item) => (
          <SideNavLink key={item.id} item={item} />
        ))}
      </ul>
    </div>
  )
}

export interface SideNavProps {
  /** Ungrouped destinations, rendered above any sections. */
  readonly items?: readonly NavigationItem[]
  /** Grouped destinations, each group with a visible heading. */
  readonly sections?: readonly NavigationSection[]
  /** Names the landmark. Defaults to "Main navigation". */
  readonly label?: string
  /**
   * The foot of the rail. This is where `HelpEntry` goes on a desktop, and it is the same slot on
   * every desktop screen — which is the whole of 3.2.6 Consistent Help and checklist items A11Y-10
   * and A11Y-85: same place, same name, every screen.
   */
  readonly footer?: ReactNode
  readonly className?: string
}

/**
 * The desktop navigation rail.
 *
 * A persistent list rather than a menu that opens: the counter and print-station desktops are
 * keyboard-driven, and a navigation that has to be opened before it can be tabbed puts an extra
 * keystroke in front of every move a Cashier makes all day. It stays visible, so 2.4.5 Multiple Ways
 * and 3.2.3 Consistent Navigation are satisfied by the shell rather than by each screen.
 *
 * Rows are `standard` targets even though a desktop has a mouse. The dense 32 px control class of
 * AL-03 is for toolbar icons and table controls, not for the thing somebody clicks forty times a
 * day; and a branch manager on a touchscreen laptop is inside the support matrix.
 */
export function SideNav({ items = [], sections = [], label, footer, className }: SideNavProps) {
  const intl = useIntl()

  return (
    <nav
      className={cx('side-nav', className)}
      aria-label={label ?? intl.formatMessage({ id: 'navigation.primary' })}
    >
      <div className="side-nav__scroll">
        {items.length === 0 ? null : (
          <ul className="side-nav__list">
            {items.map((item) => (
              <SideNavLink key={item.id} item={item} />
            ))}
          </ul>
        )}
        {sections.map((section) => (
          <SideNavSection key={section.id} section={section} />
        ))}
      </div>
      {footer === undefined ? null : <div className="side-nav__footer">{footer}</div>}
    </nav>
  )
}
