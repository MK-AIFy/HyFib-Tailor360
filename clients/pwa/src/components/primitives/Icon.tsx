import { cx } from '../../design-system/foundations/cx'
import { ICON_PATHS } from './icons'
import type { IconName } from './icons'
import './Icon.css'

export interface IconProps {
  /** Which glyph. */
  readonly name: IconName
  /**
   * Extra classes. The size comes from `--icon-size` on the parent, so a caller sets the size by
   * styling the control the icon sits in rather than by passing a number here — which is what keeps
   * an icon in step with the text-size preference instead of staying stubbornly 16 px at 150%.
   */
  readonly className?: string
}

/**
 * A decorative glyph.
 *
 * Always `aria-hidden`, with no escape hatch, and that is the point. An icon in this system is the
 * fast channel beside a word, never a substitute for one: `IconButton` requires a `label`,
 * `StatusBadge` renders a status word, and `Alert` renders its tone in text. If a glyph ever needs
 * to carry meaning on its own, the fix is to give the control a name — not to name the picture, which
 * produces the doubled announcement checklist item A11Y-52 is looking for.
 *
 * `focusable="false"` is not redundant: older Edge and some assistive technologies still put an SVG
 * in the tab order without it, which puts a dead tab stop inside every button on the screen.
 */
export function Icon({ name, className }: IconProps) {
  return (
    <svg
      className={cx('icon', className)}
      viewBox="0 0 24 24"
      aria-hidden="true"
      focusable="false"
      role="presentation"
    >
      <path d={ICON_PATHS[name]} />
    </svg>
  )
}
