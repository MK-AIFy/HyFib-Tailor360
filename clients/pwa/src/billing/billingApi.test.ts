import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { forgetAntiforgeryToken } from '../auth/antiforgery'
import { setSessionChallengeHandler } from '../auth/apiClient'
import { jsonResponse, stubFetch } from '../auth/testing/fixtures'
import type { FetchStub } from '../auth/testing/fixtures'
import { listOutstandingBalances } from './billingApi'
import { anOutstandingBalancePage, anOutstandingBalanceRow } from './testing/fixtures'

let transport: FetchStub

const OUTSTANDING = '/api/v1/billing/outstanding-balances'

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

describe('listOutstandingBalances', () => {
  // #421: the server-side aggregate replaced the client's own fan-out — a request per page of
  // ListInvoices plus a GetOrderBalance per invoice on it, 123 requests for 120 posted invoices at
  // one branch. This asserts exactly one request per page, the shape the fan-out could never give.
  it('reads one page in one request, passing the limit and the cursor along', async () => {
    const row = anOutstandingBalanceRow()
    transport.route(`GET ${OUTSTANDING}?limit=20`, () =>
      jsonResponse(anOutstandingBalancePage({ rows: [row], nextCursor: 'cursor-2' })),
    )

    const page = await listOutstandingBalances({ limit: 20 })

    expect(page.rows).toEqual([row])
    expect(page.nextCursor).toBe('cursor-2')
    expect(transport.callsTo(`GET ${OUTSTANDING}?limit=20`)).toHaveLength(1)
  })

  it('sends the cursor a previous page returned', async () => {
    transport.route(`GET ${OUTSTANDING}?cursor=cursor-2&limit=20`, () =>
      jsonResponse(anOutstandingBalancePage({ nextCursor: null })),
    )

    const page = await listOutstandingBalances({ cursor: 'cursor-2', limit: 20 })

    expect(page.nextCursor).toBeNull()
    expect(transport.callsTo(`GET ${OUTSTANDING}?cursor=cursor-2&limit=20`)).toHaveLength(1)
  })

  it('answers an empty page with a non-null cursor without treating it as an error', async () => {
    // The scan bound's own exception: a page can find nothing yet still have more to look through.
    transport.route(`GET ${OUTSTANDING}?limit=20`, () =>
      jsonResponse(anOutstandingBalancePage({ rows: [], nextCursor: 'cursor-2' })),
    )

    const page = await listOutstandingBalances({ limit: 20 })

    expect(page.rows).toEqual([])
    expect(page.nextCursor).toBe('cursor-2')
  })
})
