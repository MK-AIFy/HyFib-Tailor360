import { FormattedMessage, useIntl } from 'react-intl'
import { NavLink, Outlet } from 'react-router'
import { useCurrentUser } from '../../auth/useSession'
import { ADMIN_DESTINATIONS } from '../../admin/adminDestinations'
import './admin.css'

/**
 * The administration section and the way between its screens.
 *
 * ## Why the sub-navigation is filtered and the screens are not
 *
 * A destination whose permission the person does not hold is not shown here, because a list of links
 * that all refuse is indistinguishable from a broken application. The screens behind them still guard
 * themselves with `RequirePermission`, and the server guards itself again: three layers, and only the
 * innermost is the authorisation. Filtering here is courtesy; the other two are the control.
 *
 * ## Why this is a `nav` with its own name
 *
 * The shell already has a main navigation, and 3.2.3 Consistent Navigation is about that one staying
 * put. A second navigation landmark inside the page needs a name of its own or a screen reader offers
 * two identical "navigation" landmarks and no way to tell them apart — which is the most common way a
 * secondary navigation fails an audit.
 */
export function AdminShell() {
  const intl = useIntl()
  const { permissions } = useCurrentUser()

  const destinations = ADMIN_DESTINATIONS.filter((destination) =>
    permissions.includes(destination.permission),
  )

  return (
    <section className="page admin">
      <h1>
        <FormattedMessage id="admin.title" />
      </h1>
      <p className="admin__lede">
        <FormattedMessage id="admin.body" />
      </p>

      {destinations.length === 0 ? null : (
        <nav className="admin__nav" aria-label={intl.formatMessage({ id: 'admin.nav.label' })}>
          <ul className="admin__navList">
            {destinations.map((destination) => (
              <li key={destination.path}>
                <NavLink
                  to={destination.path}
                  end={destination.path === ''}
                  className={({ isActive }) =>
                    isActive ? 'admin__navLink admin__navLink--current' : 'admin__navLink'
                  }
                >
                  <FormattedMessage id={destination.messageId} />
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
      )}

      <Outlet />
    </section>
  )
}
