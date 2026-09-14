import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { ShellStatusProvider } from '../../components/layout/ShellStatusProvider'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import { ORDER_ID, anInvoice, versionedResponse } from '../../billing/testing/fixtures'
import { InvoiceDraftRoute } from './InvoiceDraftRoute'

let transport: FetchStub

const draftUrl = '/api/v1/billing/invoices'
const CALCULATION_REFERENCE = `order:${ORDER_ID}:1`

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

function renderAt(search: string) {
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [BILLING_PERMISSIONS.createInvoice] })),
  )

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <ShellStatusProvider>
          <MemoryRouter initialEntries={[`/billing/invoices/new${search}`]}>
            <Routes>
              <Route element={<RequireSession />}>
                <Route
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
                      <InvoiceDraftRoute />
                    </RequirePermission>
                  }
                  path="/billing/invoices/new"
                />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

const FULL_QUERY = `?orderId=${ORDER_ID}&calculation=${encodeURIComponent(CALCULATION_REFERENCE)}`

describe('raising an invoice for an order (#345)', () => {
  it('says this screen is opened from an order when a parameter is missing, and sends no request', async () => {
    renderAt('')

    expect(await screen.findByText('This screen is opened from an order')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${draftUrl}`)).toHaveLength(0)
  })

  it('offline, blocks the draft and sends no request', async () => {
    // Positioned as the first test to render InvoiceDraft with real parameters: useNetworkState
    // reads navigator.onLine lazily, once, into a module-level snapshot shared by every test in this
    // file — spying on it only takes effect before anything has read it yet.
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)

    renderAt(FULL_QUERY)

    expect(
      await screen.findByText(
        'Drafting this invoice needs a connection. It has not been sent, and it will not be sent later.',
      ),
    ).toBeInTheDocument()
    expect(transport.callsTo(`POST ${draftUrl}`)).toHaveLength(0)

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('drafts the invoice from the two query parameters with an Idempotency-Key, and shows the lines and totals the server returned', async () => {
    transport.route(`POST ${draftUrl}`, () =>
      versionedResponse(anInvoice({ status: 'Draft', invoiceNumber: null }), 'W/"1"', 201),
    )

    renderAt(FULL_QUERY)

    expect(await screen.findByText('Blouse stitching')).toBeInTheDocument()
    expect(screen.getByText('Sleeve alteration')).toBeInTheDocument()
    expect(screen.getByText('CGST 2.5%: ₹12.63')).toBeInTheDocument()
    expect(screen.getAllByText('₹720.00')).toHaveLength(2)

    const [sent] = transport.callsTo(`POST ${draftUrl}`)
    expect(sent?.body).toEqual({
      orderId: ORDER_ID,
      calculationReference: CALCULATION_REFERENCE,
      garmentJobIds: null,
      reason: null,
    })
    expect(sent?.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)

    expect(screen.getByRole('link', { name: 'Open this invoice' })).toHaveAttribute(
      'href',
      `/billing/invoices/${anInvoice().invoiceId}`,
    )
  })

  it('renders billing.job-already-invoiced as a sentence', async () => {
    transport.route(`POST ${draftUrl}`, () => problemResponse(409, 'billing.job-already-invoiced'))

    renderAt(FULL_QUERY)

    expect(
      await screen.findByText('One of these garment jobs is already on another invoice.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('billing.job-already-invoiced')).not.toBeInTheDocument()
  })

  it('renders billing.order-cancelled as a sentence', async () => {
    transport.route(`POST ${draftUrl}`, () => problemResponse(409, 'billing.order-cancelled'))

    renderAt(FULL_QUERY)

    expect(await screen.findByText('This order was cancelled.')).toBeInTheDocument()
  })

  it('sends billing.snapshot-mismatch to re-price, never implying the invoice itself is broken, and offers no way to post anyway', async () => {
    transport.route(`POST ${draftUrl}`, () => problemResponse(409, 'billing.snapshot-mismatch'))

    renderAt(FULL_QUERY)

    expect(
      await screen.findByText(
        'The stored price no longer adds up. Re-price the order rather than posting this draft.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Post' })).not.toBeInTheDocument()
  })

  it('retries a failed draft with the same Idempotency-Key', async () => {
    let attempt = 0
    transport.route(`POST ${draftUrl}`, () => {
      attempt += 1
      return attempt === 1
        ? problemResponse(503, 'platform.unavailable')
        : versionedResponse(anInvoice({ status: 'Draft', invoiceNumber: null }), 'W/"1"', 201)
    })

    renderAt(FULL_QUERY)

    await screen.findByText(
      'The shop system could not finish this. It is not something you did wrong.',
    )
    const [first] = transport.callsTo(`POST ${draftUrl}`)

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    await screen.findByText('Blouse stitching')
    const [, second] = transport.callsTo(`POST ${draftUrl}`)
    expect(first?.headers.get('Idempotency-Key')).toBe(second?.headers.get('Idempotency-Key'))
  })

  it('has no accessibility violations once the draft is shown', async () => {
    transport.route(`POST ${draftUrl}`, () =>
      versionedResponse(anInvoice({ status: 'Draft', invoiceNumber: null }), 'W/"1"', 201),
    )

    const { container } = renderAt(FULL_QUERY)
    await screen.findByText('Blouse stitching')
    await expectNoAccessibilityViolations(container)
  })
})
