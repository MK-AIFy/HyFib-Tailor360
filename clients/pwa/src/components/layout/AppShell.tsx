import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { FormattedMessage } from 'react-intl'
import { setCssVariable } from '../../design-system/foundations/setCssVariable'
import type { JourneyRole, ShellKind } from '../../design-system/foundations/types'
import { TrainingBanner } from '../TrainingBanner'
import { DesktopShell } from './DesktopShell'
import { PhoneShell } from './PhoneShell'
import { RouteAnnouncer } from './RouteAnnouncer'
import { ShellStatusProvider } from './ShellStatusProvider'
import { TabletShell } from './TabletShell'
import { useRoleNavigation } from './roleNavigation'
import type { NavigationBadges } from './roleNavigation'
import { useShellKind } from './useShellKind'
import { useVirtualKeyboard } from './useVirtualKeyboard'
import './AppShell.css'

/**
 * The role the shell assumes until there is a session to ask.
 *
 * Authentication and the permission model arrive with #23 and #24; there is no identity yet and this
 * issue must not invent one. Reception is the widest of the counter roles, so a shell built on it
 * shows the most navigation and hides the least — which is the failure mode to prefer while the real
 * answer is missing.
 */
export const DEFAULT_JOURNEY_ROLE: JourneyRole = 'reception'

export interface AppShellProps {
  readonly children: ReactNode
  /** Whose navigation to draw. Comes from the session once #23 and #24 land. */
  readonly role?: JourneyRole
  /** Counts to show beside destinations — overdue jobs, queued scans, unread exceptions. */
  readonly badges?: NavigationBadges
  /**
   * Forces a layout. For stories, tests and the design review only.
   *
   * It must never be wired to a user-agent string. The whole point of measuring is that a desktop
   * window dragged to 400 px gets the phone shell, which is what 1.4.10 Reflow asks for.
   */
  readonly shellKind?: ShellKind
}

/**
 * The application shell: rendered once, stays mounted while routes change, and decides which of the
 * three layouts the screen is in.
 *
 * ## How the layout is chosen
 *
 * By measuring the shell's own width, never by asking what the device is
 * (docs/nfr/support-matrix.md section 5). Under 768 px is the phone shell, under 1024 px the tablet,
 * and 1024 px and up the desktop. A CSS container query would be the natural tool and does the
 * styling, but it cannot choose *structure*: a bottom bar and a desktop rail are different elements,
 * and rendering both and hiding one puts two "Main navigation" landmarks in the accessibility tree —
 * exactly what checklist item A11Y-06 fails a screen for.
 *
 * ## What it writes on the document, and why it has to
 *
 * Two attributes and one custom property go onto `<html>` rather than onto the shell element:
 *
 *   `data-shell`     which layout is active. `html` needs it because `scroll-padding-bottom` is a
 *                    property of the scrolling element, and its value is the bottom bar's height —
 *                    which is zero when there is no bottom bar.
 *   `data-keyboard`  whether the on-screen keyboard is up, which also zeroes the bottom-bar height,
 *                    because the bar has been hidden.
 *   `--virtual-keyboard-height`  how much the keyboard covers, in pixels. It is the one genuinely
 *                    dynamic number here, so it goes through `setCssVariable` — the CSSOM, which the
 *                    Content Security Policy does not restrict, and never a `style` attribute in
 *                    JSX. It exists for iOS Safari, which honours neither
 *                    `interactive-widget=resizes-content` nor a layout-viewport resize, and where
 *                    without it the focused field stays under the keyboard (2.4.11).
 *
 * Together with `scroll-padding-bottom` in global.css and the `hidden` bottom bar, those are the
 * three mechanics the #50 blueprint names for Focus Not Obscured, and all three are needed: the
 * viewport meta for browsers that resize, the scroll padding for the bar, and the measured keyboard
 * height for the browser that does neither.
 */
export function AppShell({
  children,
  role = DEFAULT_JOURNEY_ROLE,
  badges = {},
  shellKind: shellKindOverride,
}: AppShellProps) {
  const rootRef = useRef<HTMLDivElement>(null)
  const shellKind = useShellKind(rootRef, shellKindOverride)
  const keyboard = useVirtualKeyboard()
  const navigation = useRoleNavigation(role, badges)

  useEffect(() => {
    const root = document.documentElement
    root.setAttribute('data-shell', shellKind)
    return () => {
      root.removeAttribute('data-shell')
    }
  }, [shellKind])

  useEffect(() => {
    const root = document.documentElement
    if (keyboard.open) {
      root.setAttribute('data-keyboard', 'open')
      setCssVariable(root, '--virtual-keyboard-height', `${String(keyboard.height)}px`)
    } else {
      root.removeAttribute('data-keyboard')
      setCssVariable(root, '--virtual-keyboard-height', null)
    }
    return () => {
      root.removeAttribute('data-keyboard')
      setCssVariable(root, '--virtual-keyboard-height', null)
    }
  }, [keyboard.open, keyboard.height])

  const layoutProps = {
    navigation,
    keyboardOpen: keyboard.open,
    children,
  }

  return (
    <ShellStatusProvider>
      <div className="app-shell" data-shell={shellKind} ref={rootRef}>
        {/* First in the DOM so it is the first thing Tab reaches on every screen (2.4.1, A11Y-20). */}
        <a className="skip-link" href="#main-content">
          <FormattedMessage id="app.skipToContent" />
        </a>

        <RouteAnnouncer />

        {/* Before the header, so it is the first thing announced and the first thing seen. */}
        <TrainingBanner />

        {shellKind === 'phone' ? <PhoneShell {...layoutProps} /> : null}
        {shellKind === 'tablet' ? <TabletShell {...layoutProps} /> : null}
        {shellKind === 'desktop' ? <DesktopShell {...layoutProps} /> : null}
      </div>
    </ShellStatusProvider>
  )
}
