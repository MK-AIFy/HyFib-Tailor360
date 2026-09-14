import { render, screen } from '@testing-library/react'
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
import { PAYMENT_ID, aPayment } from '../../billing/testing/fixtures'
import type { Receipt } from '../../billing/types'
import { PaymentDetailRoute } from './PaymentDetailRoute'

let transport: FetchStub
const formatters = getFormatters()

const PAYMENT = `/api/v1/billing/payments/${PAYMENT_ID}`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [BILLING_PERMISSIONS.recordPayment] })),
  )
  transport.route(`GET ${PAYMENT}`, () => jsonResponse(aPayment()))
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
                  path="/billing/payments/:paymentId"
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.recordPayment}>
                      <PaymentDetailRoute />
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

const PATH = `/billing/payments/${PAYMENT_ID}`

/** A payment whose receipt carries the given overrides, without disturbing the rest of the fixture. */
function aPaymentWithReceipt(overrides: Partial<Receipt>) {
  const payment = aPayment()
  return {
    ...payment,
    receipt: payment.receipt === null ? null : { ...payment.receipt, ...overrides },
  }
}

describe('reading a payment’s receipt', () => {
  it('shows the receipt with what was applied and what is still outstanding', async () => {
    renderAt(PATH)

    expect(
      await screen.findByText(
        `${formatters.formatMoney(309)} applied to this order’s posted invoices`,
      ),
    ).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Receipt' })).toBeInTheDocument()
    expect(
      screen.getByText(`${formatters.formatMoney(0)} still outstanding on this order`),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Send to the print queue' })).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'In-app reference: R-CBE01-2627-001366' }),
    ).toBeInTheDocument()
  })

  it('shows an unapplied advance with a link to allocate it now', async () => {
    transport.route(`GET ${PAYMENT}`, () =>
      jsonResponse(aPaymentWithReceipt({ allocated: 0, unappliedAdvance: 309 })),
    )
    renderAt(PATH)

    expect(
      await screen.findByText(`${formatters.formatMoney(309)} held as an advance`),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Allocate this advance now' })).toBeInTheDocument()
  })

  it('renders a payment at another branch as not found, never a raw status', async () => {
    transport.route(`GET ${PAYMENT}`, () => problemResponse(404, 'billing.payment-not-found'))
    renderAt(PATH)

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.queryByText('billing.payment-not-found')).not.toBeInTheDocument()
  })

  it('refuses a caller holding no billing permission before any request is made', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: [] })))
    renderAt(PATH)

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${PAYMENT}`)).toHaveLength(0)
  })
})

describe('accessibility', () => {
  it('has no violations when loaded', async () => {
    const { container } = renderAt(PATH)
    await screen.findByRole('button', { name: 'Send to the print queue' })
    await expectNoAccessibilityViolations(container)
  })

  /** Nothing applied and nothing held — the receipt's own zero-row rendering, not an `EmptyState`. */
  it('has no violations with nothing applied and nothing held', async () => {
    transport.route(`GET ${PAYMENT}`, () =>
      jsonResponse(aPaymentWithReceipt({ allocated: 0, unappliedAdvance: 0 })),
    )
    const { container } = renderAt(PATH)
    await screen.findByRole('button', { name: 'Send to the print queue' })
    expect(screen.queryByText('held as an advance', { exact: false })).not.toBeInTheDocument()
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
