import { render, screen, waitFor } from '@testing-library/react'
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
    // Grand total and Balance due both print ₹720.00 — the document view's own totals block, under
    // BillingDocumentTemplate.cs's rule that an invoice (never a note) carries a balance-due row.
    expect(screen.getAllByText('₹720.00')).toHaveLength(2)
    expect(screen.getByText('Balance due')).toBeInTheDocument()

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
    // The invoice's own grand total and balance due (in the document view's totals block) and the
    // compensating note's total (in the notes section, relieving the same amount in full) all print
    // ₹720.00.
    expect(screen.getAllByText('₹720.00')).toHaveLength(3)

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

describe('the print and download controls (#336)', () => {
  const printUrl = `${invoiceUrl}/print`
  const documentUrl = `${invoiceUrl}/document`

  function pdfResponse(disposition: string | null = 'attachment; filename="doc.pdf"'): Response {
    const headers = new Headers({ 'Content-Type': 'application/pdf' })
    if (disposition !== null) {
      headers.set('Content-Disposition', disposition)
    }
    return new Response(new Blob(['%PDF-1.4 synthetic'], { type: 'application/pdf' }), {
      status: 200,
      headers,
    })
  }

  it('downloads the document: streams the bytes and revokes the object URL it created', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`GET ${documentUrl}`, () =>
      pdfResponse('attachment; filename="INV-CBE01-2627-000731.pdf"'),
    )
    const createObjectURL = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
    const revokeObjectURL = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined)

    renderAt()
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    await user.click(screen.getByRole('button', { name: 'Download PDF' }))

    await waitFor(() => {
      expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url')
    })
    expect(createObjectURL).toHaveBeenCalledTimes(1)
    expect(createObjectURL.mock.calls[0]?.[0]).toBeInstanceOf(Blob)
  })

  it('sends one to five copies to the print station, announces the job, and a retry after a failure reuses the same Idempotency-Key', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    let attempt = 0
    transport.route(`POST ${printUrl}`, () => {
      attempt += 1
      return attempt === 1
        ? problemResponse(503, 'platform.unavailable')
        : jsonResponse({ printJobId: 'PJ-CBE01-000042' })
    })

    renderAt()
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    await user.click(screen.getByRole('button', { name: 'Send to print station' }))
    await screen.findByText(
      'The shop system could not finish this. It is not something you did wrong.',
    )

    const [first] = transport.callsTo(`POST ${printUrl}`)
    expect(first?.body).toEqual({ copies: 1 })
    const firstKey = first?.headers.get('Idempotency-Key')
    expect(firstKey).toMatch(/[0-9a-f-]{36}/)

    await user.click(screen.getByRole('button', { name: 'Send to print station' }))
    await screen.findByText(
      'Sent to the branch’s print queue as job PJ-CBE01-000042. Nothing prints yet — the print bridge is a later change.',
    )

    const calls = transport.callsTo(`POST ${printUrl}`)
    expect(calls).toHaveLength(2)
    expect(calls[1]?.headers.get('Idempotency-Key')).toBe(firstKey)
  })

  it('refuses 0 and 6 copies before any request is sent, and a stubbed out-of-range refusal from the server reads the same sentence', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`POST ${printUrl}`, () => problemResponse(422, 'billing.copies-out-of-range'))

    renderAt()
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    const copies = screen.getByLabelText('Copies')
    const sendButton = screen.getByRole('button', { name: 'Send to print station' })

    await user.clear(copies)
    await user.type(copies, '0')
    await user.click(sendButton)
    expect(screen.getByText('Choose between 1 and 5 copies.')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${printUrl}`)).toHaveLength(0)

    await user.clear(copies)
    await user.type(copies, '6')
    await user.click(sendButton)
    expect(screen.getByText('Choose between 1 and 5 copies.')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${printUrl}`)).toHaveLength(0)

    await user.clear(copies)
    await user.type(copies, '3')
    await user.click(sendButton)

    expect(await screen.findByText('Choose between 1 and 5 copies.')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${printUrl}`)).toHaveLength(1)
    expect(screen.queryByText('billing.copies-out-of-range')).not.toBeInTheDocument()
  })

  it('renders a 403 refusing the print station as a sentence, never a code', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`POST ${printUrl}`, () => problemResponse(403, 'billing.print-forbidden'))

    renderAt()
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    await user.click(screen.getByRole('button', { name: 'Send to print station' }))

    expect(
      await screen.findByText('This did not go through, and the reason is not clear.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('billing.print-forbidden')).not.toBeInTheDocument()
  })

  it('reports billing.document-not-available as a sentence, keeps Print this page working, and offers no control that could be mistaken for re-posting', async () => {
    const user = userEvent.setup()
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined)
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`GET ${documentUrl}`, () =>
      problemResponse(409, 'billing.document-not-available'),
    )

    renderAt()
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    await user.click(screen.getByRole('button', { name: 'Download PDF' }))

    expect(
      await screen.findByText(
        'This document has not finished rendering yet. You can still print this page — try downloading again shortly.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText('billing.document-not-available')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /post/i })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Print this page' }))
    expect(print).toHaveBeenCalledTimes(1)
  })

  it('still downloads and prints the document of a cancelled invoice', async () => {
    const user = userEvent.setup()
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined)
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(aCancelledInvoice()))
    transport.route(`GET ${documentUrl}`, () => pdfResponse())
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
    const revokeObjectURL = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined)

    renderAt()
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    await user.click(screen.getByRole('button', { name: 'Download PDF' }))
    await waitFor(() => {
      expect(revokeObjectURL).toHaveBeenCalled()
    })

    await user.click(screen.getByRole('button', { name: 'Print this page' }))
    expect(print).toHaveBeenCalledTimes(1)
  })

  it('offline, blocks the print-station queue and the download and sends neither request, while Print this page still works', async () => {
    const user = userEvent.setup()
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined)
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt()
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText(
        'Sending to the print station needs a connection. It has not been sent, and it will not be sent later.',
      ),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Downloading the invoice needs a connection. It has not been sent, and it will not be sent later.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Send to print station' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Download PDF' })).not.toBeInTheDocument()

    const callsBefore = transport.calls.length
    await user.click(screen.getByRole('button', { name: 'Print this page' }))
    expect(print).toHaveBeenCalledTimes(1)
    expect(transport.calls.length).toBe(callsBefore)

    window.dispatchEvent(new Event('online'))
  })
})
