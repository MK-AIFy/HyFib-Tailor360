import { apiRequest } from './apiClient'
import { forgetAntiforgeryToken } from './antiforgery'
import type {
  AuthenticatorEnrolment,
  ChallengeFactor,
  CurrentUser,
  MultiFactorResult,
  PasskeyChallenge,
  PasskeySummary,
  RecoveryCodeSheet,
  RecoveryCompleted,
  SessionDevice,
  SignInResult,
  SignOutEverywhereResult,
} from './types'

/**
 * Every call the authentication screens make, named for what a person is doing rather than for the
 * HTTP verb underneath.
 *
 * Two things are handled here rather than at each call site, because forgetting either is a defect
 * nobody notices until a shared device starts refusing writes:
 *
 *  - **The anti-forgery token is forgotten whenever the account changes.** It is bound to the
 *    signed-in account, so signing in, answering a challenge (which rotates the session), signing out
 *    and signing out everywhere all invalidate it. The interceptor recovers from a stale one anyway,
 *    but recovering from a refusal that was predictable is a round trip spent for nothing.
 *  - **The authentication endpoints never raise the re-authentication dialog.** A refused sign-in
 *    answers 401. Challenging it would open a dialog asking the person to sign in so that they can
 *    sign in, and there is no more reliable way to lock somebody out of an application.
 */

const AUTH = '/api/v1/auth'

/** Answers the first factor and starts a session. */
export async function signIn(input: {
  readonly identifier: string
  readonly password: string
  readonly captchaResponse?: string
}): Promise<SignInResult> {
  const result = await apiRequest<SignInResult>(`${AUTH}/login`, {
    method: 'POST',
    body: {
      identifier: input.identifier,
      password: input.password,
      ...(input.captchaResponse === undefined ? {} : { captchaResponse: input.captchaResponse }),
    },
    challengeOnUnauthenticated: false,
  })
  forgetAntiforgeryToken()
  return result
}

/** Answers a second-factor challenge with an authenticator code or a recovery code. */
export async function answerChallenge(input: {
  readonly factor: ChallengeFactor
  readonly code: string
  readonly rememberDevice: boolean
}): Promise<MultiFactorResult> {
  const result = await apiRequest<MultiFactorResult>(`${AUTH}/mfa/challenge`, {
    method: 'POST',
    body: { factor: input.factor, code: input.code, rememberDevice: input.rememberDevice },
    challengeOnUnauthenticated: false,
  })
  // The challenge replaces the session, so the token pair it was issued under has gone with it.
  forgetAntiforgeryToken()
  return result
}

/** Ends this session. */
export async function signOut(): Promise<void> {
  await apiRequest<void>(`${AUTH}/logout`, {
    method: 'POST',
    challengeOnUnauthenticated: false,
  })
  forgetAntiforgeryToken()
}

/**
 * Ends every session on the account and forgets every remembered device.
 *
 * This is the control a person uses when they think somebody else has their password, which is why
 * it also forgets the devices: ending the sessions while leaving a password-only bypass in place
 * would look like it had worked.
 */
export async function signOutEverywhere(): Promise<SignOutEverywhereResult> {
  const result = await apiRequest<SignOutEverywhereResult>(`${AUTH}/logout-all`, {
    method: 'POST',
    challengeOnUnauthenticated: false,
  })
  forgetAntiforgeryToken()
  return result
}

/**
 * The caller's own account, preferences and session expiry.
 *
 * `challenge` is false for the start-up probe: a 401 there means nobody is signed in, and the answer
 * to that is the sign-in screen rather than a dialog floating over an empty application.
 */
export function currentUser(options: { readonly challenge?: boolean } = {}): Promise<CurrentUser> {
  return apiRequest<CurrentUser>('/api/v1/me', {
    challengeOnUnauthenticated: options.challenge ?? false,
  })
}

