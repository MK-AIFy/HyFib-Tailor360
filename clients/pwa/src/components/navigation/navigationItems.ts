import type { IconName } from '../primitives/icons'

/**
 * One destination in a navigation set.
 *
 * The same descriptor drives the phone bottom bar, the desktop side navigation and the tablet tab
 * strip, which is the point: 3.2.3 Consistent Navigation and checklist item A11Y-10 ask whether the
 * navigation, the help entry and the support contact are in the same place with the same names as on
 * the previous screen — and the surest way to keep three shells in step is for them to be reading one
 * list. The role-specific lists themselves belong to the layouts that choose them, not here.
 */
export interface NavigationItem {
  /** Stable identifier, used as the React key. */
  readonly id: string
  /**
   * The destination's name, already translated — `navigation.destination.*` in the message
   * catalogue. It is rendered as text in every shell: there are no icon-only destinations, because a
   * glyph a person has not learnt yet is a guess, and a glyph in the dark at the end of a shift is a
   * guess for everybody.
   */
  readonly label: string
  /** Where it goes. */
  readonly href: string
  /** The glyph beside the label. Decorative; the label is what is announced. */
  readonly icon: IconName
  /**
   * Matches the path exactly rather than as a prefix. Needed for the root, which would otherwise be
   * marked current on every screen in the application.
   */
  readonly end?: boolean
  /**
   * A count of things wanting attention — overdue jobs, unread exceptions, queued scans.
   *
   * Rendered as a number *and* a sentence for assistive technology, never as a bare dot: a coloured
   * dot with no text is a status conveyed by colour alone, which section 4.1 forbids outright.
   */
  readonly badgeCount?: number
}

/** A named group of destinations. Only the side navigation renders the group headings. */
export interface NavigationSection {
  readonly id: string
  /** The group's name, already translated. */
  readonly label: string
  readonly items: readonly NavigationItem[]
}

/**
 * How many destinations a phone bottom bar can hold.
 *
 * Five, and it is a hard number rather than a guideline: at 320 CSS px the reflow floor of 1.4.10,
 * six 44 px targets with legible labels do not fit, and shrinking them below the AL-03 standard
 * control size to make them fit is the exact trade docs/nfr/accessibility-localisation.md section 5
 * refuses. A role with more than five destinations puts the rest behind a "More" screen.
 */
export const MAX_BOTTOM_NAV_ITEMS = 5
