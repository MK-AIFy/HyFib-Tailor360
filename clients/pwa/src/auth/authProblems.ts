import { ApiError } from './apiClient'
import type { MessageKey } from '../i18n/en-IN'

/**
 * Turning an authentication failure into a sentence, and refusing to turn it into any other one.
 *
 * The server answers a wrong password, an unknown account, a suspended account, an account that
 * never completed its invitation and a locked-out account with one byte-identical body:
 * `identity.invalid-credentials`. It goes to some trouble to do that — down to verifying the
 * submitted password against a decoy hash so that the unknown-account path costs the same time — and
 * a client that added "no account with that name" would hand back the enumeration oracle for free.
 *
 * So this mapping is deliberately small, and every entry in it is a failure the person can do
 * something about. Anything not listed falls through to the plain-language sentences in
 * `components/states/problemDetails.ts`, which describe the *class* of failure — the connection, the
 * server, the rate limit — and never the specifics.
 *
 * Nothing here ever renders the server's `detail`. That is not distrust of this server: it is the
 * rule from docs/nfr/accessibility-localisation.md section 8.2, which exists because a misconfigured
 * environment is the one that puts an exception message on a counter screen.
 */

/** A message to render, with whatever the sentence needs to say it. */
export interface AuthProblemMessage {
  readonly id: MessageKey
  readonly values?: Readonly<Record<string, string | number>>
}

/** Problem codes this application says something specific about. */
const CODE_MESSAGES: Readonly<Record<string, MessageKey>> = {
  'identity.invalid-credentials': 'auth.problem.invalidCredentials',
  'identity.captcha-required': 'auth.problem.captchaRequired',
  'identity.mfa-code-invalid': 'auth.problem.codeInvalid',
  'identity.recovery-code-invalid': 'auth.problem.codeInvalid',
  'identity.totp-code-replayed': 'auth.problem.codeInvalid',
  'identity.recovery-token-invalid': 'auth.problem.recoveryTokenInvalid',
  'identity.session-required': 'auth.problem.sessionRequired',
  'identity.session-not-active': 'auth.problem.sessionRequired',
  'identity.passkeys-unavailable': 'auth.problem.passkeysUnavailable',
  'identity.passkey-verification-failed': 'auth.problem.passkeyRefused',
  'identity.passkey-ceremony-not-valid': 'auth.problem.passkeyRefused',
  'identity.passkey-response-not-readable': 'auth.problem.passkeyRefused',
  'identity.passkey-counter-went-backwards': 'auth.problem.passkeyRefused',
  'identity.passkey-already-registered': 'auth.problem.passkeyRefused',
  'identity.mfa-already-enrolled': 'auth.problem.mfaAlreadyEnrolled',
  'identity.last-factor-cannot-be-removed': 'auth.problem.lastFactor',

  // The three refusals that mean "you have not proved enough yet", which are recoverable by the
  // person in front of the screen and so are worth a sentence rather than a generic failure. The
  // server distinguishes them: the first two come from the route's own assurance requirement, the
  // third from the handler once it knows the account already holds a factor.
  'security.second-factor-required': 'auth.problem.secondFactorRequired',
  'security.step-up-required': 'admin.stepUp.body',
  'identity.second-factor-not-satisfied': 'auth.problem.secondFactorRequired',
  'security.sign-in-incomplete': 'auth.problem.signInIncomplete',
  'security.antiforgery-token-invalid': 'auth.problem.securityRefused',
  'security.cross-site-request': 'auth.problem.securityRefused',
}

/** The code the throttle refuses with, which carries a wait the sentence has to name. */
const TOO_MANY_ATTEMPTS = 'identity.too-many-attempts'

/**
 * The sentence for a failure, or undefined when there is nothing specific to say and the screen
 * should fall back to the plain-language description of the failure class.
 */
export function authProblemMessage(error: unknown): AuthProblemMessage | undefined {
  if (!(error instanceof ApiError)) {
    return undefined
  }

  const code = error.code
  if (code === undefined) {
    return undefined
  }

  if (code === TOO_MANY_ATTEMPTS) {
    // The server always sends the wait, in the body and in `Retry-After`. Sixty seconds is the
    // fallback only for a body that arrived without it, and it is deliberately an over-estimate: a
    // person told to wait too long tries again and succeeds, which is the harmless direction.
    const seconds = error.problem?.retryAfterSeconds ?? 60
    return { id: 'auth.problem.tooManyAttempts', values: { seconds } }
  }

  const id = CODE_MESSAGES[code]
  return id === undefined ? undefined : { id }
}

/**
 * Whether a failure is a validation problem carrying per-field messages — which the password policy
 * failures on the recovery screen are, and which belong in the field and in the error summary rather
 * than in a banner at the top of the screen.
 */
export function hasFieldErrors(error: unknown): error is ApiError {
  return (
    error instanceof ApiError &&
    error.problem?.errors !== undefined &&
    Object.keys(error.problem.errors).length > 0
  )
}
