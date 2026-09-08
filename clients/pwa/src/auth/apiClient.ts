import type { ProblemDetails } from '../components/states/problemDetails'
import { CLIENT_VERSION, CLIENT_VERSION_HEADER } from '../app/clientVersion'
import { ANTIFORGERY_HEADER, antiforgeryToken, forgetAntiforgeryToken } from './antiforgery'

/**
 * The one way this application talks to its API.
 *
 * It is a real interceptor rather than a wrapper around `fetch`, because three things have to happen
 * on every request and none of them can be left to a call site:
 *
 *  1. **The anti-forgery header.** Every state-changing request carries the request half of the token
 *     pair. A screen that forgets it gets a 403 that looks like a permission problem.
 *  2. **A refused token is refetched once, and the request is replayed.** The token is bound to the
 *     signed-in account, so it stops being valid the moment somebody signs in or out — which happens
 *     between two ordinary requests often enough that treating it as an error would be wrong.
 *  3. **An expired session is re-authenticated in place, and the request is replayed.** This is the
 *     one that matters most on a shop floor. A session that dies while a measurement wizard is half
 *     filled in must not become a redirect to a sign-in screen: the typed input would be gone, and
 *     3.3.7 Redundant Entry is not the only reason — the person is standing in front of a customer
 *     with a tape measure. So a 401 suspends the request, raises the re-authentication dialog over
 *     whatever is on screen, and when the person has signed back in the *same* request is sent again.
 *     The screen never unmounts and never learns that any of it happened.
 *
 * ## What it deliberately does not do
 *
 * It does not cache, it does not de-duplicate and it does not queue. Server state belongs to the
 * shared query cache #50 introduces, and the bounded offline queue is #51's; a second data-fetching
 * path built here would be the thing both of those have to unpick. This is the transport, and the
 * three rules above are transport concerns.
 *
 * ## The three headers it adds without being asked
 *
 * `X-Correlation-Id` on every request, so "it did not work" becomes a line a technical reviewer can
 * find. `X-Client-Version` on every request, so a build the server no longer supports is told so once
 * rather than failing one field at a time. And `Idempotency-Key` on a command that asks for one — see
 * `ApiRequestOptions.idempotent` for why the key is the caller's to hold rather than this module's to
 * generate.
 */

/** The RFC 9457 body this API answers failures with, including the conventions section 4.3 members. */
export interface ApiProblem extends ProblemDetails {
  /** The stable, branchable code — `identity.invalid-credentials`, `security.cross-site-request`. */
  readonly code?: string
  /** Whether repeating the request could succeed without anything else changing. */
  readonly retryable?: boolean
  /** How long to wait, when a throttle refused the request. */
  readonly retryAfterSeconds?: number
  /** On a 426, the oldest client build the server answers. */
  readonly minimumClient?: string
  /** On a 426, the build the server is serving, which is the one to collect. */
  readonly current?: string
}

/**
 * A request that did not succeed.
 *
 * It carries the parsed problem details and nothing else. The `message` is for a developer reading a
 * stack in a test; **it is never rendered**, because the sentence a person sees is chosen by the
 * screen from the problem's `code`. That separation is what stops a server-side exception message
 * reaching a counter screen (docs/nfr/accessibility-localisation.md section 8.2).
 */
export class ApiError extends Error {
  /** The HTTP status, or undefined when the request never got an answer at all. */
  readonly status: number | undefined

  /** The parsed problem details, when the server sent a body that could be read as one. */
  readonly problem: ApiProblem | undefined

  /** What the session authentication handler said about the cookie that was presented. */
  readonly sessionState: SessionState | undefined

  constructor(message: string, options: ApiErrorOptions = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = options.status
    this.problem = options.problem
    this.sessionState = options.sessionState
  }

  /** The stable problem code, when the server sent one. */
  get code(): string | undefined {
    return this.problem?.code
  }
}

/** What the constructor accepts. A separate interface, so the class has no parameter properties. */
export interface ApiErrorOptions {
  readonly status?: number
  readonly problem?: ApiProblem
  readonly sessionState?: SessionState
}

/**
 * Why a presented session cookie was not accepted, from the `X-Session-State` response header.
 *
 * It exists so the client can tell a session that ended from one that never existed, and say the
 * right thing: "you were signed out on another device" reads very differently from "your session
 * timed out", and only one of them is a reason to check the device inventory.
 */
export const SESSION_STATES = ['revoked', 'expired', 'unknown'] as const

export type SessionState = (typeof SESSION_STATES)[number]

/** The response header the session scheme names a refusal in. */
export const SESSION_STATE_HEADER = 'X-Session-State'

/** The problem code the server returns when the anti-forgery token is missing or stale. */
export const ANTIFORGERY_REFUSED_CODE = 'security.antiforgery-token-invalid'

