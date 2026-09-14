import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'
import {
  aCurrentUser,
  aSignInResult,
  jsonResponse,
  problemResponse,
  stubFetch,
} from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { ShellStatusProvider } from '../../components/layout/ShellStatusProvider'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import {
  INVOICE_ID,
  aCancelledInvoice,
  aDraftInvoice,
  anInvoice,
  versionedResponse,
} from '../../billing/testing/fixtures'
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
    // A string body, not a Blob: constructing a Response from a Blob is unreliable across jsdom's
    // fetch polyfill versions (it fails outright under some Node/jsdom combinations CI exercises,
    // even though the two ought to be equivalent). Response.blob() reads Content-Type off the
    // response's own headers regardless of what the body was constructed from, so this is identical
    // from apiRequestBlob's side.
    return new Response('%PDF-1.4 synthetic', { status: 200, headers })
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
    // Not toBeInstanceOf(Blob): jsdom's Response.blob() and the test's global Blob constructor are
    // different classes under some Node versions, even though the object is a real, correctly-shaped
    // blob — duck-type it instead of asserting a realm-specific identity the browser this ships to
    // does not have two of.
    const blobArg = createObjectURL.mock.calls[0]?.[0] as unknown as Blob
    expect(blobArg.size).toBeGreaterThan(0)
    expect(blobArg.type).toBe('application/pdf')
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

