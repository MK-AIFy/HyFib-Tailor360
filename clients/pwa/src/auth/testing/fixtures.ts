import { vi } from 'vitest'
import type { Mock } from 'vitest'
import type {
  AuthenticatorEnrolment,
  CurrentUser,
  PasskeySummary,
  SessionDevice,
  SessionExpiry,
  SignInResult,
} from '../types'

/**
 * Synthetic data and a stub transport for the authentication tests.
 *
 * Every value here is invented. No real name, address or code appears in any fixture in this
 * repository, and an authentication fixture is the last place one should: a test that used a real
 * recovery code would be a test that committed one.
 */

/** One request the stub saw, so a test can assert what was sent as well as what came back. */
export interface RecordedCall {
  readonly method: string
  readonly path: string
  readonly headers: Headers
  readonly body: unknown
}

export type Responder = (call: RecordedCall) => Response | Promise<Response>

export interface FetchStub {
  /** Every call, in order. */
  readonly calls: readonly RecordedCall[]
  /** Answers `"POST /api/v1/auth/login"` with this responder. A later route replaces an earlier one. */
  route: (signature: string, responder: Responder) => void
  /** The mock itself, for assertions about how many times something was asked for. */
  readonly fetch: Mock
  /** Calls matching a signature. */
  callsTo: (signature: string) => readonly RecordedCall[]
}

/** A JSON body. */
export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** An RFC 9457 problem, in the shape the API actually sends. */
export function problemResponse(
  status: number,
  code: string,
  extra: Readonly<Record<string, unknown>> = {},
  headers: Readonly<Record<string, string>> = {},
): Response {
  return new Response(
    JSON.stringify({
      type: `urn:tailor360:problem:${code}`,
      title: 'A failure',
      status,
      detail: 'Something the server said.',
      code,
      correlationId: '0199aa11-2233-4455-6677-8899aabbccdd',
      ...extra,
    }),
    {
      status,
      headers: { 'Content-Type': 'application/problem+json', ...headers },
    },
  )
}

/** A 204. */
export function noContent(): Response {
  return new Response(null, { status: 204 })
}

/**
 * Installs a `fetch` stub that answers by `"METHOD /path"`.
 *
 * The anti-forgery endpoint is answered by default, because every write in the application fetches
 * a token first and a test that had to remember would be a test that failed for the wrong reason.
 */
export function stubFetch(): FetchStub {
  const routes = new Map<string, Responder>()
  const calls: RecordedCall[] = []

  routes.set('GET /api/v1/antiforgery', () =>
    jsonResponse({ token: 'test-request-token', headerName: 'X-CSRF-Token' }),
  )

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const method = (init?.method ?? 'GET').toUpperCase()
    const headers = new Headers(init?.headers)
    const raw = init?.body
    const body: unknown = typeof raw === 'string' ? JSON.parse(raw) : undefined
    const call: RecordedCall = { method, path, headers, body }
    calls.push(call)

    const responder = routes.get(`${method} ${path}`)
    if (responder === undefined) {
      return problemResponse(404, 'test.route-not-stubbed')
    }
    return await responder(call)
  })

  vi.stubGlobal('fetch', fetchMock)

  return {
    calls,
    fetch: fetchMock,
    route(signature, responder) {
      routes.set(signature, responder)
    },
    callsTo(signature) {
      const [method, path] = signature.split(' ')
      return calls.filter((call) => call.method === method && call.path === path)
    },
  }
}

/** A session that has fifteen minutes left and warns two minutes before it ends. */
export function aSessionExpiry(overrides: Partial<SessionExpiry> = {}): SessionExpiry {
  const now = Date.now()
  return {
    idleExpiresAt: new Date(now + 15 * 60 * 1000).toISOString(),
    absoluteExpiresAt: new Date(now + 11 * 60 * 60 * 1000).toISOString(),
    warningLeadSeconds: 120,
    mfaSatisfied: true,
    ...overrides,
  }
}

/** An ordinary signed-in account. */
export function aCurrentUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    userId: '0199aa00-0000-7000-8000-000000000001',
    userName: 'asha.counter',
    displayName: 'Asha (counter)',
    email: 'asha.counter@example.invalid',
    status: 'Active',
    organisationId: '0199aa00-0000-7000-8000-0000000000ff',
    branchId: '0199aa00-0000-7000-8000-0000000000aa',
    permissions: [],
    security: {
      mfaEnrolment: 'Enrolled',
      mustChangePassword: false,
      mfaSatisfied: true,
      lastStrongAuthenticationAt: new Date().toISOString(),
      factors: { authenticator: true, recoveryCode: true, passkey: false },
      unusedRecoveryCodes: 8,
    },
    preferences: {
      locale: 'en-IN',
      timeZoneId: 'Asia/Kolkata',
      theme: 'System',
      density: 'Comfortable',
      reducedMotion: false,
      landingRoute: null,
    },
    session: aSessionExpiry(),
    ...overrides,
  }
}

/** What a sign-in answered. */
export function aSignInResult(overrides: Partial<SignInResult> = {}): SignInResult {
  return {
    step: 'complete',
    userId: '0199aa00-0000-7000-8000-000000000001',
    displayName: 'Asha (counter)',
    mustChangePassword: false,
    factors: { authenticator: true, recoveryCode: true, passkey: false },
    session: aSessionExpiry(),
    ...overrides,
  }
}

/** One row of the device inventory. */
export function aSessionDevice(overrides: Partial<SessionDevice> = {}): SessionDevice {
  const now = Date.now()
  return {
    sessionId: '0199aa00-0000-7000-8000-000000000010',
    deviceLabel: 'Counter tablet',
    ipAddress: '198.51.100.24',
    createdAt: new Date(now - 60 * 60 * 1000).toISOString(),
    lastSeenAt: new Date(now - 5 * 60 * 1000).toISOString(),
    idleExpiresAt: new Date(now + 25 * 60 * 1000).toISOString(),
    absoluteExpiresAt: new Date(now + 10 * 60 * 60 * 1000).toISOString(),
    mfaSatisfied: true,
    isCurrent: false,
    ...overrides,
  }
}

/** One registered passkey. */
export function aPasskey(overrides: Partial<PasskeySummary> = {}): PasskeySummary {
  return {
    passkeyId: '0199aa00-0000-7000-8000-000000000020',
    label: 'Workshop laptop',
    createdAt: new Date('2026-08-01T09:00:00.000Z').toISOString(),
    lastUsedAt: null,
    isBackedUp: false,
    ...overrides,
  }
}

/**
 * A started authenticator enrolment.
 *
 * The secret is a made-up base32 string of the right shape. It is not a working key for anything.
 */
export function anEnrolment(
  overrides: Partial<AuthenticatorEnrolment> = {},
): AuthenticatorEnrolment {
  const secret = 'MFZW IZTB ONSG C3TH MFZW IZTB'
  return {
    otpAuthUri:
      'otpauth://totp/HyFib%20Tailor360:asha.counter?secret=' +
      secret.replace(/ /g, '') +
      '&issuer=HyFib%20Tailor360&digits=6&period=30',
    manualEntryKey: secret,
    issuer: 'HyFib Tailor360',
    accountName: 'asha.counter',
    digits: 6,
    periodSeconds: 30,
    ...overrides,
  }
}
