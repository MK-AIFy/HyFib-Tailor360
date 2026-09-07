import { NavLink } from 'react-router'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from '../primitives/Icon'
import { NavBadge } from './NavBadge'
import type { NavigationItem } from './navigationItems'
import { useVirtualKeyboardOpen } from './useVirtualKeyboardOpen'
import './BottomNav.css'

export interface BottomNavProps {
  /** The destinations, at most `MAX_BOTTOM_NAV_ITEMS` of them. */
  readonly items: readonly NavigationItem[]
  /** Names the landmark. Defaults to "Main navigation" from the catalogue. */
  readonly label?: string
  /**
   * Overrides the keyboard detection.
   *
   * A shell that already tracks the visual viewport — to size its scroll container, say — passes the
   * answer in rather than making this component run a second observer over the same events. Stories
   * and tests use it to render the hidden state without a keyboard.
   */
  readonly keyboardOpen?: boolean
  readonly className?: string
}

/**
 * The phone navigation bar.
 *
 * Everything about it is a consequence of one sentence in
 * docs/nfr/accessibility-localisation.md section 3: the device is used one-handed, often with a
 * thumb, while the other hand holds a garment.
 *
 *  - **It is at the bottom**, in thumb reach, and the destinations are the whole width of the screen
 *    divided between them, so each target is far larger than the 44 px standard.
 *  - **Every destination has a word under its glyph.** An icon-only bar saves a line of height and
 *    costs a person who has not memorised it every single time.
 *  - **It pads its own safe area**, plus the 8 px gesture-bar clearance that checklist item A11Y-70
 *    measures: nothing may sit in the bottom 8 px of a phone viewport, where the system gesture bar
 *    takes the touch. This is also why `.app-shell` stopped padding the bottom inset — a bar that
 *    reaches the edge of the glass cannot have an ancestor that has already padded the edge away.
 *  - **It gets out of the way of the keyboard.** 2.4.11 Focus Not Obscured is the commonest phone
 *    form failure there is, and `scroll-padding-bottom` alone does not save a screen where the
 *    keyboard has taken half the viewport. While the keyboard is up the bar is `hidden` outright —
 *    removed from the layout and from the accessibility tree, so nothing is announced that cannot be
 *    reached.
 *
 * The current destination is marked by weight, a top rule and a filled glyph — three signals, none
 * of which is colour (1.4.1). `NavLink` supplies the `aria-current="page"` that the rule hangs off.
 */
export function BottomNav({ items, label, keyboardOpen, className }: BottomNavProps) {
  const intl = useIntl()
  const detectedKeyboard = useVirtualKeyboardOpen()
  const hidden = keyboardOpen ?? detectedKeyboard

  return (
    <nav
      className={cx('bottom-nav', className)}
      aria-label={label ?? intl.formatMessage({ id: 'navigation.primary' })}
      hidden={hidden}
    >
      <ul className="bottom-nav__list">
        {items.map((item) => (
          <li key={item.id} className="bottom-nav__item">
            <NavLink className="bottom-nav__link" to={item.href} end={item.end ?? false}>
              <span className="bottom-nav__glyph">
                <Icon name={item.icon} />
                {item.badgeCount === undefined ? null : <NavBadge count={item.badgeCount} />}
              </span>
              <span className="bottom-nav__label">{item.label}</span>
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}