/**
 * The problem code the server returns when this build is older than the minimum it supports.
 *
 * It is never retried and never turned into a field error: the only thing that fixes it is collecting
 * the current build, so a screen that sees it shows the update prompt. The body carries `minimumClient`
 * and `current`, which is what the prompt is written from.
 */
export const UPGRADE_REQUIRED_CODE = 'client.upgrade-required'

/** The header a command carries the caller's retry key in. */
export const IDEMPOTENCY_HEADER = 'Idempotency-Key'

/** The header an edit carries the version it is being made against in. */
export const IF_MATCH_HEADER = 'If-Match'

/** The header a read carries the version a later edit must present back. */
export const ETAG_HEADER = 'ETag'

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS'])

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export interface ApiRequestOptions {
  readonly method?: HttpMethod
  /** Serialised as JSON. Omit for a request with no body. */
  readonly body?: unknown
  readonly signal?: AbortSignal
  /**
   * Whether a 401 raises the in-place re-authentication dialog and replays the request.
   *
   * True for everything a signed-in person does. **False for the authentication endpoints
   * themselves** — a refused sign-in answers 401, and challenging it would ask the person to sign in
   * so that they can sign in — and false for the start-up probe of `GET /me`, where a 401 is the
   * ordinary answer for somebody who has not signed in yet and the right response is the sign-in
   * screen, not a dialog over an empty page.
   */
  readonly challengeOnUnauthenticated?: boolean
  /**
   * The retry key for a command, sent as `Idempotency-Key`.
   *
   * **The caller holds it, this module does not generate it.** A key generated here would be fresh on
   * every call, which is the one thing it must not be: the guarantee is that a retry of the *same*
   * command — the same payment, the same confirmation — reuses the key it first used, so the server
   * replays the original outcome instead of doing the thing twice. A screen therefore mints the key
   * when the person commits to the action and holds it for as long as it may retry, including across
   * an in-place re-authentication, which is exactly what the replay below preserves.
   */
  readonly idempotencyKey?: string
  /**
   * The version this edit is being made against, sent as `If-Match`.
   *
   * Every administrative edit is gated on one: the server compares it against the version the row
   * carries now and refuses with 409 when somebody else got there first. The screen holds the value
   * it was given by the read it rendered, which is what makes the refusal mean "the thing you are
   * looking at has changed" rather than "try again".
   */
  readonly ifMatch?: string
}

/**
 * How the application re-authenticates in place.
 *
 * Registered by `SessionProvider`. It resolves true when the person has signed back in and the
 * suspended request should be replayed, and false when they abandoned the dialog — in which case the
 * original 401 is thrown and the screen shows it.
 */
export type SessionChallengeHandler = (state: SessionState) => Promise<boolean>

let challengeHandler: SessionChallengeHandler | null = null

/** One challenge at a time. Ten requests failing together must not raise ten dialogs. */
let challengeInFlight: Promise<boolean> | null = null

/**
 * Registers the handler. Passing null removes it, which is what the provider does on unmount.
 *
 * Any challenge still in flight is abandoned at the same time. It belonged to the handler being
 * replaced and can never be answered now — the dialog it was waiting on has gone with the provider —
 * and leaving it in place would make every later 401 wait on a promise that never settles, which
 * presents as an application that has quietly stopped saving anything.
 */
export function setSessionChallengeHandler(handler: SessionChallengeHandler | null): void {
  challengeHandler = handler
  challengeInFlight = null
}

async function runSessionChallenge(state: SessionState): Promise<boolean> {
  if (challengeHandler === null) {
    return false
  }

  challengeInFlight ??= challengeHandler(state)

  try {
    return await challengeInFlight
  } finally {
    challengeInFlight = null
  }
}

/**
 * A correlation identifier for this request, so that "it did not work" becomes a line a technical
 * reviewer can find. It is a random value and identifies nothing about the person.
 */
function newCorrelationId(): string | undefined {
  // `globalThis.crypto` is declared non-optional by the DOM types and is genuinely absent in some
  // environments, so the capability is tested rather than trusted.
  const source: Crypto | undefined = globalThis.crypto
  if (source !== undefined && typeof source.randomUUID === 'function') {
    return source.randomUUID()
  }
  return undefined
}

function readSessionState(response: Response): SessionState | undefined {
  const raw = response.headers.get(SESSION_STATE_HEADER)
  return (SESSION_STATES as readonly string[]).includes(raw ?? '')
    ? (raw as SessionState)
    : undefined
}

async function readProblem(response: Response): Promise<ApiProblem | undefined> {
  const contentType = response.headers.get('Content-Type') ?? ''
  if (!contentType.includes('json')) {
    return undefined
  }
  try {
    const payload: unknown = await response.json()
    // Every member of `ApiProblem` is optional, so any object is structurally one. That is the
    // honest model of an RFC 9457 body: the server may send any subset, and every read of it below
    // checks for what it needs rather than assuming the shape arrived intact.
    return typeof payload === 'object' && payload !== null ? payload : undefined
  } catch {
    // A truncated or non-JSON body is not a second failure to report: the status is the answer, and
    // the screen has a plain-language sentence for every status already.
    return undefined
  }
}

