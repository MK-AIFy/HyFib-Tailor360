import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { forgetAntiforgeryToken } from './antiforgery'
import { ApiError, apiRequest, setSessionChallengeHandler } from './apiClient'
import { jsonResponse, noContent, problemResponse, stubFetch } from './testing/fixtures'
import type { FetchStub } from './testing/fixtures'

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
})

describe('what every request carries', () => {
  it('sends the anti-forgery header on a write and not on a read', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse({ ok: true }))
    transport.route('POST /api/v1/auth/logout', () => noContent())

    await apiRequest('/api/v1/me')
    await apiRequest('/api/v1/auth/logout', { method: 'POST' })

    const read = transport.callsTo('GET /api/v1/me')[0]
    const write = transport.callsTo('POST /api/v1/auth/logout')[0]
    expect(read?.headers.get('X-CSRF-Token')).toBeNull()
    expect(write?.headers.get('X-CSRF-Token')).toBe('test-request-token')
  })

  it('fetches the token once however many writes follow', async () => {
    transport.route('POST /api/v1/auth/logout', () => noContent())

    await apiRequest('/api/v1/auth/logout', { method: 'POST' })
    await apiRequest('/api/v1/auth/logout', { method: 'POST' })

    expect(transport.callsTo('GET /api/v1/antiforgery')).toHaveLength(1)
  })

  it('never sends a credential in a query string', async () => {
    transport.route('POST /api/v1/auth/login', () => jsonResponse({ step: 'complete' }))

    await apiRequest('/api/v1/auth/login', {
      method: 'POST',
      body: { identifier: 'asha.counter', password: 'not-a-real-password' },
    })

    for (const call of transport.calls) {
      expect(call.path).not.toContain('password')
      expect(call.path).not.toContain('?')
    }
  })

  it('asks for nothing to be cached, because every answer is personal or momentary', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse({ ok: true }))

    await apiRequest('/api/v1/me')

    const init = transport.fetch.mock.calls[0]?.[1] as RequestInit | undefined
    expect(init?.cache).toBe('no-store')
    expect(init?.credentials).toBe('same-origin')
  })
})

describe('a refused anti-forgery token', () => {
  it('is refetched once and the request is replayed', async () => {
    let attempts = 0
    transport.route('POST /api/v1/auth/logout', () => {
      attempts += 1
      return attempts === 1
        ? problemResponse(403, 'security.antiforgery-token-invalid')
        : noContent()
    })

    await apiRequest('/api/v1/auth/logout', { method: 'POST' })

    expect(attempts).toBe(2)
    expect(transport.callsTo('GET /api/v1/antiforgery')).toHaveLength(2)
  })

  it('is not retried a second time, because a server saying no twice means it', async () => {
    transport.route('POST /api/v1/auth/logout', () =>
      problemResponse(403, 'security.antiforgery-token-invalid'),
    )

    await expect(apiRequest('/api/v1/auth/logout', { method: 'POST' })).rejects.toMatchObject({
      status: 403,
    })
    expect(transport.callsTo('POST /api/v1/auth/logout')).toHaveLength(2)
  })

  it('leaves an ordinary 403 alone', async () => {
    transport.route('POST /api/v1/auth/logout', () =>
      problemResponse(403, 'security.cross-site-request'),
    )

    await expect(apiRequest('/api/v1/auth/logout', { method: 'POST' })).rejects.toMatchObject({
      code: 'security.cross-site-request',
    })
    expect(transport.callsTo('POST /api/v1/auth/logout')).toHaveLength(1)
  })
})

