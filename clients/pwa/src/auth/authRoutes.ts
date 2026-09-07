/**
 * Where the authentication screens live.
 *
 * Constants rather than literals, because six components and the route table all have to agree, and
 * a mistyped path in a `<Navigate>` is a redirect loop rather than a compile error.
 *
 * The signing-in paths are deliberately outside the application shell. A person who cannot sign in
 * must not be shown a navigation bar full of destinations they cannot reach; the account screens,
 * which need a session by definition, are inside it.
 */
export const AUTH_ROUTES = {
  /** The sign-in screen. Public. */
  signIn: '/sign-in',
  /** The second-factor challenge. Needs the session the first factor created, and nothing more. */
  verify: '/sign-in/verify',
  /** Asking for a password reset link. Public — somebody who cannot sign in is who needs it. */
  recovery: '/recovery',
  /** Spending a reset link. Public; the link itself is the credential. */
  recoveryConfirm: '/recovery/confirm',
  /** The account's own security screen: authenticator, recovery codes, passkeys. */
  security: '/account/security',
  /** Setting up an authenticator. */
  authenticator: '/account/security/authenticator',
  /** The session and device inventory. */
  sessions: '/account/sessions',
} as const

/**
 * The query-string parameter the recovery email's link carries the token in.
 *
 * The token is a credential, and it is in the address bar because that is what a link is. It is
 * therefore read once, held in component state, spent, and never written anywhere else — not to
 * storage, not to telemetry, and not to a second URL.
 */
export const RECOVERY_TOKEN_PARAM = 'token'

/**
 * Where to go after signing in when nothing else was asked for.
 *
 * A person's own landing route arrives with #25's preferences; until then the shell's home screen is
 * the answer, and it is deliberately not the last screen they were on: an expired session that
 * resumed on a half-finished form is resumed by the re-authentication dialog, not by a redirect.
 */
export const AFTER_SIGN_IN = '/'

/**
 * Where to go after signing in, taken from the router state the guard set.
 *
 * It accepts a path on this origin and nothing else. `//evil.example` and `https://evil.example` are
 * both rejected, because a redirect target a caller can influence is an open redirect — and a
 * sign-in screen that forwards to another origin after a successful sign-in is the exact shape of a
 * credential-phishing flow. The state is router state rather than a query parameter for the same
 * reason, and because a path in the address bar ends up in browser history and in every log that
 * records a URL.
 */
export function redirectTargetFrom(state: unknown): string {
  if (typeof state !== 'object' || state === null) {
    return AFTER_SIGN_IN
  }
  const from = (state as { readonly from?: unknown }).from
  if (typeof from !== 'string' || !from.startsWith('/') || from.startsWith('//')) {
    return AFTER_SIGN_IN
  }
  return from
}