describe('the draft lifecycle: post and discard (#345)', () => {
  const postUrl = `${invoiceUrl}/post`
  const discardUrl = `${invoiceUrl}/discard`
  const ALL_PERMISSIONS = [
    BILLING_PERMISSIONS.createInvoice,
    BILLING_PERMISSIONS.postInvoice,
    BILLING_PERMISSIONS.updateInvoice,
  ]

  it('offers Post and Discard on a draft, and neither on a posted invoice', async () => {
    transport.route(`GET ${invoiceUrl}`, () => versionedResponse(aDraftInvoice(), 'W/"1"'))

    renderAt(ALL_PERMISSIONS)

    await screen.findByRole('heading', { level: 1, name: 'Draft' })
    expect(screen.getByRole('button', { name: 'Post' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Discard' })).toBeInTheDocument()
  })

  it('offers neither control on a posted invoice', async () => {
    transport.route(`GET ${invoiceUrl}`, () => versionedResponse(anInvoice(), 'W/"1"'))

    renderAt(ALL_PERMISSIONS)

    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })
    expect(screen.queryByRole('button', { name: 'Post' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Discard' })).not.toBeInTheDocument()
  })

  it('shows no Post control without billing.post_invoice, and no Discard control without billing.update_invoice', async () => {
    transport.route(`GET ${invoiceUrl}`, () => versionedResponse(aDraftInvoice(), 'W/"1"'))

    renderAt([BILLING_PERMISSIONS.createInvoice])

    await screen.findByRole('heading', { level: 1, name: 'Draft' })
    expect(screen.queryByRole('button', { name: 'Post' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Discard' })).not.toBeInTheDocument()
  })

  it('posts the invoice behind a confirmation that names the amount and the immutability, then announces the allocated number and refreshes the invoice', async () => {
    const user = userEvent.setup()
    let reads = 0
    transport.route(`GET ${invoiceUrl}`, () => {
      reads += 1
      return versionedResponse(reads === 1 ? aDraftInvoice() : anInvoice(), `W/"${String(reads)}"`)
    })
    transport.route(`POST ${postUrl}`, () => versionedResponse(anInvoice(), 'W/"2"'))

    renderAt(ALL_PERMISSIONS)
    await screen.findByRole('heading', { level: 1, name: 'Draft' })

    await user.click(screen.getByRole('button', { name: 'Post' }))
    const dialog = await screen.findByRole('dialog')
    expect(
      within(dialog).getByText(
        '₹720.00. Once posted, this invoice cannot be edited — a correction becomes a credit or debit note.',
      ),
    ).toBeInTheDocument()
    expect(
      within(dialog).getByText('Cannot be undone — a supervisor correction is needed'),
    ).toBeInTheDocument()

    // jsdom answers no media query, so the typed tier's phone substitute applies here exactly as
    // it would on a real phone (confirmTiers.ts, checklist item A11Y-BI-13): a mandatory reason
    // plus a second explicit press, not a typed phrase.
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Reason' }),
      'Reviewed at the counter.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Post' }))
    await user.click(within(dialog).getByRole('button', { name: 'Confirm again' }))

    expect(await screen.findByText('Posted as INV-CBE01-2627-000731.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    const [sent] = transport.callsTo(`POST ${postUrl}`)
    expect(sent?.body).toEqual({ reason: null })
    expect(sent?.headers.get('If-Match')).toBe('W/"1"')

    // The invoice is refetched so the new ETag, number, barcode payload and financial year are in
    // hand — the heading now reads the allocated number rather than "Draft".
    expect(
      await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' }),
    ).toBeInTheDocument()
    expect(transport.callsTo(`GET ${invoiceUrl}`)).toHaveLength(2)
  })

  it('renders a 403 refusing the post as a sentence, never a code', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => versionedResponse(aDraftInvoice(), 'W/"1"'))
    transport.route(`POST ${postUrl}`, () => problemResponse(403, 'billing.post-forbidden'))

    renderAt(ALL_PERMISSIONS)
    await screen.findByRole('heading', { level: 1, name: 'Draft' })

    await user.click(screen.getByRole('button', { name: 'Post' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Reviewed.')
    await user.click(within(dialog).getByRole('button', { name: 'Post' }))
    await user.click(within(dialog).getByRole('button', { name: 'Confirm again' }))

    expect(
      await within(dialog).findByText('This did not go through, and the reason is not clear.'),
    ).toBeInTheDocument()
    expect(within(dialog).queryByText('billing.post-forbidden')).not.toBeInTheDocument()
  })

  it('reuses the same Idempotency-Key across a retry after a failure', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => versionedResponse(aDraftInvoice(), 'W/"1"'))
    let attempt = 0
    transport.route(`POST ${postUrl}`, () => {
      attempt += 1
      return attempt === 1
        ? problemResponse(503, 'platform.unavailable')
        : versionedResponse(anInvoice(), 'W/"2"')
    })

    renderAt(ALL_PERMISSIONS)
    await screen.findByRole('heading', { level: 1, name: 'Draft' })

    await user.click(screen.getByRole('button', { name: 'Post' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Reviewed.')
    await user.click(within(dialog).getByRole('button', { name: 'Post' })) // arms the second press
    await user.click(within(dialog).getByRole('button', { name: 'Confirm again' })) // attempt 1, fails

    await within(dialog).findByText(
      'The shop system could not finish this. It is not something you did wrong.',
    )

    await user.click(within(dialog).getByRole('button', { name: 'Confirm again' })) // attempt 2, same key
    await screen.findByText('Posted as INV-CBE01-2627-000731.')

    const keys = transport
      .callsTo(`POST ${postUrl}`)
      .map((call) => call.headers.get('Idempotency-Key'))
    expect(keys).toHaveLength(2)
    expect(keys[0]).not.toBeNull()
    expect(keys[0]).toBe(keys[1])
  })

  it('discards the draft with its typed reason; a stale ETag refetches the invoice, shows the sentence, does not discard, and keeps the reason', async () => {
    const user = userEvent.setup()
    let reads = 0
    transport.route(`GET ${invoiceUrl}`, () => {
      reads += 1
      return versionedResponse(aDraftInvoice(), `W/"${String(reads)}"`)
    })
    let attempt = 0
    transport.route(`POST ${discardUrl}`, () => {
      attempt += 1
      return attempt === 1
        ? problemResponse(412, 'billing.invoice-changed')
        : versionedResponse(aDraftInvoice({ discardedAt: '2026-09-14T05:00:00.000Z' }), 'W/"2"')
    })

    renderAt(ALL_PERMISSIONS)
    await screen.findByRole('heading', { level: 1, name: 'Draft' })

    await user.click(screen.getByRole('button', { name: 'Discard' }))
    const dialog = await screen.findByRole('dialog')
    const reason = within(dialog).getByRole('textbox', { name: 'Reason' })
    await user.type(reason, 'Drafted against the wrong order.')
    await user.click(within(dialog).getByRole('button', { name: 'Discard' }))

    expect(
      await within(dialog).findByText('Somebody changed this invoice. Here it is again.'),
    ).toBeInTheDocument()
    // The dialog stays open — closing it here would lose what was typed — and the invoice was
    // refetched in the background so the next press carries a fresh precondition.
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(reason).toHaveValue('Drafted against the wrong order.')
    await waitFor(() => {
      expect(transport.callsTo(`GET ${invoiceUrl}`)).toHaveLength(2)
    })

    await user.click(within(dialog).getByRole('button', { name: 'Discard' }))
    expect(await screen.findByText('This draft is discarded.')).toBeInTheDocument()

    const [first, second] = transport.callsTo(`POST ${discardUrl}`)
    expect(first?.body).toEqual({ reason: 'Drafted against the wrong order.' })
    expect(first?.headers.get('If-Match')).toBe('W/"1"')
    expect(second?.headers.get('If-Match')).toBe('W/"2"')
  })

  it('offline, blocks Post and Discard and sends neither request', async () => {
    transport.route(`GET ${invoiceUrl}`, () => versionedResponse(aDraftInvoice(), 'W/"1"'))

    renderAt(ALL_PERMISSIONS)
    await screen.findByRole('heading', { level: 1, name: 'Draft' })

    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText(
        'Posting this invoice needs a connection. It has not been sent, and it will not be sent later.',
      ),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Discarding this draft needs a connection. It has not been sent, and it will not be sent later.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Post' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Discard' })).not.toBeInTheDocument()
    expect(transport.callsTo(`POST ${postUrl}`)).toHaveLength(0)
    expect(transport.callsTo(`POST ${discardUrl}`)).toHaveLength(0)

    window.dispatchEvent(new Event('online'))
  })

  it('has no accessibility violations on a draft invoice with Post and Discard available', async () => {
    transport.route(`GET ${invoiceUrl}`, () => versionedResponse(aDraftInvoice(), 'W/"1"'))

    const { container } = renderAt(ALL_PERMISSIONS)
    await screen.findByRole('heading', { level: 1, name: 'Draft' })
    await expectNoAccessibilityViolations(container)
  })
})

describe('the cancel lifecycle (#354)', () => {
  const cancelUrl = `${invoiceUrl}/cancel`
  const CAN_CANCEL = [BILLING_PERMISSIONS.createInvoice, BILLING_PERMISSIONS.cancelInvoice]

  it('offers Cancel on a posted, not-cancelled invoice, for a caller holding billing.cancel_invoice', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt(CAN_CANCEL)

    expect(await screen.findByRole('button', { name: 'Cancel invoice' })).toBeInTheDocument()
  })

  it('shows no Cancel control on a draft invoice', async () => {
    transport.route(`GET ${invoiceUrl}`, () => versionedResponse(aDraftInvoice(), 'W/"1"'))

    renderAt(CAN_CANCEL)

    await screen.findByRole('heading', { level: 1, name: 'Draft' })
    expect(screen.queryByRole('button', { name: 'Cancel invoice' })).not.toBeInTheDocument()
  })

  it('shows no Cancel control on an already-cancelled invoice', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(aCancelledInvoice()))

    renderAt(CAN_CANCEL)

    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })
    expect(screen.queryByRole('button', { name: 'Cancel invoice' })).not.toBeInTheDocument()
  })

  it('shows no Cancel control without billing.cancel_invoice', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt([BILLING_PERMISSIONS.createInvoice])

    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })
    expect(screen.queryByRole('button', { name: 'Cancel invoice' })).not.toBeInTheDocument()
  })

  it('cancels the invoice behind a confirmation with a reason, announces it, and refreshes the invoice', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`POST ${cancelUrl}`, () =>
      jsonResponse(
        anInvoice({
          cancelled: true,
          cancellation: {
            cancellationId: '0199dd00-0000-7000-8000-000000006001',
            creditNoteId: '0199dd00-0000-7000-8000-000000006002',
            reason: 'Issued to the wrong customer.',
            cancelledAt: '2026-09-14T05:00:00.000Z',
          },
        }),
      ),
    )

    renderAt(CAN_CANCEL)
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    await user.click(screen.getByRole('button', { name: 'Cancel invoice' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Reason' }),
      'Issued to the wrong customer.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Cancel invoice' }))

    expect(await screen.findByText('This invoice is cancelled.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    const [sent] = transport.callsTo(`POST ${cancelUrl}`)
    expect(sent?.body).toEqual({ reason: 'Issued to the wrong customer.' })
    expect(transport.callsTo(`GET ${invoiceUrl}`)).toHaveLength(2)
  })

  it('replays the same Idempotency-Key and reason after a step-up challenge', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route('POST /api/v1/auth/login', () => jsonResponse(aSignInResult()))
    let attempt = 0
    transport.route(`POST ${cancelUrl}`, () => {
      attempt += 1
      return attempt === 1
        ? problemResponse(403, 'security.step-up-required')
        : jsonResponse(anInvoice({ cancelled: true }))
    })

    renderAt(CAN_CANCEL)
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    await user.click(screen.getByRole('button', { name: 'Cancel invoice' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Reason' }),
      'Issued to the wrong customer.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Cancel invoice' }))

    const identityDialog = await screen.findByRole('dialog', { name: 'Confirm it is you' })
    await user.type(within(identityDialog).getByLabelText('Password'), 'synthetic-password')
    await user.click(within(identityDialog).getByRole('button', { name: 'Confirm' }))

    await waitFor(() => {
      expect(transport.callsTo(`POST ${cancelUrl}`)).toHaveLength(2)
    })
    expect(await screen.findByText('This invoice is cancelled.')).toBeInTheDocument()

    const [first, second] = transport.callsTo(`POST ${cancelUrl}`)
    expect(first?.body).toEqual(second?.body)
    expect(first?.headers.get('Idempotency-Key')).toBe(second?.headers.get('Idempotency-Key'))
  })

  it('renders billing.cancellation-window-closed as a sentence pointing at the credit note route, never a code', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`POST ${cancelUrl}`, () =>
      problemResponse(422, 'billing.cancellation-window-closed'),
    )

    renderAt(CAN_CANCEL)
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    await user.click(screen.getByRole('button', { name: 'Cancel invoice' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Too late to cancel.')
    await user.click(within(dialog).getByRole('button', { name: 'Cancel invoice' }))

    expect(
      await within(dialog).findByText(
        'This invoice was posted too long ago to cancel. Post a credit note against it instead.',
      ),
    ).toBeInTheDocument()
    expect(within(dialog).queryByText('billing.cancellation-window-closed')).not.toBeInTheDocument()
  })

  it('renders a 403 refusing the cancellation as a sentence, never a code', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`POST ${cancelUrl}`, () => problemResponse(403, 'billing.cancel-forbidden'))

    renderAt(CAN_CANCEL)
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    await user.click(screen.getByRole('button', { name: 'Cancel invoice' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Reason' }),
      'Issued to the wrong customer.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Cancel invoice' }))

    expect(
      await within(dialog).findByText('This did not go through, and the reason is not clear.'),
    ).toBeInTheDocument()
    expect(within(dialog).queryByText('billing.cancel-forbidden')).not.toBeInTheDocument()
  })

  it('offline, blocks Cancel and sends no request', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt(CAN_CANCEL)
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })

    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText(
        'Cancelling this invoice needs a connection. It has not been sent, and it will not be sent later.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel invoice' })).not.toBeInTheDocument()
    expect(transport.callsTo(`POST ${cancelUrl}`)).toHaveLength(0)

    window.dispatchEvent(new Event('online'))
  })

  it('has no accessibility violations on a posted invoice with Cancel available', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    const { container } = renderAt(CAN_CANCEL)
    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })
    await expectNoAccessibilityViolations(container)
  })
})

