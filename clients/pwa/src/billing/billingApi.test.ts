import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { forgetAntiforgeryToken } from '../auth/antiforgery'
import { setSessionChallengeHandler } from '../auth/apiClient'
import { jsonResponse, stubFetch } from '../auth/testing/fixtures'
import type { FetchStub } from '../auth/testing/fixtures'
import { listOutstandingBalances } from './billingApi'
import {
  anInvoiceBalance,
  anInvoicePage,
  anInvoiceSummary,
  anOrderBalance,
} from './testing/fixtures'

let transport: FetchStub

const INVOICES = '/api/v1/billing/invoices'
const balanceUrl = (orderId: string) => `/api/v1/billing/orders/${orderId}/balance`

const OLDER_ORDER_ID = '0199dd00-0000-7000-8000-00000000f0f0'
const OLDER_INVOICE_ID = '0199dd00-0000-7000-8000-00000000f0f1'
const NEWER_ORDER_ID = '0199dd00-0000-7000-8000-00000000f0f2'
const NEWER_INVOICE_ID = '0199dd00-0000-7000-8000-00000000f0f3'
const CURSOR = 'cursor-page-2'

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
  // Codex review, PR #217: the previous version read only the first page of posted invoices and
  // dropped `nextCursor`, so a branch with more than fifty posted invoices silently lost every
  // older outstanding one — it could even show the empty state while an older invoice still owed
  // money.
  it('follows nextCursor until the server answers null, rather than stopping at the first page', async () => {
    const newerInvoice = anInvoiceSummary({
      invoiceId: NEWER_INVOICE_ID,
      orderId: NEWER_ORDER_ID,
      orderNumber: 'O-CBE01-2627-000900',
    })
    const olderInvoice = anInvoiceSummary({
      invoiceId: OLDER_INVOICE_ID,
      orderId: OLDER_ORDER_ID,
      orderNumber: 'O-CBE01-2627-000100',
    })

    transport.route(`GET ${INVOICES}?status=Posted&limit=50`, () =>
      jsonResponse(anInvoicePage({ invoices: [newerInvoice], nextCursor: CURSOR })),
    )
    transport.route(`GET ${INVOICES}?status=Posted&cursor=${CURSOR}&limit=50`, () =>
      jsonResponse(anInvoicePage({ invoices: [olderInvoice], nextCursor: null })),
    )
    transport.route(`GET ${balanceUrl(NEWER_ORDER_ID)}`, () =>
      jsonResponse(
        anOrderBalance({
          orderId: NEWER_ORDER_ID,
          outstanding: 0,
          invoices: [anInvoiceBalance({ invoiceId: NEWER_INVOICE_ID, outstanding: 0 })],
        }),
      ),
    )
    transport.route(`GET ${balanceUrl(OLDER_ORDER_ID)}`, () =>
      jsonResponse(
        anOrderBalance({
          orderId: OLDER_ORDER_ID,
          outstanding: 250,
          invoices: [anInvoiceBalance({ invoiceId: OLDER_INVOICE_ID, outstanding: 250 })],
        }),
      ),
    )

    const rows = await listOutstandingBalances()

    // The newest page is fully settled — exactly the shape that used to render the empty state —
    // but the older page's genuinely outstanding invoice is still in the answer.
    expect(rows.map((row) => row.invoiceId)).toEqual([OLDER_INVOICE_ID])
    expect(rows[0]?.outstanding).toBe(250)
    expect(transport.callsTo(`GET ${INVOICES}?status=Posted&limit=50`)).toHaveLength(1)
    expect(
      transport.callsTo(`GET ${INVOICES}?status=Posted&cursor=${CURSOR}&limit=50`),
    ).toHaveLength(1)
  })

  it('reads only one page when the server says there is nothing more', async () => {
    transport.route(`GET ${INVOICES}?status=Posted&limit=50`, () =>
      jsonResponse(anInvoicePage({ nextCursor: null })),
    )
    transport.route(`GET ${balanceUrl(anInvoiceSummary().orderId)}`, () =>
      jsonResponse(anOrderBalance()),
    )

    const rows = await listOutstandingBalances()

    expect(rows).toHaveLength(1)
    expect(transport.callsTo(`GET ${INVOICES}?status=Posted&limit=50`)).toHaveLength(1)
  })
})
