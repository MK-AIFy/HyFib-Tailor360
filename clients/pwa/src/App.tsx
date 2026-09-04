import { FormattedMessage, useIntl } from 'react-intl'
import { NavLink, Outlet } from 'react-router'
import { TrainingBanner } from './components/TrainingBanner'
import { useVersion } from './app/version'

/**
 * The application shell: it is rendered once and stays mounted while routes change.
 *
 * The order of the landmarks is deliberate. The skip link comes first so that a keyboard user reaches
 * the content in one tab; the training banner comes before the header so that it is the first thing
 * announced and the first thing seen; navigation and main content are separate landmarks so that a
 * screen-reader user can jump between them.
 *
 * The role-optimised layouts (bottom bar on a phone, side navigation on a desktop) and the design
 * system arrive with #50; this is the semantic skeleton they will grow into.
 */
export function App() {
  const intl = useIntl()

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        <FormattedMessage id="app.skipToContent" />
      </a>

      <TrainingBanner />

      <header className="app-header">
        <span className="app-header__shop">
          <FormattedMessage id="app.shopName" />
        </span>
      </header>

      <nav className="app-nav" aria-label={intl.formatMessage({ id: 'nav.label' })}>
        <ul className="app-nav__list">
          <li>
            <NavLink className="app-nav__link" to="/" end>
              <FormattedMessage id="nav.home" />
            </NavLink>
          </li>
        </ul>
      </nav>

      {/* tabIndex -1 makes the landmark a valid target for the skip link. */}
      <main className="app-main" id="main-content" tabIndex={-1}>
        <Outlet />
      </main>

      <AppFooter />
    </div>
  )
}

/**
 * Shows the running build. It is the evidence that the served shell and GET /api/version agree, which
 * is the health check the deployment checklist asks for (implementation plan #20).
 */
function AppFooter() {
  const version = useVersion()

  return (
    <footer className="app-footer">
      {version.status === 'ready' ? (
        <FormattedMessage
          id="footer.version"
          values={{ version: version.info.version, buildHash: version.info.buildHash }}
        />
      ) : null}
      {version.status === 'error' ? <FormattedMessage id="footer.versionUnavailable" /> : null}
    </footer>
  )
}
