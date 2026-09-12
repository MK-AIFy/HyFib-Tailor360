import { render, screen, waitFor, within } from '@testing-library/react'
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
import { aCurrentUser, jsonResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { ShellStatusProvider } from '../../components/layout/ShellStatusProvider'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import {
  ORDER_ID,
  anAvailablePaymentModeList,
  anOrderBalance,
  aPayment,
} from '../../billing/testing/fixtures'
import { TakePaymentRoute } from './TakePaymentRoute'

let transport: FetchStub
const formatters = getFormatters()

const BALANCE = `/api/v1/billing/orders/${ORDER_ID}/balance`
const MODES = '/api/v1/billing/payment-modes/available'
const PAYMENTS = '/api/v1/billing/payments'

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [BILLING_PERMISSIONS.recordPayment] })),
  )
  transport.route(`GET ${BALANCE}`, () => jsonResponse(anOrderBalance()))
  transport.route(`GET ${MODES}`, () => jsonResponse(anAvailablePaymentModeList()))
  transport.route(`POST ${PAYMENTS}`, () => jsonResponse(aPayment(), 201))
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
                  path="/billing/payments/new"
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.recordPayment}>
                      <TakePaymentRoute />
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

const PATH = `/billing/payments/new?orderId=${ORDER_ID}&orderNumber=O-CBE01-2627-000512`

describe('taking a payment — form validation', () => {
  it('refuses to open the confirmation until an amount and a mode are chosen', async () => {
    const user = userEvent.setup()
    renderAt(PATH)

    await user.click(await screen.findByRole('button', { name: 'Record payment' }))

    expect(screen.getByText('Enter an amount before recording the payment.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(transport.callsTo(`POST ${PAYMENTS}`)).toHaveLength(0)
  })

  it('quick-fills the full balance', async () => {
    const user = userEvent.setup()
    renderAt(PATH)

    await user.click(
      await screen.findByRole('button', { name: `Full balance (${formatters.formatMoney(309)})` }),
    )

    expect(screen.getByLabelText('Amount')).toHaveValue('309.00')
  })

  it('asks for a reference when the chosen mode requires one', async () => {
    const user = userEvent.setup()
    renderAt(PATH)

    await user.type(await screen.findByLabelText('Amount'), '309')
    await user.click(screen.getByRole('radio', { name: 'Card' }))
    await user.click(screen.getByRole('button', { name: 'Record payment' }))

    expect(screen.getByText('This mode needs a reference.')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${PAYMENTS}`)).toHaveLength(0)
  })

  it('shows the cash tendered field and the change only for cash', async () => {
    const user = userEvent.setup()
    renderAt(PATH)

    await user.type(await screen.findByLabelText('Amount'), '309')
    expect(screen.queryByLabelText('Cash tendered')).not.toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: 'Cash' }))
    await user.type(screen.getByLabelText('Cash tendered'), '500')

    expect(screen.getByText(`Change to give: ${formatters.formatMoney(191)}`)).toBeInTheDocument()
  })

  it('says how much short the tendered amount is, rather than a negative change', async () => {
    const user = userEvent.setup()
    renderAt(PATH)

    await user.type(await screen.findByLabelText('Amount'), '309')
    await user.click(screen.getByRole('radio', { name: 'Cash' }))
    await user.type(screen.getByLabelText('Cash tendered'), '100')

    expect(
      screen.getByText(`${formatters.formatMoney(209)} short of the amount being recorded`),
    ).toBeInTheDocument()
  })

  it('records the payment behind a confirmation, with a retry key, and shows the receipt', async () => {
    const user = userEvent.setup()
    renderAt(PATH)

    await user.type(await screen.findByLabelText('Amount'), '309')
    await user.click(screen.getByRole('radio', { name: 'UPI' }))
    await user.type(screen.getByLabelText('Reference'), 'UPI-426114-8QX2')
    await user.click(screen.getByRole('button', { name: 'Record payment' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Record this payment?')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }))

    expect(await screen.findByText('Payment recorded')).toBeInTheDocument()

    const [recorded] = transport.callsTo(`POST ${PAYMENTS}`)
    expect(recorded?.body).toEqual({
      orderId: ORDER_ID,
      modeCode: 'UPI',
      amount: 309,
      reference: 'UPI-426114-8QX2',
    })
    expect(recorded?.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)

    expect(screen.getByRole('button', { name: 'Send to the print queue' })).toBeInTheDocument()
    expect(screen.getByText('In-app reference: R-CBE01-2627-001366')).toBeInTheDocument()
  })

  it('blocks recording while offline and keeps what was typed', async () => {
    const user = userEvent.setup()
    renderAt(PATH)

    await user.type(await screen.findByLabelText('Amount'), '250')
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText('Needs connection — this will not be queued'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Record payment' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Amount')).toHaveValue('250')

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('has no accessibility violations', async () => {
    const { container } = renderAt(PATH)
    await screen.findByLabelText('Amount')
    await expectNoAccessibilityViolations(container)
  })

  // Codex review, PR #217: clicking "Take another payment" used to clear only the form fields, so
  // the balance read before the just-recorded payment stayed on screen — a cashier could quick-fill
  // and record the same, now-settled, full balance again, creating an unintended advance.
  it('reloads the balance before offering another payment, hiding the stale figure meanwhile', async () => {
    const user = userEvent.setup()
    let balanceCalls = 0
    // A plain `let` closed over inside the responder below defeats TypeScript's narrowing (it
    // proves the variable can never be reassigned before its later, optional call, which is wrong
    // at runtime); a mutable holder object sidesteps that.
    const secondBalance: { resolve: (() => void) | null } = { resolve: null }

    transport.route(`GET ${BALANCE}`, () => {
      balanceCalls += 1
      if (balanceCalls === 1) {
        return jsonResponse(anOrderBalance())
      }
      return new Promise<Response>((resolve) => {
        secondBalance.resolve = () => {
          resolve(jsonResponse(anOrderBalance({ outstanding: 0 })))
        }
      })
    })

    renderAt(PATH)

    await user.click(
      await screen.findByRole('button', { name: `Full balance (${formatters.formatMoney(309)})` }),
    )
    await user.click(screen.getByRole('radio', { name: 'Cash' }))
    await user.click(screen.getByRole('button', { name: 'Record payment' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }))
    await screen.findByText('Payment recorded')

    await user.click(screen.getByRole('button', { name: 'Take another payment' }))

    expect(transport.callsTo(`GET ${BALANCE}`)).toHaveLength(2)
    // The reload is still in flight: the pre-payment full balance must not be offered again.
    expect(
      screen.queryByRole('button', { name: `Full balance (${formatters.formatMoney(309)})` }),
    ).not.toBeInTheDocument()
    expect(screen.getByText('Loading the order’s balance…')).toBeInTheDocument()

    secondBalance.resolve?.()

    await waitFor(() => {
      expect(screen.getByText('Nothing is outstanding on this order.')).toBeInTheDocument()
    })
    expect(
      screen.queryByRole('button', { name: `Full balance (${formatters.formatMoney(309)})` }),
    ).not.toBeInTheDocument()
  })
})
