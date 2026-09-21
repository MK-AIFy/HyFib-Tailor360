import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { forgetAntiforgeryToken } from '../auth/antiforgery'
import { setSessionChallengeHandler } from '../auth/apiClient'
import { jsonResponse, problemResponse, stubFetch } from '../auth/testing/fixtures'
import type { FetchStub } from '../auth/testing/fixtures'
import {
  correctCustomer,
  readDuplicateCandidates,
  registerCustomer,
  searchCustomers,
} from './customersApi'
import { aCustomer, aDuplicateCandidate, versionedResponse } from './testing/fixtures'
import { CUSTOMER_DUPLICATES_CODE } from './types'

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
  vi.restoreAllMocks()
})

describe('searchCustomers', () => {
  // The function's own doc comment claims this; a name with an ampersand or a plus sign in it must
  // travel as one value, never be read back as a second query parameter or a literal space.
  it('encodes the term, so a name is never read as a second parameter', async () => {
    transport.route('GET /api/v1/customers/?term=Selvam+%26+Sons', () =>
      jsonResponse({ customers: [], nextCursor: null }),
    )

    await searchCustomers('Selvam & Sons')

    expect(transport.callsTo('GET /api/v1/customers/?term=Selvam+%26+Sons')).toHaveLength(1)
  })
})

describe('readDuplicateCandidates', () => {
  it('reads the candidates off the 409 that registerCustomer throws', async () => {
    const candidate = aDuplicateCandidate()
    transport.route('POST /api/v1/customers/', () =>
      problemResponse(409, CUSTOMER_DUPLICATES_CODE, { candidates: [candidate] }),
    )

    const failure: unknown = await registerCustomer({
      details: { displayName: 'Priya Selvam' },
      duplicatesReviewed: false,
      idempotencyKey: 'idem-duplicates',
    }).catch((cause: unknown) => cause)

    expect(readDuplicateCandidates(failure)).toEqual([candidate])
  })

  it('is null for a refusal that is not the duplicate question', async () => {
    transport.route('POST /api/v1/customers/', () =>
      problemResponse(403, 'security.permission-denied'),
    )

    const failure: unknown = await registerCustomer({
      details: { displayName: 'Priya Selvam' },
      duplicatesReviewed: false,
      idempotencyKey: 'idem-forbidden',
    }).catch((cause: unknown) => cause)

    expect(readDuplicateCandidates(failure)).toBeNull()
  })

  // The defensive branches below construct the shape by hand rather than through a real failed
  // request, because they are guarding against a value the server is not expected to send — the
  // point of a type guard is what it does with the case nobody promised would not happen.
  it('is null for the right code with no candidates list at all', () => {
    expect(readDuplicateCandidates({ problem: { code: CUSTOMER_DUPLICATES_CODE } })).toBeNull()
  })

  it('is null for anything not shaped like a failure carrying a problem', () => {
    expect(readDuplicateCandidates(null)).toBeNull()
    expect(readDuplicateCandidates(undefined)).toBeNull()
    expect(readDuplicateCandidates('a plain string')).toBeNull()
    expect(readDuplicateCandidates(new Error('boom'))).toBeNull()
    expect(readDuplicateCandidates({ problem: null })).toBeNull()
    expect(readDuplicateCandidates({ problem: 'not an object' })).toBeNull()
  })
})

describe('correctCustomer', () => {
  const CUSTOMER_ID = '0199cc00-0000-7000-8000-000000000001'

  // The three headers are what make the correction safe, and all three are the transport's job —
  // this asserts the call site actually asks for them, because a correction sent without `If-Match`
  // overwrites a colleague's save and looks like it worked.
  it('presents the version it was read at, a retry key, and the reason in the body', async () => {
    transport.route(`PUT /api/v1/customers/${CUSTOMER_ID}`, () =>
      versionedResponse(aCustomer({ displayName: 'Priya S' }), 'W/"8"'),
    )

    const corrected = await correctCustomer({
      customerId: CUSTOMER_ID,
      details: { displayName: 'Priya S', language: 'en-IN' },
      reason: 'Spelling on her identity document',
      version: 'W/"7"',
      idempotencyKey: 'idem-correct',
    })

    expect(corrected.version).toBe('W/"8"')
    expect(corrected.value.displayName).toBe('Priya S')

    const [sent] = transport.callsTo(`PUT /api/v1/customers/${CUSTOMER_ID}`)
    expect(sent?.headers.get('If-Match')).toBe('W/"7"')
    expect(sent?.headers.get('Idempotency-Key')).toBe('idem-correct')
    expect(sent?.body).toEqual({
      displayName: 'Priya S',
      language: 'en-IN',
      reason: 'Spelling on her identity document',
    })
  })
})