describe('the link to issue a credit or debit note (#354)', () => {
  const CAN_ISSUE_NOTES = [
    BILLING_PERMISSIONS.createInvoice,
    BILLING_PERMISSIONS.postAdjustmentNote,
  ]

  it('offers the link on a posted, not-cancelled invoice, for a caller holding billing.post_credit_note', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt(CAN_ISSUE_NOTES)

    expect(
      await screen.findByRole('link', {
        name: 'Issue a credit or debit note against INV-CBE01-2627-000731',
      }),
    ).toBeInTheDocument()
  })

  it('offers no link on a draft invoice', async () => {
    transport.route(`GET ${invoiceUrl}`, () => versionedResponse(aDraftInvoice(), 'W/"1"'))

    renderAt(CAN_ISSUE_NOTES)

    await screen.findByRole('heading', { level: 1, name: 'Draft' })
    expect(
      screen.queryByRole('link', {
        name: 'Issue a credit or debit note against INV-CBE01-2627-000731',
      }),
    ).not.toBeInTheDocument()
  })

  it('offers no link on an already-cancelled invoice', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(aCancelledInvoice()))

    renderAt(CAN_ISSUE_NOTES)

    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })
    expect(
      screen.queryByRole('link', {
        name: 'Issue a credit or debit note against INV-CBE01-2627-000731',
      }),
    ).not.toBeInTheDocument()
  })

  it('offers no link without billing.post_credit_note', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt([BILLING_PERMISSIONS.createInvoice])

    await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' })
    expect(
      screen.queryByRole('link', {
        name: 'Issue a credit or debit note against INV-CBE01-2627-000731',
      }),
    ).not.toBeInTheDocument()
  })
})
