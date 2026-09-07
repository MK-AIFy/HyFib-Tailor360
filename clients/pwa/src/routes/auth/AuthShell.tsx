import { Link, Outlet } from 'react-router'
import { FormattedMessage, useIntl } from 'react-intl'
import { DISPLAY_SETTINGS_HREF } from '../../components/layout/ShellParts'
import { RouteAnnouncer } from '../../components/layout/RouteAnnouncer'
import { TrainingBanner } from '../../components/TrainingBanner'
import '../../auth/auth.css'

/**
 * The frame the signing-in screens sit in.
 *
 * It is deliberately not `AppShell`. A person who cannot sign in must not be shown a navigation bar
 * full of destinations that will refuse them: every one of those links is a dead end, and a screen
 * full of dead ends reads as a broken application rather than as a sign-in form.
 *
 * What it does keep from the shell is the training banner, and that is the reason this component
 * exists at all rather than a bare `<main>`. A member of staff must know they are on a training or
 * staging device **before** they type a real password into it, not after. The banner renders only
 * when the server has confirmed a non-production environment, so it can never claim the wrong thing
 * in either direction.
 *
 * There is no skip link, because there is nothing to skip: the first thing in the main landmark is
 * the heading of the only thing on the screen.
 *
 * The one link here is to display settings, and it earns its place: the person who needs 150% text
 * or the high-contrast sunlight theme needs it to read *this* screen, and a preference reachable
 * only after signing in is a preference they cannot reach at all.
 */
export function AuthShell() {
  const intl = useIntl()

  return (
    <div className="auth-shell">
      <RouteAnnouncer />
      <TrainingBanner />
      <header className="auth-shell__header">
        <p className="auth-shell__brand">
          <FormattedMessage id="app.name" />
        </p>
        <Link className="text-link" to={DISPLAY_SETTINGS_HREF}>
          {intl.formatMessage({ id: 'layout.settings.display' })}
        </Link>
      </header>
      <main className="auth-shell__main" id="main-content">
        <Outlet />
      </main>
    </div>
  )
}
