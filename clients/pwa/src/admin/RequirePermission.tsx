import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { EmptyState } from '../components/states/EmptyState'
import { useCurrentUser } from '../auth/useSession'

/**
 * Renders its children only for a caller who holds the permission, and says so plainly otherwise.
 *
 * ## This is not the authorisation
 *
 * The server is the authority and re-checks every request; a caller who reached the screen another
 * way is refused there. What this does is stop the application showing somebody a screen of controls
 * that will all fail — which reads as a broken application rather than as a boundary, and which is
 * the same reasoning that keeps the sign-in screens outside the shell's navigation.
 *
 * So it is deliberately generous about what it does not know: the permission list comes from
 * `GET /me` and can be a few minutes old, and this component's answer to "no" is a sentence and a
 * suggestion, never a redirect. A redirect on a stale claim would bounce somebody out of a screen
 * they are entitled to.
 */
export function RequirePermission({
  permission,
  children,
}: {
  readonly permission: string
  readonly children: ReactNode
}) {
  const intl = useIntl()
  const { permissions } = useCurrentUser()

  if (!permissions.includes(permission)) {
    return (
      <EmptyState
        iconName="alert-circle"
        title={intl.formatMessage({ id: 'admin.forbidden.title' })}
        live="polite"
        full
      >
        {intl.formatMessage({ id: 'admin.forbidden.body' })}
      </EmptyState>
    )
  }

  return <>{children}</>
}
