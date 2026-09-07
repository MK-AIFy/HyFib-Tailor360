import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { Navigate, Outlet, useLocation } from 'react-router'
import { LoadingState } from '../components/states/LoadingState'
import { RetryableError } from '../components/states/RetryableError'
import { AUTH_ROUTES } from './authRoutes'
import { useSession } from './useSession'

/**
 * The boundary between "signed in" and everything else.
 *
 * It answers four questions, in the order that keeps a person moving:
 *
 *  1. **Is the answer known yet?** While the start-up read of `GET /me` is in flight the screen says
 *     so, rather than flashing the sign-in form at somebody who is already signed in — a flash that
 *     on a slow counter connection is long enough to start typing into.
 *  2. **Could the account be read at all?** A server that is down is not a person who is signed out.
 *     Sending them to a sign-in screen they cannot use would be a lie about what went wrong, so the
 *     screen says what happened and offers to try again.
 *  3. **Is there a session?** If not, the sign-in screen, remembering where they were going.
 *  4. **Does the session still owe something?** The second factor, or enrolling one. Both are
 *     screens, and the guard sends them there rather than letting the application render behind a
 *     half-finished sign-in.
 *
 * What it deliberately does **not** do is decide what the person may see once they are in. That is
 * the permission model, which is #24's, and the fail-closed default until then is that every
 * permission-gated endpoint refuses — which is the right direction for a control that does not exist
 * yet.
 */
export interface RequireSessionProps {
  /** Rendered when there is a session. Defaults to the nested route, so it can be a layout route. */
  readonly children?: ReactNode
}

export function RequireSession({ children }: RequireSessionProps) {
  const intl = useIntl()
  const location = useLocation()
  const { status, pendingStep, refresh } = useSession()

  if (status === 'loading') {
    return (
      <div className="page">
        <LoadingState what={intl.formatMessage({ id: 'auth.guard.loading' })} />
      </div>
    )
  }

  if (status === 'unavailable') {
    return (
      <div className="page">
        <h1>{intl.formatMessage({ id: 'auth.guard.unavailable.title' })}</h1>
        <RetryableError
          action={intl.formatMessage({ id: 'auth.guard.unavailable.action' })}
          onRetry={() => {
            void refresh()
          }}
        />
      </div>
    )
  }

  if (status === 'anonymous') {
    /*
     * Where they were going travels in router state rather than in the address bar. A `?returnTo=`
     * on the sign-in URL is an open redirect waiting to be pointed at another origin, and it puts
     * the path somebody was on into browser history and into any log that records URLs.
     */
    return (
      <Navigate
        replace
        state={{ from: `${location.pathname}${location.search}` }}
        to={AUTH_ROUTES.signIn}
      />
    )
  }

  if (pendingStep === 'multiFactorRequired' && location.pathname !== AUTH_ROUTES.verify) {
    return <Navigate replace to={AUTH_ROUTES.verify} />
  }

  if (
    pendingStep === 'multiFactorEnrolmentRequired' &&
    location.pathname !== AUTH_ROUTES.authenticator
  ) {
    return <Navigate replace to={AUTH_ROUTES.authenticator} />
  }

  return <>{children ?? <Outlet />}</>
}
