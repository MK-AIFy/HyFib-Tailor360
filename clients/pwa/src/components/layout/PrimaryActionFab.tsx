import { NavLink } from 'react-router'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from '../primitives/Icon'
import type { IconName } from '../primitives/icons'
import './PrimaryActionFab.css'

export interface PrimaryActionFabProps {
  /** The visible label. Always rendered: see the note on hover-only information below. */
  readonly label: string
  readonly href: string
  readonly icon: IconName
  /** Removed from the layout and the accessibility tree while the virtual keyboard is open. */
  readonly hidden?: boolean
  readonly className?: string
}

/**
 * The phone layout's one floating action.
 *
 * The #50 blueprint calls this the **scanner-first floating action**, and for the four shop-floor
 * roles — Tailor, Tailor Master, Inventory, Delivery — that is exactly what it is: Scan, in the
 * bottom corner, reachable with the thumb of the hand holding the phone while the other hand holds a
 * garment. `roleNavigation.ts` is what decides which action a role gets; Reception gets New order,
 * Measurement Staff gets Capture measurement, a Cashier gets Take payment, and an Owner, whose phone
 * work is reading, gets none at all.
 *
 * Four properties, each of which is a requirement rather than a preference:
 *
 *  - **It carries its label.** An icon-only circle with a tooltip is hover-only information, which
 *    the blueprint forbids outright and which no touch device can show at all. The label is text,
 *    beside the glyph, always.
 *  - **It is a 56 px primary target** with 12 px of clearance, the primary class of
 *    docs/nfr/accessibility-localisation.md section 5.
 *  - **It clears the bottom of the glass.** It sits above the bottom navigation, the safe-area inset
 *    and the 8 px gesture-bar strip that checklist item A11Y-70 measures — nothing may sit in the
 *    bottom 8 px of a phone viewport, where the system gesture takes the touch.
 *  - **It gets out of the way of the keyboard.** While the keyboard is open the field being typed in
 *    and the form's own primary action are what matter, and a floating button over them is precisely
 *    the 2.4.11 failure. `hidden` removes it from the layout and from the accessibility tree
 *    together, so nothing is announced that cannot be reached.
 *
 * It is a link, not a button, because it goes to a screen. A `NavLink` also marks itself current
 * when the person is already on that screen, which stops the Scan action looking available while the
 * scanner is what is already open.
 */
export function PrimaryActionFab({
  label,
  href,
  icon,
  hidden = false,
  className,
}: PrimaryActionFabProps) {
  return (
    <NavLink className={cx('primary-action-fab', className)} to={href} hidden={hidden}>
      <Icon name={icon} className="primary-action-fab__icon" />
      <span className="primary-action-fab__label">{label}</span>
    </NavLink>
  )
}