describe('a session that has ended mid-request', () => {
  it('re-authenticates in place and replays the identical request', async () => {
    let attempts = 0
    transport.route('POST /api/v1/orders', (call) => {
      attempts += 1
      return attempts === 1
        ? problemResponse(401, 'identity.session-required', {}, { 'X-Session-State': 'expired' })
        : jsonResponse({ received: call.body })
    })

    const seen: string[] = []
    setSessionChallengeHandler((state) => {
      seen.push(state)
      return Promise.resolve(true)
    })

    const body = { waist: 82, chest: 96 }
    const result = await apiRequest<{ received: unknown }>('/api/v1/orders', {
      method: 'POST',
      body,
    })

    // The measurement the person typed is what was sent the second time, byte for byte. That is the
    // whole promise: the screen never unmounted and nothing was retyped.
    expect(result.received).toEqual(body)
    expect(seen).toEqual(['expired'])
    expect(attempts).toBe(2)
  })

  it('says the session was revoked rather than expired when the server said so', async () => {
    transport.route('GET /api/v1/sessions', () =>
      problemResponse(401, 'identity.session-required', {}, { 'X-Session-State': 'revoked' }),
    )

    const seen: string[] = []
    setSessionChallengeHandler((state) => {
      seen.push(state)
      return Promise.resolve(false)
    })

    await expect(apiRequest('/api/v1/sessions')).rejects.toBeInstanceOf(ApiError)
    expect(seen).toEqual(['revoked'])
  })

  it('throws the original failure when the person abandons the dialog', async () => {
    transport.route('GET /api/v1/sessions', () => problemResponse(401, 'identity.session-required'))
    setSessionChallengeHandler(() => Promise.resolve(false))

    const failure: unknown = await apiRequest('/api/v1/sessions').catch((cause: unknown) => cause)

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).status).toBe(401)
    expect(transport.callsTo('GET /api/v1/sessions')).toHaveLength(1)
  })

  it('raises one dialog for a burst of requests that all fail together', async () => {
    let answered = 0
    transport.route('GET /api/v1/sessions', () =>
      answered > 0 ? jsonResponse([]) : problemResponse(401, 'identity.session-required'),
    )

    let raised = 0
    setSessionChallengeHandler(() => {
      raised += 1
      return new Promise<boolean>((resolve) => {
        setTimeout(() => {
          answered = 1
          resolve(true)
        }, 0)
      })
    })

    await Promise.all([
      apiRequest('/api/v1/sessions'),
      apiRequest('/api/v1/sessions'),
      apiRequest('/api/v1/sessions'),
    ])

    // Ten refused requests must not stack ten dialogs on top of a measurement wizard.
    expect(raised).toBe(1)
  })

  it('does not challenge the endpoints that are themselves how you sign in', async () => {
    transport.route('POST /api/v1/auth/login', () =>
      problemResponse(401, 'identity.invalid-credentials'),
    )
    let raised = 0
    setSessionChallengeHandler(() => {
      raised += 1
      return Promise.resolve(true)
    })

    await expect(
      apiRequest('/api/v1/auth/login', {
        method: 'POST',
        body: {},
        challengeOnUnauthenticated: false,
      }),
    ).rejects.toBeInstanceOf(ApiError)

    // Otherwise a refused sign-in would open a dialog asking somebody to sign in so that they can
    // sign in, which is the most reliable way to lock a person out of an application.
    expect(raised).toBe(0)
  })

  it('does not challenge when nothing has registered a way to', async () => {
    transport.route('GET /api/v1/me', () => problemResponse(401, 'identity.session-required'))

    await expect(apiRequest('/api/v1/me')).rejects.toMatchObject({ status: 401 })
  })
})

describe('what a failure carries back to the screen', () => {
  it('parses the problem, the code and the correlation identifier', async () => {
    transport.route('POST /api/v1/auth/login', () =>
      problemResponse(401, 'identity.invalid-credentials'),
    )

    const failure = (await apiRequest('/api/v1/auth/login', {
      method: 'POST',
      body: {},
      challengeOnUnauthenticated: false,
    }).catch((cause: unknown) => cause)) as ApiError

    expect(failure.status).toBe(401)
    expect(failure.code).toBe('identity.invalid-credentials')
    expect(failure.problem?.correlationId).toBe('0199aa11-2233-4455-6677-8899aabbccdd')
  })

  it('reports a connection that never landed as a failure with no status at all', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))),
    )

    const failure = (await apiRequest('/api/v1/me').catch((cause: unknown) => cause)) as ApiError

    expect(failure).toBeInstanceOf(ApiError)
    expect(failure.status).toBeUndefined()
  })

  it('lets an abort through untouched, so a cancelled screen is not an error', async () => {
    const controller = new AbortController()
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new DOMException('aborted', 'AbortError'))),
    )
    controller.abort()

    await expect(apiRequest('/api/v1/me', { signal: controller.signal })).rejects.toMatchObject({
      name: 'AbortError',
    })
  })

  it('reads a 204 as nothing rather than as a parse failure', async () => {
    transport.route('DELETE /api/v1/sessions/abc', () => noContent())
    await expect(apiRequest('/api/v1/sessions/abc', { method: 'DELETE' })).resolves.toBeUndefined()
  })
})
