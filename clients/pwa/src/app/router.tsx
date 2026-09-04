import { FormattedMessage } from 'react-intl'
import { Link, createBrowserRouter, isRouteErrorResponse, useRouteError } from 'react-router'
import { App } from '../App'

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
      // A client-side 404: the server serves the shell for any unknown path.
      { path: '*', element: <NotFoundRoute /> },
    ],
  },
])
