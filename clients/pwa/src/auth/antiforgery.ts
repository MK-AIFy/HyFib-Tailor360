/**
 * The anti-forgery request token: the only piece of security material script is ever given.
 *
 * The server issues a pair. The cookie half — `__Host-t360.csrf`, `httpOnly` — the browser holds and
 * script cannot read. The request half comes back in the body of `GET /api/v1/antiforgery` and has to
 * be sent in the `X-CSRF-Token` header on every state-changing request. Neither half is useful alone,
 * which is what makes it safe for script to hold one of them.
 *
 * ## Why it lives in a module variable and nowhere else
 *
 * `localStorage` would outlive the pair it belongs to. The token is bound to the signed-in account,
 * so signing in, signing out and switching account all invalidate the one in hand; a stored copy
 * survives all three and survives the tab being closed, which turns "refused for no visible reason"
 * into the normal experience on a shared counter device. A module variable dies with the page, which
 * is exactly the lifetime the token has.
 *
 * Everything else about the session — the session cookie itself — is never in script's reach at all.
 * This module is the whole of the client's security-material surface, and it is one string.
 */

/** Where the request half is fetched from. */
import { CLIENT_VERSION, CLIENT_VERSION_HEADER } from '../app/clientVersion'

export const ANTIFORGERY_ENDPOINT = '/api/v1/antiforgery'

/** The header the request token is sent in. Matches `AntiforgeryDefaults.HeaderName`. */
export const ANTIFORGERY_HEADER = 'X-CSRF-Token'

interface AntiforgeryTokenResponse {
  readonly token: string
  readonly headerName: string
}

/** In memory, for the life of this page. Never written to storage. */
let cached: string | null = null

/** One request in flight at a time, so a burst of writes does not fetch a token each. */
let inFlight: Promise<string> | null = null

function isTokenResponse(payload: unknown): payload is AntiforgeryTokenResponse {
  if (typeof payload !== 'object' || payload === null) {
    return false
  }
  const candidate = payload as Record<string, unknown>
  return typeof candidate['token'] === 'string' && candidate['token'].length > 0
}

async function requestToken(): Promise<string> {
  const response = await fetch(ANTIFORGERY_ENDPOINT, {
    method: 'GET',
    // This request does not go through the interceptor — the interceptor is what calls it — so the
    // build has to be declared here too. A request that omitted it would be the one request a server
    // too new for this client would answer, and the answer would be a token the next request cannot
    // spend.
    headers: {
      Accept: 'application/json',
      ...(CLIENT_VERSION === undefined ? {} : { [CLIENT_VERSION_HEADER]: CLIENT_VERSION }),
    },
    // Same-origin: there is one origin, because the API is a backend-for-frontend behind the same
    // reverse proxy. A cross-origin credentialed request would need CORS the server does not grant.
    credentials: 'same-origin',
    cache: 'no-store',
  })

  if (!response.ok) {
    throw new Error(`GET ${ANTIFORGERY_ENDPOINT} returned ${String(response.status)}`)
  }

  const payload: unknown = await response.json()
  if (!isTokenResponse(payload)) {
    throw new Error(`GET ${ANTIFORGERY_ENDPOINT} returned an unexpected payload`)
  }

  return payload.token
}

/**
 * The current request token, fetching one if there is none.
 *
 * Concurrent callers share the one request: a screen that saves two things at once must not race two
 * token fetches, because the second response replaces the cookie the first token was paired with.
 */
export async function antiforgeryToken(): Promise<string> {
  if (cached !== null) {
    return cached
  }

  inFlight ??= requestToken()

  try {
    const token = await inFlight
    cached = token
    return token
  } finally {
    inFlight = null
  }
}

/**
 * Forgets the token held in memory.
 *
 * Called on three occasions, all of which invalidate the pair: after signing in, after signing out,
 * and when the server refuses a request with `security.antiforgery-token-invalid`. The next write
 * fetches a fresh pair.
 */
export function forgetAntiforgeryToken(): void {
  cached = null
  inFlight = null
}

/** Whether a token is currently held. Exists for tests; the value itself is never exported. */
export function hasAntiforgeryToken(): boolean {
  return cached !== null
}
