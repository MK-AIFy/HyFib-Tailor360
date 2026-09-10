import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { ApiError, setSessionChallengeHandler } from './apiClient'
import type { SessionChallengeReason } from './apiClient'
import * as api from './authApi'
import { millisecondsUntilWarning } from './expiry'
import { ReauthenticationDialog } from './ReauthenticationDialog'
import { SessionContext } from './sessionContext'
import type { ReauthenticationRequest, SessionStatus, SessionValue } from './sessionContext'
import { SessionExpiryDialog } from './SessionExpiryDialog'
import type { CurrentUser, SignInStep } from './types'

/**
 * Who is signed in, when their session ends, and how they prove it is them again.
 *
 * It owns three things that have to agree with one another and are wrong when they are owned
 * separately:
 *
 *  1. **The account**, read from `GET /api/v1/me` and re-read whenever anything could have changed
 *     it. That endpoint is also what slides the idle deadline, which is why "carry on working" is
 *     implemented as a refresh rather than as a special keep-alive nobody would think to audit.
 *  2. **The warning timer**, scheduled against whichever deadline comes first and rescheduled every
 *     time the account is re-read. A session kept warm all day still dies at the absolute deadline,
 *     and the person is warned before that one too.
 *  3. **The re-authentication dialog**, which the expiry timer, the interceptor and the step-up hook
 *     all raise. One dialog, one at a time: ten requests failing together must not stack ten dialogs,
 *     and a step-up asked for while the session is already expiring is the same question twice.
 *
 * ## Nothing here is written to storage
 *
 * The account is React state. The session is a cookie script cannot read. The anti-forgery token is
 * a variable in `antiforgery.ts`. Reloading the page asks the server again, which is the only source
 * that can answer honestly — a client that remembered "signed in" across a reload would be a client
 * that shows the interface to somebody whose account was suspended an hour ago.
 */
export interface SessionProviderProps {
  readonly children: ReactNode
}