/** Starts an authenticator enrolment. The response carries a live secret; it is never stored. */
export function beginAuthenticatorEnrolment(): Promise<AuthenticatorEnrolment> {
  return apiRequest<AuthenticatorEnrolment>(`${AUTH}/mfa/enrol`, { method: 'POST' })
}

/** Confirms an authenticator with a code from it, and returns the recovery codes, once. */
export function confirmAuthenticatorEnrolment(code: string): Promise<RecoveryCodeSheet> {
  return apiRequest<RecoveryCodeSheet>(`${AUTH}/mfa/enrol/confirm`, {
    method: 'POST',
    body: { code },
  })
}

/** Prints a fresh sheet of recovery codes, destroying the previous sheet. */
export function reissueRecoveryCodes(): Promise<RecoveryCodeSheet> {
  return apiRequest<RecoveryCodeSheet>(`${AUTH}/mfa/recovery-codes`, { method: 'POST' })
}

/** Asks for a password recovery link. Answers the same way whether or not the address is known. */
export function requestRecovery(email: string): Promise<void> {
  return apiRequest<void>(`${AUTH}/recovery/request`, {
    method: 'POST',
    body: { email },
    challengeOnUnauthenticated: false,
  })
}

/** Spends a recovery link and sets a new password. */
export function confirmRecovery(input: {
  readonly token: string
  readonly newPassword: string
}): Promise<RecoveryCompleted> {
  return apiRequest<RecoveryCompleted>(`${AUTH}/recovery/confirm`, {
    method: 'POST',
    body: { token: input.token, newPassword: input.newPassword },
    challengeOnUnauthenticated: false,
  })
}

/** Starts registering a passkey and returns the WebAuthn creation options. */
export function beginPasskeyRegistration(): Promise<PasskeyChallenge> {
  return apiRequest<PasskeyChallenge>(`${AUTH}/passkeys/register/options`, { method: 'POST' })
}

/** Finishes registering a passkey. */
export function completePasskeyRegistration(input: {
  readonly ceremonyId: string
  readonly credential: unknown
  readonly label: string
}): Promise<PasskeySummary> {
  return apiRequest<PasskeySummary>(`${AUTH}/passkeys/register`, {
    method: 'POST',
    body: { ceremonyId: input.ceremonyId, credential: input.credential, label: input.label },
  })
}

/** Starts a passkey sign-in and returns the WebAuthn request options. */
export function beginPasskeyAssertion(): Promise<PasskeyChallenge> {
  return apiRequest<PasskeyChallenge>(`${AUTH}/passkeys/assert/options`, {
    method: 'POST',
    challengeOnUnauthenticated: false,
  })
}

/** Signs in with a passkey, which satisfies both factors at once. */
export async function completePasskeyAssertion(input: {
  readonly ceremonyId: string
  readonly credential: unknown
}): Promise<SignInResult> {
  const result = await apiRequest<SignInResult>(`${AUTH}/passkeys/assert`, {
    method: 'POST',
    body: { ceremonyId: input.ceremonyId, credential: input.credential },
    challengeOnUnauthenticated: false,
  })
  forgetAntiforgeryToken()
  return result
}

/** The passkeys registered against the caller's account. */
export function listPasskeys(): Promise<readonly PasskeySummary[]> {
  return apiRequest<readonly PasskeySummary[]>(`${AUTH}/passkeys`)
}

/** Removes one of the caller's passkeys. */
export function removePasskey(passkeyId: string): Promise<void> {
  return apiRequest<void>(`${AUTH}/passkeys/${encodeURIComponent(passkeyId)}`, {
    method: 'DELETE',
  })
}

/** The devices the caller's account is signed in on. */
export function listSessions(): Promise<readonly SessionDevice[]> {
  return apiRequest<readonly SessionDevice[]>('/api/v1/sessions')
}

/** Ends one session on the caller's account. Ending the current one signs this device out. */
export function revokeSession(sessionId: string): Promise<void> {
  return apiRequest<void>(`/api/v1/sessions/${encodeURIComponent(sessionId)}`, {
    method: 'DELETE',
  })
}