async function readBody<T>(response: Response): Promise<T> {
  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as T
  }
  const contentType = response.headers.get('Content-Type') ?? ''
  if (!contentType.includes('json')) {
    return undefined as T
  }
  return (await response.json()) as T
}

async function send(path: string, options: ApiRequestOptions): Promise<Response> {
  const method = options.method ?? 'GET'
  const headers = new Headers({ Accept: 'application/json' })

  const correlationId = newCorrelationId()
  if (correlationId !== undefined) {
    headers.set('X-Correlation-Id', correlationId)
  }

  if (CLIENT_VERSION !== undefined) {
    headers.set(CLIENT_VERSION_HEADER, CLIENT_VERSION)
  }

  if (options.idempotencyKey !== undefined) {
    headers.set(IDEMPOTENCY_HEADER, options.idempotencyKey)
  }

  if (options.ifMatch !== undefined) {
    headers.set(IF_MATCH_HEADER, options.ifMatch)
  }

  if (options.body !== undefined) {
    headers.set('Content-Type', 'application/json')
  }

  if (!SAFE_METHODS.has(method)) {
    headers.set(ANTIFORGERY_HEADER, await antiforgeryToken())
  }

  return fetch(path, {
    method,
    headers,
    credentials: 'same-origin',
    // Nothing this client fetches may be served from a cache: every authenticated response is either
    // personal data or a security fact about the moment it was asked for.
    cache: 'no-store',
    ...(options.body === undefined ? {} : { body: JSON.stringify(options.body) }),
    ...(options.signal === undefined ? {} : { signal: options.signal }),
  })
}

/**
 * Sends a request, applying the three interceptor rules, and returns the parsed body.
 *
 * Throws `ApiError` for anything that did not succeed, including a connection that never landed —
 * for which `status` is undefined and the screen shows the `network` cause.
 */
export async function apiRequest<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  return await readBody<T>(await exchange(path, options))
}

/**
 * A body and the version it was read at, for the screens that edit something.
 *
 * @typeParam T The response body.
 */
export interface VersionedResponse<T> {
  readonly value: T
  /**
   * The `ETag` the server sent, or undefined when it sent none.
   *
   * Undefined is a real answer rather than a fault: a flag that has never been configured has no
   * version to edit against, and the server deliberately omits the header rather than inventing one.
   * A screen that treats undefined as an error refuses the only request that can create it.
   */
  readonly version: string | undefined
}

/**
 * Sends a request and returns the body together with its `ETag`.
 *
 * The same interceptor, not a second transport: an administrative screen has to hold the version its
 * next edit will present in `If-Match`, and a bare `apiRequest` throws that header away. Everything
 * else about the request — the anti-forgery pair, the correlation identifier, the replay on a stale
 * token and the in-place re-authentication — is identical, because it is the same code path.
 */
export async function apiRequestVersioned<T>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<VersionedResponse<T>> {
  const response = await exchange(path, options)

  return {
    value: await readBody<T>(response),
    version: response.headers.get(ETAG_HEADER) ?? undefined,
  }
}

async function exchange(path: string, options: ApiRequestOptions): Promise<Response> {
  let tokenRefreshed = false
  let reauthenticated = false

  for (;;) {
    let response: Response
    try {
      response = await send(path, options)
    } catch (cause) {
      if (cause instanceof DOMException && cause.name === 'AbortError') {
        throw cause
      }
      throw new ApiError(`${options.method ?? 'GET'} ${path} did not reach the server.`)
    }

    if (response.ok) {
      return response
    }

    const problem = await readProblem(response)
    const sessionState = readSessionState(response)

    // Rule 2: a refused anti-forgery token is refetched once and the request replayed. Only once:
    // a second refusal is a real failure, and looping would hammer an endpoint that is telling us no.
    if (response.status === 403 && problem?.code === ANTIFORGERY_REFUSED_CODE && !tokenRefreshed) {
      tokenRefreshed = true
      forgetAntiforgeryToken()
      continue
    }

    // Rule 3: an expired or revoked session is re-authenticated in place and the request replayed.
    if (
      response.status === 401 &&
      (options.challengeOnUnauthenticated ?? true) &&
      !reauthenticated
    ) {
      reauthenticated = true
      // Signing in issues a new pair, so the token in hand is stale by construction.
      const signedBackIn = await runSessionChallenge(sessionState ?? 'expired')
      if (signedBackIn) {
        forgetAntiforgeryToken()
        continue
      }
    }

    throw new ApiError(`${options.method ?? 'GET'} ${path} returned ${String(response.status)}.`, {
      status: response.status,
      ...(problem === undefined ? {} : { problem }),
      ...(sessionState === undefined ? {} : { sessionState }),
    })
  }
}
