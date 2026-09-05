import { FormattedMessage } from 'react-intl'
import { Link, createBrowserRouter, isRouteErrorResponse, useRouteError } from 'react-router'
import { App } from '../App'
import { DisplayPreferencesPanel } from '../components/layout/DisplayPreferencesPanel'
import { AboutRoute } from '../routes/AboutRoute'
import { InstallRoute } from '../routes/InstallRoute'

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
 */
export const router = createBrowserRouter([
  {
    path: '/',
    element: <App />,
    errorElement: <RouteErrorBoundary />,
    children: [
      { index: true, element: <HomeRoute /> },
      { path: 'settings/display', element: <DisplaySettingsRoute /> },
      // The install surface. Both are reachable without a session, because the person who needs
      // them most is the one who cannot sign in on a device they have not installed yet.
      { path: 'install', element: <InstallRoute /> },
      { path: 'about', element: <AboutRoute /> },
      // A client-side 404: the server serves the shell for any unknown path. It stays last.
      { path: '*', element: <NotFoundRoute /> },
    ],
  },
])
