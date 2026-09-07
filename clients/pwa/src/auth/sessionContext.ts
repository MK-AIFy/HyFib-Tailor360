import { createContext } from 'react'
import type { ApiError } from './apiClient'
import type { CurrentUser, SignInStep } from './types'

/**
 * The session context: what the application knows about who is signed in, and how it asks them to
 * prove it again without losing what they were doing.
 *
 * A `.ts` module rather than part of the provider component, for the reason every other context in
 * this application is: `react-refresh/only-export-components` cannot refresh a module that exports
 * both a component and a value, and a screen that imports the context should not be importing the
 * provider's implementation with it.
 */

/** Where the application stands with the person in front of it. */
export const SESSION_STATUSES = ['loading', 'anonymous', 'active', 'unavailable'] as const

export type SessionStatus = (typeof SESSION_STATUSES)[number]

/**
 * Why the application is asking somebody to authenticate again, which is what the dialog says and
 * what decides whether it can be dismissed.
 *
 *   expiring  the two-minute warning. The session is still alive; the person can carry on
 *   expired   the session ran out and a request was refused. The request is waiting to be replayed
 *   revoked   the session was ended somewhere else — another device, or an administrator
 *   step-up   the session is fine, but the action about to be taken demands a fresh proof
 */
export const REAUTHENTICATION_REASONS = ['expiring', 'expired', 'revoked', 'step-up'] as const

export type ReauthenticationReason = (typeof REAUTHENTICATION_REASONS)[number]

export interface ReauthenticationRequest {
  readonly reason: ReauthenticationReason
  /**
   * What the person was about to do, as a verb phrase — "post this invoice", "reset this
   * authenticator". Shown in the dialog so that a step-up prompt says what it is protecting rather
   * than appearing out of nowhere. Omitted for an expiry, where the answer is "carry on working".
   */
  readonly action?: string
}

export interface SessionValue {
  readonly status: SessionStatus
  /**
   * What the last authentication step said the account still owes, or null when it owes nothing.
   *
   * It is held here rather than derived from `GET /me`, because `/me` cannot answer the question.
   * The session ticket's `mfaSatisfied` is false on every session a password starts — including one
   * the server itself considered finished, because the account has no second factor or because the
   * device was remembered — so treating it as "has this person finished signing in" would send
   * accounts that had finished back to a challenge they cannot answer.
   *
   * `SignInResponse.step` is the field the server publishes for exactly this, and this is where the
   * client remembers what it said. It is memory, not truth: a hard reload forgets it, and that is
   * honest — the client genuinely does not know any more.
   *
   * What makes forgetting safe is on the server, not here. The session row records the step it still
   * owes, and a session that owes one reaches only the four endpoints that finish the sign-in or end
   * it; everything else answers 403 with `security.sign-in-incomplete` or
   * `security.second-factor-required`. This redirect is therefore a courtesy that sends somebody to
   * the right screen, and never the thing that stops them reaching the wrong one.
   */
  readonly pendingStep: SignInStep | null
  /**
   * The signed-in account, or null.
   *
   * It is kept in memory after a session expires, deliberately: the re-authentication dialog needs
   * the sign-in name to ask for a password rather than starting from nothing, and knowing who was
   * signed in is not knowing anything the person cannot see on their own screen. It is cleared on
   * sign-out.
   */
  readonly user: CurrentUser | null
  /** Why the account could not be read, when the status is `unavailable`. */
  readonly problem: ApiError | null
  /** Re-reads `GET /api/v1/me`. Also what slides the idle deadline when somebody says "keep working". */
  readonly refresh: () => Promise<CurrentUser | null>
  /**
   * Records what an authentication step answered, and re-reads the account.
   *
   * Every path that authenticates calls it — the sign-in screen, the challenge screen, a passkey
   * assertion and the re-authentication dialog — so that the guard has one place to read what is
   * still owed rather than four screens each deciding where to send somebody next.
   */
  readonly recordAuthentication: (step: SignInStep) => Promise<CurrentUser | null>
  /** Ends this session and forgets the account. */
  readonly signOut: () => Promise<void>
  /** Ends every session on the account, including this one, and forgets every remembered device. */
  readonly signOutEverywhere: () => Promise<number>
  /**
   * Asks the person to authenticate again, over whatever is on screen.
   *
   * Resolves true when they did, and false when they abandoned it. The caller decides what that
   * means: the interceptor replays the suspended request, a step-up caller abandons the action, and
   * the expiry warning does nothing at all because the session was still alive.
   */
  readonly reauthenticate: (request: ReauthenticationRequest) => Promise<boolean>
}

/** Null outside a provider, which `useSession` turns into a thrown error rather than a silent nobody. */
export const SessionContext = createContext<SessionValue | null>(null)