export function SessionProvider({ children }: SessionProviderProps) {
  const [status, setStatus] = useState<SessionStatus>('loading')
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [problem, setProblem] = useState<ApiError | null>(null)
  const [warningOpen, setWarningOpen] = useState(false)
  const [challenge, setChallenge] = useState<ReauthenticationRequest | null>(null)
  const [pendingStep, setPendingStep] = useState<SignInStep | null>(null)
  const [busy, setBusy] = useState(false)

  /*
   * The account, readable from a callback that must not be rebuilt when it changes. The challenge
   * handler is registered with the transport once, and a stale closure there would mean the dialog
   * asking for the password of whoever was signed in when the page loaded.
   *
   * It is written beside every `setUser` rather than synchronised in an effect, so that a request
   * refused in the same tick as the account arrived still finds an account to ask about.
   */
  const userRef = useRef<CurrentUser | null>(null)
  const rememberUser = useCallback((next: CurrentUser | null) => {
    userRef.current = next
    setUser(next)
  }, [])

  /** The promise handed to whoever asked for a re-authentication, and the way to settle it. */
  const pendingRef = useRef<{
    readonly promise: Promise<boolean>
    readonly settle: (signedBackIn: boolean) => void
  } | null>(null)

  const refresh = useCallback(async (): Promise<CurrentUser | null> => {
    try {
      const next = await api.currentUser()
      rememberUser(next)
      setProblem(null)
      setStatus('active')
      return next
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) {
        // Not signed in — or not any more. The account already in hand is deliberately kept: the
        // re-authentication dialog needs a name to ask a password for, and it is a name the person
        // is looking at anyway.
        setStatus('anonymous')
        return null
      }
      setProblem(cause instanceof ApiError ? cause : new ApiError('The account could not be read.'))
      setStatus('unavailable')
      return null
    }
  }, [rememberUser])

  const recordAuthentication = useCallback(
    async (step: SignInStep): Promise<CurrentUser | null> => {
      setPendingStep(step === 'complete' ? null : step)
      return await refresh()
    },
    [refresh],
  )

  /*
   * The start-up read. It is written out here rather than as a call to `refresh` so that the state
   * is only ever set from a promise callback — a synchronous `setState` inside an effect is a
   * cascading render — and so that a provider unmounted while the request is in flight does not set
   * state afterwards.
   */
  useEffect(() => {
    let cancelled = false

    void api
      .currentUser()
      .then((next) => {
        if (!cancelled) {
          rememberUser(next)
          setProblem(null)
          setStatus('active')
        }
      })
      .catch((cause: unknown) => {
        if (cancelled) {
          return
        }
        if (cause instanceof ApiError && cause.status === 401) {
          setStatus('anonymous')
          return
        }
        setProblem(
          cause instanceof ApiError ? cause : new ApiError('The account could not be read.'),
        )
        setStatus('unavailable')
      })

    return () => {
      cancelled = true
    }
  }, [rememberUser])

  const settleChallenge = useCallback((signedBackIn: boolean) => {
    const pending = pendingRef.current
    pendingRef.current = null
    setChallenge(null)
    setWarningOpen(false)
    pending?.settle(signedBackIn)
  }, [])

  const reauthenticate = useCallback((request: ReauthenticationRequest): Promise<boolean> => {
    const existing = pendingRef.current
    if (existing !== null) {
      return existing.promise
    }
    if (userRef.current === null) {
      // Nobody is signed in, so there is nothing to re-authenticate. The route guard sends them to
      // the sign-in screen; raising a dialog over an empty application would be theatre.
      return Promise.resolve(false)
    }

    let settle: (signedBackIn: boolean) => void = () => undefined
    const promise = new Promise<boolean>((resolve) => {
      settle = resolve
    })
    pendingRef.current = { promise, settle }
    setChallenge(request)
    return promise
  }, [])

  /* The transport shares one dialog for session expiry and a server-required step-up. */
  useEffect(() => {
    const handler = (state: SessionChallengeReason) =>
      reauthenticate({
        reason: state === 'step-up' ? 'step-up' : state === 'revoked' ? 'revoked' : 'expired',
      })

    setSessionChallengeHandler(handler)
    return () => {
      setSessionChallengeHandler(null)
      // Whoever was waiting on a dialog that has just gone gets an answer rather than a promise that
      // never settles. False is the honest one: nobody signed back in.
      const pending = pendingRef.current
      pendingRef.current = null
      pending?.settle(false)
    }
  }, [reauthenticate])

  /* The warning, against whichever deadline comes first. Rescheduled on every re-read. */
  useEffect(() => {
    if (status !== 'active' || user === null) {
      return undefined
    }
    const delay = millisecondsUntilWarning(user.session, Date.now())
    if (delay === undefined) {
      return undefined
    }

    const timer = setTimeout(() => {
      setWarningOpen(true)
    }, delay)

    return () => {
      clearTimeout(timer)
    }
  }, [status, user])

  const signOut = useCallback(async (): Promise<void> => {
    try {
      await api.signOut()
    } finally {
      // Whatever the server said, this device is done with the session: a sign-out that failed and
      // left the interface looking signed in is the worst of both answers on a shared counter.
      rememberUser(null)
      setStatus('anonymous')
      setPendingStep(null)
      setWarningOpen(false)
      settleChallenge(false)
    }
  }, [rememberUser, settleChallenge])

  const signOutEverywhere = useCallback(async (): Promise<number> => {
    try {
      const result = await api.signOutEverywhere()
      return result.sessionsEnded
    } finally {
      rememberUser(null)
      setStatus('anonymous')
      setPendingStep(null)
      setWarningOpen(false)
      settleChallenge(false)
    }
  }, [rememberUser, settleChallenge])

  const value = useMemo<SessionValue>(
    () => ({
      status,
      pendingStep,
      user,
      problem,
      refresh,
      recordAuthentication,
      signOut,
      signOutEverywhere,
      reauthenticate,
    }),
    [
      status,
      pendingStep,
      user,
      problem,
      refresh,
      recordAuthentication,
      signOut,
      signOutEverywhere,
      reauthenticate,
    ],
  )

  const keepWorking = () => {
    setBusy(true)
    void refresh()
      .then((next) => {
        setWarningOpen(false)
        if (next === null) {
          // The session had already gone by the time they answered. Same screen, next question.
          void reauthenticate({ reason: 'expired' })
        }
      })
      .finally(() => {
        setBusy(false)
      })
  }

  return (
    <SessionContext.Provider value={value}>
      {children}

      {user !== null && warningOpen && challenge === null ? (
        <SessionExpiryDialog
          busy={busy}
          expiry={user.session}
          onExpired={() => {
            setWarningOpen(false)
            void reauthenticate({ reason: 'expired' })
          }}
          onKeepWorking={keepWorking}
          onSignOut={() => {
            setBusy(true)
            void signOut().finally(() => {
              setBusy(false)
            })
          }}
        />
      ) : null}

      {user !== null && challenge !== null ? (
        <ReauthenticationDialog
          onAuthenticated={recordAuthentication}
          onResolve={settleChallenge}
          request={challenge}
          user={user}
        />
      ) : null}
    </SessionContext.Provider>
  )
}
