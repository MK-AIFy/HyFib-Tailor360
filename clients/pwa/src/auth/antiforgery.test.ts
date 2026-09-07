import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ANTIFORGERY_ENDPOINT,
  ANTIFORGERY_HEADER,
  antiforgeryToken,
  forgetAntiforgeryToken,
  hasAntiforgeryToken,
} from './antiforgery'
import { jsonResponse, stubFetch } from './testing/fixtures'
import type { FetchStub } from './testing/fixtures'

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  transport = stubFetch()
})

afterEach(() => {
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
})

describe('the anti-forgery request token', () => {
  it('names the header the server expects', () => {
    expect(ANTIFORGERY_HEADER).toBe('X-CSRF-Token')
    expect(ANTIFORGERY_ENDPOINT).toBe('/api/v1/antiforgery')
  })

  it('is fetched once and held for the life of the page', async () => {
    expect(hasAntiforgeryToken()).toBe(false)

    expect(await antiforgeryToken()).toBe('test-request-token')
    expect(await antiforgeryToken()).toBe('test-request-token')

    expect(transport.callsTo(`GET ${ANTIFORGERY_ENDPOINT}`)).toHaveLength(1)
    expect(hasAntiforgeryToken()).toBe(true)
  })

  it('makes one request for a burst of callers rather than one each', async () => {
    // Two writes at once must not fetch two pairs: the second response replaces the cookie the
    // first token was paired with, and the first write is then refused for no visible reason.
    const [a, b, c] = await Promise.all([
      antiforgeryToken(),
      antiforgeryToken(),
      antiforgeryToken(),
    ])

    expect([a, b, c]).toEqual(['test-request-token', 'test-request-token', 'test-request-token'])
    expect(transport.callsTo(`GET ${ANTIFORGERY_ENDPOINT}`)).toHaveLength(1)
  })

  it('is forgotten on demand, which is what signing in and out do', async () => {
    await antiforgeryToken()
    forgetAntiforgeryToken()

    expect(hasAntiforgeryToken()).toBe(false)
    await antiforgeryToken()
    expect(transport.callsTo(`GET ${ANTIFORGERY_ENDPOINT}`)).toHaveLength(2)
  })

  it('asks for nothing to be cached, because a cached token is stale or shared', async () => {
    await antiforgeryToken()

    const init = transport.fetch.mock.calls[0]?.[1] as RequestInit | undefined
    expect(init?.cache).toBe('no-store')
    expect(init?.credentials).toBe('same-origin')
  })

  it('refuses a response that is not a token rather than sending an empty header', async () => {
    forgetAntiforgeryToken()
    transport.route(`GET ${ANTIFORGERY_ENDPOINT}`, () => jsonResponse({ token: '' }))

    await expect(antiforgeryToken()).rejects.toThrow(/unexpected payload/)
  })

  it('lets a later attempt succeed after a failed one', async () => {
    forgetAntiforgeryToken()
    let attempts = 0
    transport.route(`GET ${ANTIFORGERY_ENDPOINT}`, () => {
      attempts += 1
      return attempts === 1
        ? new Response('nope', { status: 503 })
        : jsonResponse({ token: 'second-token', headerName: ANTIFORGERY_HEADER })
    })

    await expect(antiforgeryToken()).rejects.toThrow()
    expect(await antiforgeryToken()).toBe('second-token')
  })
})
