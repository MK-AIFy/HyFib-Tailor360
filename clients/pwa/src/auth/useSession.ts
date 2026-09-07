import { useCallback, useContext } from 'react'
import { SessionContext } from './sessionContext'
import type { ReauthenticationRequest, SessionValue } from './sessionContext'
import type { CurrentUser } from './types'

/**
 * The session, from anywhere inside the provider.
 *
 * It throws rather than returning a null session outside the provider. A hook that quietly answered
 * "nobody is signed in" would let a screen render its signed-out state because of a missing provider,
 * which is a bug that looks like a feature until somebody's permissions silently disappear.
 */
export function useSession(): SessionValue {
  const value = useContext(SessionContext)
  if (value === null) {
    throw new Error('useSession must be used inside a SessionProvider.')
  }
  return value
}

/**
 * The signed-in account, for a screen that is already behind `RequireSession` and therefore cannot
 * render without one. It throws for the same reason as above.
 */
export function useCurrentUser(): CurrentUser {
  const { user } = useSession()
  if (user === null) {
    throw new Error('useCurrentUser must be used inside a RequireSession boundary.')
  }
  return user
}

/**
 * Asks for a fresh proof of identity before a sensitive action, and answers whether it was given.
 *
 * This is the hook #25 reuses for its administrative actions — resetting somebody's second factor,
 * changing a permission grant — and it is deliberately the *same* dialog the session-expiry path
 * raises. One re-authentication experience, in one place: a second implementation would be the one
 * that forgets that a person may still owe a second factor.
 *
 * The server is the authority. `PermissionAuthorisationHandler` re-checks step-up freshness on the
 * request itself, so a caller that skipped this hook is refused rather than allowed; asking first is
 * how the person meets the requirement as a question instead of as an error.
 */
export function useStepUp(): (action: string) => Promise<boolean> {
  const { reauthenticate } = useSession()

  return useCallback(
    (action: string) => {
      const request: ReauthenticationRequest = { reason: 'step-up', action }
      return reauthenticate(request)
    },
    [reauthenticate],
  )
}
