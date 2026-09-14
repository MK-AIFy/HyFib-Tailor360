import { render, screen, within } from '@testing-library/react'
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
import {
  INVOICE_ID,
  JOB_ID,
  aCancelledInvoice,
  aDraftInvoice,
  anAdjustmentNote,
  anInvoice,
} from '../../billing/testing/fixtures'
import { AdjustmentNoteRoute } from './AdjustmentNoteRoute'

let transport: FetchStub

const invoiceUrl = `/api/v1/billing/invoices/${INVOICE_ID}`
const creditUrl = `${invoiceUrl}/credit-notes`
const debitUrl = `${invoiceUrl}/debit-notes`
const CAN_POST = [BILLING_PERMISSIONS.postAdjustmentNote]

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

function renderAt(permissions: readonly string[] = CAN_POST) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <ShellStatusProvider>
          <MemoryRouter initialEntries={[`/billing/invoices/${INVOICE_ID}/notes/new`]}>
            <Routes>
              <Route element={<RequireSession />}>
                <Route
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.postAdjustmentNote}>
                      <AdjustmentNoteRoute />
                    </RequirePermission>
                  }
                  path="/billing/invoices/:invoiceId/notes/new"
                />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('issuing a credit or debit note (#354)', () => {
  it('refuses a caller holding no billing.post_credit_note permission before any request is made', async () => {
    renderAt([])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${invoiceUrl}`)).toHaveLength(0)
  })

  it('posts a credit note against a line, announces the number, the invoice and the amount, and offers to download and open the invoice', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`POST ${creditUrl}`, () => jsonResponse(anAdjustmentNote()))

    renderAt()
    await screen.findByRole('radio', { name: 'Credit note' })

    await user.type(screen.getByLabelText('Blouse stitching — taxable value to move'), '90')
    await user.click(screen.getByRole('button', { name: 'Post credit note' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Reason' }),
      'Lining charged twice.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Post credit note' }))

    expect(
      await screen.findByText(
        'Credit note CN-CBE01-2627-000045 posted against INV-CBE01-2627-000731 for ₹94.50.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Open the invoice' })).toBeInTheDocument()

    const [sent] = transport.callsTo(`POST ${creditUrl}`)
    expect(sent?.body).toEqual({
      lines: [{ garmentJobId: JOB_ID, taxableValue: 90 }],
      reason: 'Lining charged twice.',
    })
  })

  it('posts a debit note to the debit route, with no ceiling on the line', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`POST ${debitUrl}`, () =>
      jsonResponse(anAdjustmentNote({ kind: 'Debit', number: 'DN-CBE01-2627-000012' })),
    )

    renderAt()
    await screen.findByRole('radio', { name: 'Credit note' })

    await user.click(screen.getByRole('radio', { name: 'Debit note' }))
    // Well past the line's own taxable value of 505 — a debit note carries no ceiling.
    await user.type(screen.getByLabelText('Blouse stitching — taxable value to move'), '900')
    await user.click(screen.getByRole('button', { name: 'Post debit note' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Extra alteration.')
    await user.click(within(dialog).getByRole('button', { name: 'Post debit note' }))

    expect(
      await screen.findByText(
        'Debit note DN-CBE01-2627-000012 posted against INV-CBE01-2627-000731 for ₹94.50.',
      ),
    ).toBeInTheDocument()
    expect(transport.callsTo(`POST ${debitUrl}`)).toHaveLength(1)
    expect(transport.callsTo(`POST ${creditUrl}`)).toHaveLength(0)

    const [sent] = transport.callsTo(`POST ${debitUrl}`)
    expect(sent?.body).toEqual({
      lines: [{ garmentJobId: JOB_ID, taxableValue: 900 }],
      reason: 'Extra alteration.',
    })
  })

  it('shows an empty state once every line is fully relieved by credit notes, and offers no amount field', async () => {
    transport.route(`GET ${invoiceUrl}`, () =>
      jsonResponse(
        anInvoice({
          notes: [
            anAdjustmentNote({
              lines: [
                {
                  lineNumber: 1,
                  garmentJobId: JOB_ID,
                  taxableValue: 505,
                  taxes: [],
                  taxTotal: 0,
                  lineTotal: 505,
                },
                {
                  lineNumber: 2,
                  garmentJobId: '0199dd00-0000-7000-8000-000000005003',
                  taxableValue: 180,
                  taxes: [],
                  taxTotal: 0,
                  lineTotal: 180,
                },
              ],
            }),
          ],
        }),
      ),
    )

    renderAt()

    expect(await screen.findByText('Every line is already relieved')).toBeInTheDocument()
    expect(
      screen.queryByLabelText('Blouse stitching — taxable value to move'),
    ).not.toBeInTheDocument()
  })

  it('requires at least one line before opening the confirmation', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt()
    await screen.findByRole('radio', { name: 'Credit note' })

    await user.click(screen.getByRole('button', { name: 'Post credit note' }))

    expect(
      screen.getByText('Enter a taxable value against at least one line before posting.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('renders billing.note-exceeds-line against the offending line, and the other line keeps what was typed', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`POST ${creditUrl}`, () =>
      problemResponse(422, 'billing.note-exceeds-line', {
        errors: { [`lines[${JOB_ID}].taxableValue`]: ['Too much.'] },
      }),
    )

    renderAt()
    await screen.findByRole('radio', { name: 'Credit note' })

    await user.type(screen.getByLabelText('Blouse stitching — taxable value to move'), '999')
    const otherField = screen.getByLabelText('Sleeve alteration — taxable value to move')
    await user.type(otherField, '50')
    await user.click(screen.getByRole('button', { name: 'Post credit note' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Correction.')
    await user.click(within(dialog).getByRole('button', { name: 'Post credit note' }))

    expect(
      await within(dialog).findByText('That is more than this line still carries.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('billing.note-exceeds-line')).not.toBeInTheDocument()
    expect(otherField).toHaveValue('50.00')
  })

  it('renders billing.invoice-not-posted as a sentence for a draft invoice, and offers no form', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(aDraftInvoice()))

    renderAt()

    expect(
      await screen.findByText('Only a posted invoice is cancelled or corrected by a note.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: 'Credit note' })).not.toBeInTheDocument()
  })

  it('renders billing.invoice-already-cancelled as a sentence for a cancelled invoice, and offers no form', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(aCancelledInvoice()))

    renderAt()

    expect(await screen.findByText('This invoice has already been cancelled.')).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: 'Credit note' })).not.toBeInTheDocument()
  })

  it('reuses the same Idempotency-Key across a retry after a failure', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    let attempt = 0
    transport.route(`POST ${creditUrl}`, () => {
      attempt += 1
      return attempt === 1
        ? problemResponse(503, 'platform.unavailable')
        : jsonResponse(anAdjustmentNote())
    })

    renderAt()
    await screen.findByRole('radio', { name: 'Credit note' })

    await user.type(screen.getByLabelText('Blouse stitching — taxable value to move'), '90')
    await user.click(screen.getByRole('button', { name: 'Post credit note' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Reason' }),
      'Lining charged twice.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Post credit note' }))

    await within(dialog).findByText(
      'The shop system could not finish this. It is not something you did wrong.',
    )

    await user.click(within(dialog).getByRole('button', { name: 'Post credit note' }))
    await screen.findByText(
      'Credit note CN-CBE01-2627-000045 posted against INV-CBE01-2627-000731 for ₹94.50.',
    )

    const keys = transport
      .callsTo(`POST ${creditUrl}`)
      .map((call) => call.headers.get('Idempotency-Key'))
    expect(keys).toHaveLength(2)
    expect(keys[0]).not.toBeNull()
    expect(keys[0]).toBe(keys[1])
  })

  it('offline, blocks posting and sends no request', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    renderAt()
    await screen.findByRole('radio', { name: 'Credit note' })

    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText(
        'Posting a note needs a connection. It has not been sent, and it will not be sent later.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Post credit note' })).not.toBeInTheDocument()
    expect(transport.callsTo(`POST ${creditUrl}`)).toHaveLength(0)

    window.dispatchEvent(new Event('online'))
  })

  it('has no accessibility violations on the ready form', async () => {
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))

    const { container } = renderAt()
    await screen.findByRole('radio', { name: 'Credit note' })
    await expectNoAccessibilityViolations(container)
  })

  it('has no accessibility violations once posted', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${invoiceUrl}`, () => jsonResponse(anInvoice()))
    transport.route(`POST ${creditUrl}`, () => jsonResponse(anAdjustmentNote()))

    const { container } = renderAt()
    await screen.findByRole('radio', { name: 'Credit note' })

    await user.type(screen.getByLabelText('Blouse stitching — taxable value to move'), '90')
    await user.click(screen.getByRole('button', { name: 'Post credit note' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Reason' }),
      'Lining charged twice.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Post credit note' }))
    await screen.findByText(
      'Credit note CN-CBE01-2627-000045 posted against INV-CBE01-2627-000731 for ₹94.50.',
    )

    await expectNoAccessibilityViolations(container)
  })
})
