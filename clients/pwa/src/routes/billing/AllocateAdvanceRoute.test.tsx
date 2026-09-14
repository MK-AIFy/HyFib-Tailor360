import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { getFormatters } from '../../i18n/formatters'
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
  ORDER_ID,
  PAYMENT_ID,
  anOrderBalance,
  aPayment,
} from '../../billing/testing/fixtures'
import { AllocateAdvanceRoute } from './AllocateAdvanceRoute'

let transport: FetchStub
const formatters = getFormatters()

const PAYMENT = `/api/v1/billing/payments/${PAYMENT_ID}`
const BALANCE = `/api/v1/billing/orders/${ORDER_ID}/balance`
const ALLOCATIONS = `${PAYMENT}/allocations`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [BILLING_PERMISSIONS.allocateAdvanceManual] })),
  )
  transport.route(`GET ${PAYMENT}`, () => jsonResponse(aPayment({ unappliedAdvance: 500 })))
  transport.route(`GET ${BALANCE}`, () => jsonResponse(anOrderBalance()))
  transport.route(`POST ${ALLOCATIONS}`, () => jsonResponse(aPayment({ unappliedAdvance: 300 })))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderAt(path: string) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <ShellStatusProvider>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route element={<RequireSession />}>
                <Route
                  path="/billing/payments/:paymentId/allocate"
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.allocateAdvanceManual}>
                      <AllocateAdvanceRoute />
                    </RequirePermission>
                  }
                />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

const PATH = `/billing/payments/${PAYMENT_ID}/allocate`
const INVOICE_OPTION = `I-CBE01-2627-000731 — ${formatters.formatMoney(309)} outstanding`

describe('allocating an advance by hand', () => {
  it('shows the held advance and the invoice it can be applied to', async () => {
    renderAt(PATH)

    expect(
      await screen.findByText(`${formatters.formatMoney(500)} held, unapplied`),
    ).toBeInTheDocument()
    expect(screen.getByRole('option', { name: INVOICE_OPTION })).toBeInTheDocument()
  })

  it('allocates part of the advance behind a confirmation, asking for a reason, with a retry key', async () => {
    const user = userEvent.setup()
    renderAt(PATH)

    await user.selectOptions(await screen.findByLabelText('Invoice'), INVOICE_OPTION)
    await user.type(screen.getByLabelText('Amount to allocate'), '200')
    await user.click(screen.getByRole('button', { name: 'Allocate' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Allocate this advance?')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Reason'), 'Settling the older invoice first.')
    await user.click(within(dialog).getByRole('button', { name: 'Allocate' }))

    expect(await screen.findByText('Advance allocated')).toBeInTheDocument()

    const [recorded] = transport.callsTo(`POST ${ALLOCATIONS}`)
    expect(recorded?.body).toEqual({
      invoiceId: INVOICE_ID,
      amount: 200,
      reason: 'Settling the older invoice first.',
    })
    expect(recorded?.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)
  })

  it('refuses to open the confirmation until an invoice and an amount are chosen', async () => {
    const user = userEvent.setup()
    renderAt(PATH)

    await user.click(await screen.findByRole('button', { name: 'Allocate' }))

    expect(screen.getByText('Choose an invoice.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(transport.callsTo(`POST ${ALLOCATIONS}`)).toHaveLength(0)
  })

  it('shows Nothing held when the payment carries no advance', async () => {
    transport.route(`GET ${PAYMENT}`, () => jsonResponse(aPayment({ unappliedAdvance: 0 })))
    renderAt(PATH)

    expect(await screen.findByText('Nothing held')).toBeInTheDocument()
    expect(screen.getByText('This payment holds no advance to allocate.')).toBeInTheDocument()
    expect(screen.queryByLabelText('Invoice')).not.toBeInTheDocument()
  })

  it('shows no posted invoice to allocate against when the order has none eligible', async () => {
    transport.route(`GET ${BALANCE}`, () => jsonResponse(anOrderBalance({ invoices: [] })))
    renderAt(PATH)

    expect(
      await screen.findByText(
        'The order this payment was taken against has no other posted invoice with a balance still owing.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByLabelText('Invoice')).not.toBeInTheDocument()
  })

  it('blocks allocating while offline and keeps what was chosen', async () => {
    const user = userEvent.setup()
    const { container } = renderAt(PATH)

    await user.type(await screen.findByLabelText('Amount to allocate'), '200')
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText('Needs connection — this will not be queued'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Allocate' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Amount to allocate')).toHaveValue('200')
    await expectNoAccessibilityViolations(container)

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })
})

describe('accessibility', () => {
  it('has no violations when loaded', async () => {
    const { container } = renderAt(PATH)
    await screen.findByLabelText('Invoice')
    await expectNoAccessibilityViolations(container)
  })

  it('has no violations with nothing held', async () => {
    transport.route(`GET ${PAYMENT}`, () => jsonResponse(aPayment({ unappliedAdvance: 0 })))
    const { container } = renderAt(PATH)
    await screen.findByText('Nothing held')
    await expectNoAccessibilityViolations(container)
  })

  it('has no violations when the payment fails to load', async () => {
    transport.route(`GET ${PAYMENT}`, () => problemResponse(503, 'platform.unavailable'))
    const { container } = renderAt(PATH)
    await screen.findByRole('alert')
    await expectNoAccessibilityViolations(container)
  })

  it('has no violations when forbidden', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: [] })))
    const { container } = renderAt(PATH)
    await screen.findByText('You do not have access to this')
    await expectNoAccessibilityViolations(container)
  })
})
