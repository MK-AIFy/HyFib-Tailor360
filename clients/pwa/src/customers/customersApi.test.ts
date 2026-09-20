import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { forgetAntiforgeryToken } from '../auth/antiforgery'
import { setSessionChallengeHandler } from '../auth/apiClient'
import { jsonResponse, problemResponse, stubFetch } from '../auth/testing/fixtures'
import type { FetchStub } from '../auth/testing/fixtures'
import { readDuplicateCandidates, registerCustomer, searchCustomers } from './customersApi'
import { aDuplicateCandidate } from './testing/fixtures'
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
