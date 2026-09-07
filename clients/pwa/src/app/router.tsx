import { FormattedMessage } from 'react-intl'
import { Link, createBrowserRouter, isRouteErrorResponse, useRouteError } from 'react-router'
import { App } from '../App'
import { RequireSession } from '../auth/RequireSession'
import { DisplayPreferencesPanel } from '../components/layout/DisplayPreferencesPanel'
import { AboutRoute } from '../routes/AboutRoute'
import { InstallRoute } from '../routes/InstallRoute'
import { AuthShell } from '../routes/auth/AuthShell'
import { AuthenticatorEnrolmentRoute } from '../routes/auth/AuthenticatorEnrolmentRoute'
import { LoginRoute } from '../routes/auth/LoginRoute'
import { MfaChallengeRoute } from '../routes/auth/MfaChallengeRoute'
import { RecoveryConfirmRoute } from '../routes/auth/RecoveryConfirmRoute'
import { RecoveryRequestRoute } from '../routes/auth/RecoveryRequestRoute'
import { SecurityRoute } from '../routes/auth/SecurityRoute'
import { SessionsRoute } from '../routes/auth/SessionsRoute'

/** Placeholder home screen. The role dashboards arrive with #50 and the feature milestones. */
export function HomeRoute() {
  return (
    <section className="page">
      <h1>
        <FormattedMessage id="home.title" />
      </h1>
      <p>
        <FormattedMessage id="home.body" />
      </p>
    </section>
  )
}

/**
 * The display-preferences screen: theme, text size and row density.
 *
 * It lives in the shell rather than behind a role's Settings destination, and the shell's utilities
 * slot links to it from every layout. Only three of the eight journey roles have a Settings
 * destination at all, and the person who needs 150% text or the high-contrast sunlight theme is at
 * least as likely to be a Tailor in a workshop as an Owner at a desk.
 */
export function DisplaySettingsRoute() {
  return (
    <section className="page">
      <h1>
        <FormattedMessage id="layout.settings.display" />
      </h1>
      <p>
        <FormattedMessage id="layout.settings.displayBody" />
      </p>
      <DisplayPreferencesPanel />
    </section>
  )
}

export function NotFoundRoute() {
  return (
    <section className="page">
      <h1>
        <FormattedMessage id="notFound.title" />
      </h1>
      <p>
        <FormattedMessage id="notFound.body" />
      </p>
      <p>
        <Link to="/">
          <FormattedMessage id="notFound.back" />
        </Link>
      </p>
    </section>
  )
}

/**
 * The last line of defence for a render or loader failure. It shows plain language and never the
 * exception text, which can carry identifiers or personal data.
 */
export function RouteErrorBoundary() {
  const error = useRouteError()
  const notFound = isRouteErrorResponse(error) && error.status === 404

  return (
    <section className="page">
      <h1>
        <FormattedMessage id={notFound ? 'notFound.title' : 'error.title'} />
      </h1>
      <p>
        <FormattedMessage id={notFound ? 'notFound.body' : 'error.body'} />
      </p>
      <p>
        <Link to="/">
          <FormattedMessage id="notFound.back" />
        </Link>
      </p>
    </section>
  )
}

/**
 * The data router. Data routers are used from the start (rather than plain <Routes>) because loaders,
 * actions and the pending/error states of later screens depend on them.
 *
 * ## Two frames, and the line between them
 *
 * The signing-in screens are their own branch, outside the application shell. A person who cannot
 * sign in must not be shown a navigation bar of destinations that will refuse them — every link is a
 * dead end, and a screen full of dead ends reads as a broken application. `AuthShell` gives them a
 * main landmark, the product name and the training banner, which is the one piece of chrome that
 * genuinely matters before somebody types a password.
 *
 * Everything inside the shell that is about the shop is behind `RequireSession`. `install` and
 * `about` deliberately are not: the person who most needs the install instructions is the one who
 * cannot yet sign in on a device they have not installed the application on.
 */
export const router = createBrowserRouter([
  {
    element: <AuthShell />,
    errorElement: <RouteErrorBoundary />,
    children: [
      { path: 'sign-in', element: <LoginRoute /> },
      { path: 'sign-in/verify', element: <MfaChallengeRoute /> },
      { path: 'recovery', element: <RecoveryRequestRoute /> },
      { path: 'recovery/confirm', element: <RecoveryConfirmRoute /> },
    ],
  },
  {
    path: '/',
    element: <App />,
    errorElement: <RouteErrorBoundary />,
    children: [
      // Reachable without a session, because the person who needs them most is the one who cannot
      // sign in on a device they have not installed yet.
      { path: 'install', element: <InstallRoute /> },
      { path: 'about', element: <AboutRoute /> },
      // Display settings are deliberately outside the guard as well. Somebody who needs 150% text or
      // the high-contrast sunlight theme needs it *to read the sign-in screen*, and a preference
      // that can only be reached after signing in is a preference they cannot reach at all. Nothing
      // on that screen is personal data: it is a device-local stub until #25 gives it a home on the
      // user record.
      { path: 'settings/display', element: <DisplaySettingsRoute /> },
      {
        element: <RequireSession />,
        children: [
          { index: true, element: <HomeRoute /> },
          { path: 'account/security', element: <SecurityRoute /> },
          { path: 'account/security/authenticator', element: <AuthenticatorEnrolmentRoute /> },
          { path: 'account/sessions', element: <SessionsRoute /> },
        ],
      },
      // A client-side 404: the server serves the shell for any unknown path. It stays last.
      { path: '*', element: <NotFoundRoute /> },
    ],
  },
])
