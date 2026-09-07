import { FormattedMessage, useIntl } from 'react-intl'
import { NavLink } from 'react-router'
import type { ReactNode } from 'react'
import { AUTH_ROUTES } from '../../auth/authRoutes'
import { cx } from '../../design-system/foundations/cx'
import { useVersion } from '../../app/version'
import { HelpEntry } from '../navigation/HelpEntry'
import type { HelpEntryPlacement } from '../navigation/HelpEntry'
import { Icon } from '../primitives/Icon'
import { NetworkStatusBanner } from '../states/NetworkStatusBanner'
import { ShellStatusRegions } from './ShellStatusRegions'

/** The address of the display-preferences screen. One constant, so every shell reaches the same one. */
export const DISPLAY_SETTINGS_HREF = '/settings/display'

export interface ShellUtilitiesProps {
  /** How `HelpEntry` renders itself here — `side` in the desktop rail, `bar` in a header. */
  readonly placement: HelpEntryPlacement
  /** Also render the support contact beside help. The desktop rail has room for it; a phone bar does not. */
  readonly showSupport?: boolean
  readonly className?: string
}

/**
 * Help, support, display settings and the account — the shell entries that are not destinations.
 *
 * They travel together and they sit in one reserved slot per shell: the foot of the rail on a
 * desktop, the header on a tablet and a phone. WCAG 3.2.6 Consistent Help and checklist items
 * A11Y-10 and A11Y-85 ask whether help is in the same relative place with the same name on every
 * screen, and one component rendering one set of strings is the only way to keep that true across
 * three shells.
 *
 * Display settings are here rather than behind the Settings destination on purpose. Only three of
 * the eight roles have a Settings destination at all, and the person who needs 150% text or the
 * high-contrast theme is at least as likely to be a Tailor in a workshop as an Owner at a desk. A
 * preference that some roles cannot reach is a preference that does not exist for them.
 */
export function ShellUtilities({ placement, showSupport = false, className }: ShellUtilitiesProps) {
  const intl = useIntl()

  return (
    <div className={cx('shell-utilities', className)} data-placement={placement}>
      <HelpEntry placement={placement} showSupport={showSupport} />
      <NavLink className="shell-utilities__link" to={DISPLAY_SETTINGS_HREF}>
        <Icon name="settings" />
        <span className="shell-utilities__label">
          {intl.formatMessage({ id: 'layout.settings.display' })}
        </span>
      </NavLink>
      {/*
        The account's own security screen — authenticator, recovery codes, passkeys, devices, and
        signing out. It sits here for the same reason display settings do: only three of the eight
        roles have a Settings destination, and every one of the eight has to be able to sign out of a
        shared counter device and to see where their account is signed in.
      */}
      <NavLink className="shell-utilities__link" to={AUTH_ROUTES.security}>
        <Icon name="users" />
        <span className="shell-utilities__label">
          {intl.formatMessage({ id: 'layout.settings.account' })}
        </span>
      </NavLink>
    </div>
  )
}

export interface ShellHeaderProps {
  readonly children?: ReactNode
  readonly className?: string
}

/**
 * The bar across the top of every shell.
 *
 * It names the shop and nothing else, because the screen's own name is its `h1` inside `main` and a
 * second name in the chrome is noise a screen-reader user steps through on every screen. The
 * utilities slot on the right is what a shell fills in when it has no rail to put them in.
 *
 * It pads its own top safe-area inset. That padding used to sit on the shell, and moving it here is
 * what lets a bottom navigation bar reach the bottom edge of the glass.
 */
export function ShellHeader({ children, className }: ShellHeaderProps) {
  return (
    <header className={cx('app-header', className)}>
      <span className="app-header__shop">
        <FormattedMessage id="app.shopName" />
      </span>
      {children === undefined ? null : <div className="app-header__utilities">{children}</div>}
    </header>
  )
}

export interface ShellMainProps {
  readonly children: ReactNode
  readonly className?: string
}

/**
 * The main landmark, the skip link's destination, and the shell's status regions.
 *
 * `main` begins where the *work* begins — below the banner, the header and the navigation — which is
 * what checklist item A11Y-05 asks: a `main` that starts above the navigation is present, valid and
 * useless. `tabIndex={-1}` makes it a focus target for the skip link without adding a tab stop.
 *
 * The connection banner and the status regions are the first things inside it, in the same place on
 * every screen, so that a scan result, the sync state or a lost connection is where a person already
 * knows to look and where a screen-reader user reaches it immediately after the landmark. Both are
 * mounted whether or not they have anything to say: a live region inserted at the moment its content
 * appears is the commonest reason an announcement is missed.
 */
export function ShellMain({ children, className }: ShellMainProps) {
  return (
    <main className={cx('app-main', className)} id="main-content" tabIndex={-1}>
      <NetworkStatusBanner />
      <ShellStatusRegions />
      {children}
    </main>
  )
}

/**
 * Shows the running build.
 *
 * It is the evidence that the served shell and `GET /api/version` agree, which is the health check
 * the deployment checklist asks for. It pads the bottom safe-area inset and the gesture-bar
 * clearance, because on a desktop and a tablet it is the last thing on the page.
 */
export function ShellFooter({ className }: { readonly className?: string }) {
  const version = useVersion()

  return (
    <footer className={cx('app-footer', className)}>
      {version.status === 'ready' ? (
        // The revision is a development-only member, so the footer has two forms rather than one with
        // an empty half: "Version 0.1.0 · build " reads as a bug to the person being asked to read it
        // down a telephone.
        version.info.commit === undefined ? (
          <FormattedMessage id="footer.version" values={{ version: version.info.current }} />
        ) : (
          <FormattedMessage
            id="footer.versionWithBuild"
            values={{ version: version.info.current, buildHash: version.info.commit }}
          />
        )
      ) : null}
      {version.status === 'error' ? <FormattedMessage id="footer.versionUnavailable" /> : null}
    </footer>
  )
}
