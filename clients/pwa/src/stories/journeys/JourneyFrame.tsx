import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { DisplayPreferencesProvider } from '../../app/DisplayPreferencesProvider'
import { createInMemoryDisplayPreferencesStore } from '../../app/preferences'
import { AppShell } from '../../components/layout/AppShell'
import type { NavigationBadges } from '../../components/layout/roleNavigation'
import type { JourneyRole, ShellKind } from '../../design-system/foundations/types'
import './journeys.css'

export interface JourneyFrameProps {
  /** Whose navigation the shell draws. One of the eight `A11Y-RJ-01`..`08` roles. */
  readonly role: JourneyRole
  /** Which layout to force. Stories only; the application always measures. */
  readonly shellKind: ShellKind
  /** The frame's width in CSS pixels — one of the widths the overflow helper asserts at. */
  readonly width: '320' | '360' | '768' | '1024' | '1280'
  /** The address the shell thinks it is at, so the right destination is marked current. */
  readonly route?: string
  readonly badges?: NavigationBadges
  readonly children: ReactNode
}

/**
 * The frame every reference journey is rendered in.
 *
 * ## What it is for
 *
 * A journey is not a component: it is a person, a device and a screen, and the interesting failures
 * live in the join. A queue that reads perfectly on its own has still failed if its last row sits
 * under the bottom bar, and a wizard that validates correctly has still failed if the error summary
 * appears behind the on-screen keyboard. So each journey renders inside the real `AppShell`, at one
 * of the widths `expectNoHorizontalOverflow` asserts at, with the navigation the role actually has.
 *
 * ## Why the width is fixed and the shell is forced
 *
 * `AppShell` chooses its layout by measuring its own container, which is exactly right in the
 * application and useless in a Storybook frame that is as wide as the browser. `shellKind` and a
 * fixed frame width together pin the story to one device class, so a reviewer can put the phone and
 * the desktop journeys side by side and so #52's visual baselines do not move with the window.
 * Nothing in the application passes either.
 *
 * ## What it deliberately does not provide
 *
 * A router with real routes, a query client, or a session. There is no backend for orders,
 * customers or billing — those arrive with #23 onward — so every journey holds its own state in
 * `useState` over the fixtures. The point of the evidence is the interaction and the announcement,
 * not the fetch.
 */
export function JourneyFrame({
  role,
  shellKind,
  width,
  route = '/',
  badges,
  children,
}: JourneyFrameProps) {
  return (
    <MemoryRouter initialEntries={[route]}>
      <DisplayPreferencesProvider store={createInMemoryDisplayPreferencesStore()}>
        <div className="journey" data-width={width}>
          <div className="journey__scroll">
            <AppShell badges={badges ?? {}} role={role} shellKind={shellKind}>
              {children}
            </AppShell>
          </div>
        </div>
      </DisplayPreferencesProvider>
    </MemoryRouter>
  )
}
