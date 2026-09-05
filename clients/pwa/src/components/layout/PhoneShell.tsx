import type { ReactNode } from 'react'
import { BottomNav } from '../navigation/BottomNav'
import { PrimaryActionFab } from './PrimaryActionFab'
import { ShellFooter, ShellHeader, ShellMain, ShellUtilities } from './ShellParts'
import type { RoleNavigation } from './roleNavigation'

export interface ShellLayoutProps {
  /** The role's destinations and primary action, from `useRoleNavigation`. */
  readonly navigation: RoleNavigation
  /** True while the on-screen keyboard is covering the viewport. */
  readonly keyboardOpen: boolean
  readonly children: ReactNode
}

/**
 * The phone layout: a bottom navigation bar and one floating primary action.
 *
 * Every decision here comes from one sentence in docs/nfr/accessibility-localisation.md section 3 —
 * the device is used one-handed, often with a thumb, while the other hand holds a garment.
 *
 *  - **Navigation is at the bottom**, in thumb reach, five destinations at most, each with a word
 *    under its glyph.
 *  - **The role's primary action floats above it.** For the four shop-floor roles that action is
 *    Scan, which is the scanner-first phone layout the #50 blueprint asks for.
 *  - **Both disappear while the keyboard is open.** WCAG 2.4.11 Focus Not Obscured is the commonest
 *    phone-form failure there is: the field being typed in ends up behind the bar. `scroll-padding`
 *    alone does not save a screen where the keyboard has taken half the viewport, so while it is up
 *    the bar and the floating action are `hidden` outright — out of the layout and out of the
 *    accessibility tree together, so nothing is announced that cannot be reached.
 *  - **The footer scrolls with the content**, above the bar rather than under it, because a phone
 *    has no room for a second permanent strip and a build number is not worth one.
 *
 * The document order is the visual order: banner, header, work, footer, navigation. Checklist item
 * A11Y-09 walks a screen element by element and asks whether the two agree, and a bottom bar placed
 * first in the DOM and moved down by CSS is the classic way they stop agreeing.
 */
export function PhoneShell({ navigation, keyboardOpen, children }: ShellLayoutProps) {
  const { primaryAction } = navigation

  return (
    <>
      <ShellHeader>
        <ShellUtilities placement="bar" />
      </ShellHeader>

      <div className="app-shell__body">
        <ShellMain>{children}</ShellMain>
      </div>

      <ShellFooter />

      <BottomNav items={navigation.bottomBar} keyboardOpen={keyboardOpen} />

      {primaryAction === null ? null : (
        <PrimaryActionFab
          label={primaryAction.label}
          href={primaryAction.href}
          icon={primaryAction.icon}
          hidden={keyboardOpen}
        />
      )}
    </>
  )
}
