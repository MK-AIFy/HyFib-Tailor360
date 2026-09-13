import { render, screen } from '@testing-library/react'
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
import { INVOICE_ID, aCancelledInvoice, anInvoice } from '../../billing/testing/fixtures'
import { InvoiceDetailRoute } from './InvoiceDetailRoute'

let transport: FetchStub

const invoiceUrl = `/api/v1/billing/invoices/${INVOICE_ID}`

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

function renderAt(permissions: readonly string[] = [BILLING_PERMISSIONS.createInvoice]) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <ShellStatusProvider>
          <MemoryRouter initialEntries={[`/billing/invoices/${INVOICE_ID}`]}>
            <Routes>
              <Route element={<RequireSession />}>
                <Route
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
                      <InvoiceDetailRoute />
                    </RequirePermission>
                  }
                  path="/billing/invoices/:invoiceId"
                />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('the invoice detail screen', () => {
  it('shows the number, every line with its tax components, the signed round-off and the grand total', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt()

    expect(
      await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' }),
    ).toBeInTheDocument()

    expect(screen.getByText('Blouse stitching')).toBeInTheDocument()
    expect(screen.getByText('Sleeve alteration')).toBeInTheDocument()
    expect(screen.getByText('CGST 2.5%: ₹12.63')).toBeInTheDocument()
    expect(screen.getByText('SGST 2.5%: ₹12.63')).toBeInTheDocument()
    expect(screen.getByText('FESTIVE10 −₹20.00')).toBeInTheDocument()

    // A11Y-BI-06: the round-off is announced with its label and sign, on a fixture that carries one.
    expect(screen.getByText('Round-off')).toBeInTheDocument()
    expect(screen.getByText('+₹0.74')).toBeInTheDocument()
    expect(screen.getByText('Grand total')).toBeInTheDocument()
    expect(screen.getByText('₹720.00')).toBeInTheDocument()

    // An intra-state invoice never shows IGST, and every non-zero row shown has a non-zero amount.
    expect(screen.queryByText('IGST')).not.toBeInTheDocument()
  })

  it('shows the cancellation banner, with the number and totals unchanged', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(aCancelledInvoice()))

    renderAt()

    expect(await screen.findByText('This invoice is cancelled')).toBeInTheDocument()
    expect(
      screen.getByText('Cancelled 12-09-2026. Issued to the wrong customer.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'INV-CBE01-2627-000731' })).toBeInTheDocument()
    // The invoice's own totals (in the Totals section) and the compensating note's total (in the
    // notes section, relieving the same amount in full) both print ₹720.00.
    expect(screen.getAllByText('₹720.00')).toHaveLength(2)

    // The compensating credit note is listed with its number, reason and total.
    expect(screen.getByText('Credit note CN-CBE01-2627-000046')).toBeInTheDocument()
    expect(screen.getByText('Reason: Issued to the wrong customer.')).toBeInTheDocument()
  })

  it('renders billing.problem.invoiceNotFound for an unknown identifier, not a code', async () => {
    transport.route(`GET ${invoiceUrl}`, () => problemResponse(404, 'billing.invoice-not-found'))

    renderAt()

    expect(await screen.findByText('No invoice matches that reference.')).toBeInTheDocument()
    expect(screen.queryByText('billing.invoice-not-found')).not.toBeInTheDocument()
  })

  it('refuses a caller holding no billing permission before any request is made', async () => {
    renderAt([])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${invoiceUrl}`)).toHaveLength(0)
  })

  it('shows NetworkStatusBanner while offline; the screen has nothing else to block', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt()
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    window.dispatchEvent(new Event('offline'))
    expect(await screen.findByText('No connection')).toBeInTheDocument()

    window.dispatchEvent(new Event('online'))
  })

  it('has no accessibility violations, on an invoice with a non-zero round-off and a cancellation', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(aCancelledInvoice()))

    const { container } = renderAt()
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })
    await expectNoAccessibilityViolations(container)
  })
})
