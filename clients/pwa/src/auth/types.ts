/**
 * The authentication contract, as the client sees it.
 *
 * These types mirror the payloads the Identity module returns from `/api/v1/auth`, `/api/v1/me` and
 * `/api/v1/sessions`. They are hand-written today and are replaced by the generated client when #53
 * publishes the OpenAPI document; until then this file is the single place a server field name
 * appears, so a contract change is one edit rather than a search.
 *
 * ## What is deliberately absent
 *
 * There is no token, ticket, secret or credential type anywhere below, and there never will be. The
 * session is an `httpOnly`, `__Host-`-prefixed cookie that script cannot read, and the only piece of
 * security material this application ever holds in a variable is the anti-forgery request token —
 * which is useless without the cookie half that script also cannot read. `antiforgery.ts` holds that
 * one value, in memory, and nothing else.
 *
 * ## Nulls, not optionals
 *
 * A field the server may answer with `null` is typed `T | null` rather than `T | undefined`. The
 * application compiles with `exactOptionalPropertyTypes`, and JSON `null` is a value: modelling it as
 * an absent property would make every read a lie the type checker cannot catch.
 */

/** What the client must do next after answering the first factor. */
export const SIGN_IN_STEPS = [
  'complete',
  'multiFactorRequired',
  'multiFactorEnrolmentRequired',
] as const

export type SignInStep = (typeof SIGN_IN_STEPS)[number]

/** How far an account has got with its second factor. Mirrors `MfaEnrolmentState`. */
export const MFA_ENROLMENT_STATES = [
  'NotEnrolled',
  'PendingConfirmation',
  'Enrolled',
  'ResetRequired',
] as const

export type MfaEnrolmentState = (typeof MFA_ENROLMENT_STATES)[number]

/** Which second factors the interface may offer. */
export interface FactorAvailability {
  /** A confirmed authenticator is enrolled. */
  readonly authenticator: boolean
  /** At least one unspent recovery code remains. */
  readonly recoveryCode: boolean
  /** At least one passkey is registered. */
  readonly passkey: boolean
}

/**
 * When the current session ends.
 *
 * Both deadlines are ISO-8601 instants from the server, and both matter: a session dies at the idle
 * deadline if nothing uses it, and at the absolute deadline however busy it is. The warning is raised
 * against whichever comes first, which is why the client never assumes the idle one.
 */
export interface SessionExpiry {
  readonly idleExpiresAt: string
  readonly absoluteExpiresAt: string
  /** How long before the deadline to warn. The server owns the number; 120 seconds today. */
  readonly warningLeadSeconds: number
  readonly mfaSatisfied: boolean
}

/** What a sign-in answered. It carries no token — the session travels in a cookie. */
export interface SignInResult {
  readonly step: SignInStep
  readonly userId: string
  readonly displayName: string
  readonly mustChangePassword: boolean
  readonly factors: FactorAvailability
  readonly session: SessionExpiry
}

/** What answering a second-factor challenge returned. */
export interface MultiFactorResult {
  readonly remainingRecoveryCodes: number
  readonly shouldReissueRecoveryCodes: boolean
  readonly deviceRemembered: boolean
  readonly session: SessionExpiry
}

/** What the account still owes and what it can prove. */
export interface AccountSecurity {
  readonly mfaEnrolment: MfaEnrolmentState
  readonly mustChangePassword: boolean
  readonly mfaSatisfied: boolean
  /**
   * When the holder last proved a strong factor. The step-up dialog reads it so that a person is
   * asked before an action is refused, rather than discovering the requirement as a failure.
   */
  readonly lastStrongAuthenticationAt: string | null
  readonly factors: FactorAvailability
  readonly unusedRecoveryCodes: number
}

/** How the holder wants the interface to behave. Applied by the shell once #25 lets them change it. */
export interface AccountPreferences {
  readonly locale: string
  readonly timeZoneId: string
  readonly theme: string
  readonly density: string
  readonly reducedMotion: boolean
  readonly landingRoute: string | null
}

/** Everything the shell needs before it paints. */
export interface CurrentUser {
  readonly userId: string
  readonly userName: string
  readonly displayName: string
  readonly email: string
  readonly status: string
  readonly organisationId: string
  readonly branchId: string | null
  /** Empty until #24 lands the role model, which is the fail-closed direction. */
  readonly permissions: readonly string[]
  readonly security: AccountSecurity
  readonly preferences: AccountPreferences
  readonly session: SessionExpiry
}

/** One row of the holder's own session and device inventory. */
export interface SessionDevice {
  readonly sessionId: string
  readonly deviceLabel: string
  readonly ipAddress: string | null
  readonly createdAt: string
  readonly lastSeenAt: string
  readonly idleExpiresAt: string
  readonly absoluteExpiresAt: string
  readonly mfaSatisfied: boolean
  readonly isCurrent: boolean
}

/**
 * What starting an authenticator enrolment returned.
 *
 * This is a live credential in two forms, and it is the one payload in the application that must not
 * be written anywhere — not to storage, not to a query string, not to a log, not to client telemetry.
 * It lives in component state for as long as the enrolment screen is open and goes when it closes.
 */
export interface AuthenticatorEnrolment {
  readonly otpAuthUri: string
  readonly manualEntryKey: string
  readonly issuer: string
  readonly accountName: string
  readonly digits: number
  readonly periodSeconds: number
}

/** The one and only moment the recovery codes exist in a readable form. */
export interface RecoveryCodeSheet {
  readonly recoveryCodes: readonly string[]
}

/** What completing a password reset changed. */
export interface RecoveryCompleted {
  readonly multiFactorStillRequired: boolean
  readonly sessionsRevoked: number
  readonly mustChangePassword: boolean
}

/** One registered passkey, as its holder sees it. */
export interface PasskeySummary {
  readonly passkeyId: string
  readonly label: string
  readonly createdAt: string
  readonly lastUsedAt: string | null
  readonly isBackedUp: boolean
}

/**
 * A started WebAuthn ceremony.
 *
 * `options` is the W3C options object, passed through verbatim in both directions. It is deliberately
 * typed `unknown`: restating the specification's shape here would create a second definition to keep
 * in step with a specification this application does not own, and `passkeys.ts` is the one place that
 * reads it.
 */
export interface PasskeyChallenge {
  readonly ceremonyId: string
  readonly options: unknown
  readonly expiresAt: string
}

/** How many sessions signing out everywhere ended. */
export interface SignOutEverywhereResult {
  readonly sessionsEnded: number
}

/** Which factor a challenge is answered with. */
export const CHALLENGE_FACTORS = ['totp', 'recoveryCode'] as const

export type ChallengeFactor = (typeof CHALLENGE_FACTORS)[number]
