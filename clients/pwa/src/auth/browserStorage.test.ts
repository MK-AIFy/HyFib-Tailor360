import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { answerChallenge, signIn } from './authApi'
import { antiforgeryToken, forgetAntiforgeryToken } from './antiforgery'
import { jsonResponse, stubFetch } from './testing/fixtures'
import type { FetchStub } from './testing/fixtures'

/**
 * No security material reaches anywhere script or another tab can read it.
 *
 * The session is an `httpOnly` cookie, so script cannot read it by construction. That is not the
 * risk. The risk is a later change that decides a token would be convenient in `localStorage` —
 * for a "remember me", for a second tab, for an offline queue — and quietly turns a session that
 * dies with the tab into one that survives on a shared counter device until somebody clears their
 * browser. Nothing in the type system stops that, and a reviewer reading a diff that adds one
 * `setItem` will not necessarily see it as a security change.
 *
 * So this asserts the property rather than the implementation: run a real sign-in and a real
 * second-factor challenge through the transport, take the exact strings the server sent, and
 * require that none of them appear in `localStorage`, `sessionStorage` or IndexedDB afterwards.
 * It fails on any future change that stores one, whatever route it takes to get there.
 *
 * Required by the #23 blueprint, which asks for an assertion that these stores hold no token or
 * ticket material after login and MFA, and that the anti-forgery request token is the only value
 * scripts can read.
 */

/** Distinctive strings. If any of these turns up in a store, something leaked it there. */
const SESSION_MATERIAL = {
  antiforgery: 'antiforgery-request-half-9d41c07a',
  sessionId: 'session-id-4f2b8e1c',
  userId: '0199b000-0000-7000-8000-000000000042',
} as const

let transport: FetchStub

/** Everything a script could read out of the two synchronous stores, as one string. */
function readableStorage(): string {
  const entries: string[] = []
  for (const store of [window.localStorage, window.sessionStorage]) {
    for (let index = 0; index < store.length; index += 1) {
      const key = store.key(index)
      if (key !== null) {
        entries.push(key, store.getItem(key) ?? '')
      }
    }
  }
  return entries.join(' ')
}

beforeEach(() => {
  forgetAntiforgeryToken()
  window.localStorage.clear()
  window.sessionStorage.clear()
  transport = stubFetch()

  transport.route('GET /api/v1/antiforgery', () =>
    jsonResponse({ token: SESSION_MATERIAL.antiforgery, headerName: 'X-CSRF-Token' }),
  )
  transport.route('POST /api/v1/auth/login', () =>
    jsonResponse({
      step: 'MultiFactorRequired',
      userId: SESSION_MATERIAL.userId,
      displayName: 'Priya R',
      mustChangePassword: false,
      factors: { authenticator: true, passkey: false, recoveryCode: true },
      session: {
        sessionId: SESSION_MATERIAL.sessionId,
        idleExpiresAt: '2026-09-05T12:30:00Z',
        absoluteExpiresAt: '2026-09-06T00:00:00Z',
      },
    }),
  )
  transport.route('POST /api/v1/auth/mfa/challenge', () =>
    jsonResponse({
      remainingRecoveryCodes: 7,
      shouldReissueRecoveryCodes: false,
      deviceRemembered: false,
    }),
  )
})

afterEach(() => {
  forgetAntiforgeryToken()
  window.localStorage.clear()
  window.sessionStorage.clear()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('security material after signing in', () => {
  it('leaves nothing the server sent in localStorage or sessionStorage', async () => {
    await signIn({ identifier: 'priya', password: 'a-synthetic-password-1234' })
    await answerChallenge({ factor: 'totp', code: '123456', rememberDevice: false })

    const stored = readableStorage()
    for (const [name, secret] of Object.entries(SESSION_MATERIAL)) {
      expect(stored, `${name} reached a browser store`).not.toContain(secret)
    }
  })

  it('opens no IndexedDB database', async () => {
    // Nothing in the authentication path should need one. Asserted rather than assumed, because
    // IndexedDB is where an offline queue would naturally put a pending authenticated request.
    //
    // jsdom ships no IndexedDB at all, so today any use of it would throw and this test would fail
    // that way instead. A recording stub is installed regardless: it makes the assertion say what
    // it means, and it keeps the test meaningful if the environment ever gains a real one.
    const opened: string[] = []
    vi.stubGlobal('indexedDB', {
      open: (name: string) => {
        opened.push(name)
        return { onsuccess: null, onerror: null } as unknown as IDBOpenDBRequest
      },
    })

    await signIn({ identifier: 'priya', password: 'a-synthetic-password-1234' })
    await answerChallenge({ factor: 'totp', code: '123456', rememberDevice: false })

    expect(opened).toEqual([])
  })

  it('holds the anti-forgery request token in memory only', async () => {
    const token = await antiforgeryToken()

    expect(token).toBe(SESSION_MATERIAL.antiforgery)
    // It is the one value script is given, and it still must not outlive the page.
    expect(readableStorage()).not.toContain(token)
  })

  it('forgets the token when the session it belongs to is replaced', async () => {
    await antiforgeryToken()
    await signIn({ identifier: 'priya', password: 'a-synthetic-password-1234' })

    // Signing in replaced the pair, so the next request must fetch a fresh token rather than
    // reuse one bound to the session that has gone.
    const before = transport.callsTo('GET /api/v1/antiforgery').length
    await antiforgeryToken()
    expect(transport.callsTo('GET /api/v1/antiforgery').length).toBe(before + 1)
  })
})
