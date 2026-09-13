import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { getFormatters } from '../../i18n/formatters'
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
  BRANCH_ID,
  CASHIER_ID,
  CASHIER_SESSION_ID,
  ORDER_ID,
  anAvailablePaymentModeList,
  aCashierSession,
  anInvoicePage,
  anOrderBalance,
  aPayment,
} from '../../billing/testing/fixtures'
import { CashierSessionRoute } from './CashierSessionRoute'
import { DispatchExceptionApprovalRoute } from './DispatchExceptionApprovalRoute'
import { OutstandingBalancesRoute } from './OutstandingBalancesRoute'
import { TakePaymentRoute } from './TakePaymentRoute'

let transport: FetchStub
const formatters = getFormatters()

const SESSIONS = '/api/v1/billing/cashier-sessions'
const MODES = '/api/v1/billing/payment-modes/available'
const INVOICES = '/api/v1/billing/invoices'
const PAYMENTS = '/api/v1/billing/payments'
const DISPATCH = '/api/v1/billing/dispatch-exceptions'
const balanceUrl = (orderId: string) => `/api/v1/billing/orders/${orderId}/balance`
const dispatchBalanceUrl = (orderId: string) =>
  `/api/v1/billing/orders/${orderId}/dispatch-exception-balance`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(
      aCurrentUser({
        branchId: BRANCH_ID,
        userId: CASHIER_ID,
        permissions: [
          BILLING_PERMISSIONS.createInvoice,
          BILLING_PERMISSIONS.recordPayment,
          BILLING_PERMISSIONS.cashierSession,
          BILLING_PERMISSIONS.approveDispatchException,
        ],
      }),
    ),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderApp(path: string) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <ShellStatusProvider>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route element={<RequireSession />}>
                <Route
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
                      <OutstandingBalancesRoute />
                    </RequirePermission>
                  }
                  path="/billing/outstanding"
                />
                <Route
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.recordPayment}>
                      <TakePaymentRoute />
                    </RequirePermission>
                  }
                  path="/billing/payments/new"
                />
                <Route
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.cashierSession}>
                      <CashierSessionRoute />
                    </RequirePermission>
                  }
                  path="/billing/cashier"
                />
                <Route
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.approveDispatchException}>
                      <DispatchExceptionApprovalRoute />
                    </RequirePermission>
                  }
                  path="/billing/dispatch-exceptions/new"
                />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('the cashier-close journey', () => {
  it('opens a session, counts the drawer, and closes it against the count sheet', async () => {
    const user = userEvent.setup()
    let opened: unknown = null

    transport.route(`GET ${SESSIONS}?status=Open`, () =>
      jsonResponse(opened === null ? [] : [opened]),
    )
    transport.route(`POST ${SESSIONS}`, () => {
      opened = aCashierSession({ status: 'Open', openingFloat: 2000 })
      return jsonResponse(opened, 201)
    })
    transport.route(`GET ${MODES}`, () => jsonResponse(anAvailablePaymentModeList()))
    transport.route(`POST ${SESSIONS}/${CASHIER_SESSION_ID}/close`, () =>
      jsonResponse(
        aCashierSession({
          status: 'Closed',
          closedAt: '2026-09-12T11:00:00.000Z',
          closedBy: CASHIER_ID,
          countedTotal: 4350,
          expectedTotal: 4350,
          variance: 0,
          reconciliationBatch: null,
        }),
      ),
    )

    renderApp('/billing/cashier')

    // No session is open yet: the screen offers to open one.
    await user.type(await screen.findByLabelText('Opening float'), '2000')
    await user.click(screen.getByRole('button', { name: 'Open session' }))

    // Once open, the count sheet appears — every note face value, every coin face value, and the
    // branch's other payment modes.
    await screen.findByLabelText('₹2000 notes')
    await user.type(screen.getByLabelText('₹2000 notes'), '2')
    await user.type(screen.getByLabelText('₹10 notes'), '35')
    // A mixed coin tray — ₹5, ₹2 and ₹1 coins counted separately, as the server's own
    // `CashDenominations.All` requires (Codex review, PR #217): a lump aggregate value is not a
    // denomination it recognises, and was refused with `billing.denomination-not-known`.
    await user.type(screen.getByLabelText('₹5 coins'), '3')
    await user.type(screen.getByLabelText('₹2 coins'), '10')
    await user.type(screen.getByLabelText('₹1 coins'), '5')
    await user.type(screen.getByLabelText('UPI counted'), '0')
    await user.type(screen.getByLabelText('Card counted'), '0')

    expect(screen.getByText(`Cash counted: ${formatters.formatMoney(4390)}`)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Close session' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Close this session?')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Close session' }))

    expect(await screen.findByText('Session closed')).toBeInTheDocument()

    const [closed] = transport.callsTo(`POST ${SESSIONS}/${CASHIER_SESSION_ID}/close`)
    expect(closed?.body).toEqual({
      denominations: [
        { denomination: 2000, quantity: 2 },
        { denomination: 10, quantity: 35 },
        { denomination: 5, quantity: 3 },
        { denomination: 2, quantity: 10 },
        { denomination: 1, quantity: 5 },
      ],
      modeTotals: [
        { modeCode: 'CARD', counted: 0 },
        { modeCode: 'UPI', counted: 0 },
      ],
      reason: null,
    })
    expect(closed?.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)
    // Every denomination sent is one the server's `CashDenominations.All` actually recognises.
    const sentBody = closed?.body as { readonly denominations: readonly { denomination: number }[] }
    for (const line of sentBody.denominations) {
      expect([2000, 500, 200, 100, 50, 20, 10, 5, 2, 1]).toContain(line.denomination)
    }
  })

  it('asks for a reason only once the server says the variance needs one, then closes', async () => {
    const user = userEvent.setup()
    let attempts = 0
    transport.route(`GET ${SESSIONS}?status=Open`, () =>
      jsonResponse([aCashierSession({ status: 'Open' })]),
    )
    transport.route(`GET ${MODES}`, () => jsonResponse(anAvailablePaymentModeList()))
    transport.route(`POST ${SESSIONS}/${CASHIER_SESSION_ID}/close`, () => {
      attempts += 1
      return attempts === 1
        ? problemResponse(422, 'billing.variance-reason-required')
        : jsonResponse(
            aCashierSession({
              status: 'Closed',
              countedTotal: 1000,
              expectedTotal: 1200,
              variance: -200,
              reconciliationBatch: null,
            }),
          )
    })

    renderApp('/billing/cashier')

    await user.type(await screen.findByLabelText('₹500 notes'), '2')
    await user.click(screen.getByRole('button', { name: 'Close session' }))
    let dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Close session' }))

    // Refused without a reason: the same confirmation reopens asking for one, rather than a plain
    // error the person has no way to act on.
    dialog = await screen.findByRole('dialog')
    await within(dialog).findByLabelText('Reason')
    await user.type(within(dialog).getByLabelText('Reason'), 'Counted twice; short till confirmed.')
    await user.click(within(dialog).getByRole('button', { name: 'Close session' }))

    expect(await screen.findByText('Session closed')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${SESSIONS}/${CASHIER_SESSION_ID}/close`)).toHaveLength(2)
    const [, second] = transport.callsTo(`POST ${SESSIONS}/${CASHIER_SESSION_ID}/close`)
    expect(second?.body).toMatchObject({ reason: 'Counted twice; short till confirmed.' })
  })
})

describe('the unpaid-then-paid dispatch journey', () => {
  it('takes the balance at the counter, then the Owner approves a dispatch exception', async () => {
    const user = userEvent.setup()
    let outstanding = 309

    transport.route(`GET ${INVOICES}?status=Posted&limit=50`, () => jsonResponse(anInvoicePage()))
    transport.route(`GET ${balanceUrl(ORDER_ID)}`, () =>
      jsonResponse(
        anOrderBalance({
          outstanding,
          invoices: [
            {
              invoiceId: anInvoicePage().invoices[0]!.invoiceId,
              invoiceNumber: 'I-CBE01-2627-000731',
              status: 'Posted',
              charges: 609,
              credits: 0,
              debits: 0,
              allocated: 609 - outstanding,
              refunds: 0,
              outstanding,
              currency: 'INR',
            },
          ],
        }),
      ),
    )
    transport.route(`GET ${MODES}`, () => jsonResponse(anAvailablePaymentModeList()))
    transport.route(`POST ${PAYMENTS}`, () => {
      outstanding = 0
      return jsonResponse(
        aPayment({ amount: 309, receipt: { ...aPayment().receipt!, orderOutstanding: 0 } }),
        201,
      )
    })

    // Phase 1: the order is unpaid, seen from the branch's outstanding-balances list.
    const outstandingScreen = renderApp('/billing/outstanding')
    await screen.findByText('O-CBE01-2627-000512')
    expect(screen.getByText(formatters.formatMoney(309))).toBeInTheDocument()

    await user.click(
      screen.getByRole('link', { name: 'Take payment for order O-CBE01-2627-000512' }),
    )

    // Phase 2: paying it off in full, at the counter.
    await user.click(
      await screen.findByRole('button', { name: `Full balance (${formatters.formatMoney(309)})` }),
    )
    await user.click(screen.getByRole('radio', { name: 'Cash' }))
    await user.click(screen.getByRole('button', { name: 'Record payment' }))
    const paymentDialog = await screen.findByRole('dialog')
    await user.click(within(paymentDialog).getByRole('button', { name: 'Record payment' }))
    await screen.findByText('Payment recorded')

    const [payment] = transport.callsTo(`POST ${PAYMENTS}`)
    expect(payment?.body).toMatchObject({ orderId: ORDER_ID, modeCode: 'CASH', amount: 309 })
    outstandingScreen.unmount()

    // Phase 3: the order is now paid — the Owner still approves a dispatch exception, which is a
    // policy act independent of today's balance (a small allowance against a future adjustment),
    // and it needs a fresh proof of identity.
    let dispatchAttempts = 0
    transport.route(`POST ${DISPATCH}`, () => {
      dispatchAttempts += 1
      return dispatchAttempts === 1
        ? problemResponse(403, 'security.step-up-required')
        : jsonResponse(
            {
              id: '0199dd00-0000-7000-8000-000000009001',
              branchId: BRANCH_ID,
              orderId: ORDER_ID,
              jobIds: ['0199dd00-0000-7000-8000-000000000e2'],
              maxOutstandingAmount: 50,
              currency: 'INR',
              policyVersion: 'W/"1"',
              reasonCode: 'CUSTOMER_TRAVELLING',
              approvedBy: CASHIER_ID,
              approvedAt: '2026-09-12T12:00:00.000Z',
              expiresAt: '2026-09-15T12:00:00.000Z',
              status: 'Active',
            },
            201,
          )
    })
    transport.route('POST /api/v1/auth/login', () => jsonResponse(aSignInResult()))
    transport.route(`GET ${dispatchBalanceUrl(ORDER_ID)}`, () =>
      jsonResponse(anOrderBalance({ outstanding, invoices: [] })),
    )

    renderApp(`/billing/dispatch-exceptions/new?orderId=${ORDER_ID}`)

    // The order now reads as paid — the balance this screen shows is zero.
    await screen.findByText(`${formatters.formatMoney(0)} outstanding on this order`)

    await user.type(screen.getByLabelText('Garment jobs'), '0199dd00-0000-7000-8000-000000000e2')
    await user.clear(screen.getByLabelText('Maximum outstanding allowed'))
    await user.type(screen.getByLabelText('Maximum outstanding allowed'), '50')
    await user.type(screen.getByLabelText('Reason code'), 'CUSTOMER_TRAVELLING')
    await user.type(
      screen.getByLabelText('Reason'),
      'Customer is travelling; small adjustment expected on return.',
    )
    await user.click(screen.getByRole('button', { name: 'Approve exception' }))

    const dispatchDialog = await screen.findByRole('dialog')
    await user.click(within(dispatchDialog).getByRole('button', { name: 'Approve exception' }))

    // The step-up dialog: signing back in replays the same approval.
    const identityDialog = await screen.findByRole('dialog', { name: 'Confirm it is you' })
    await user.type(within(identityDialog).getByLabelText('Password'), 'synthetic-password')
    await user.click(within(identityDialog).getByRole('button', { name: 'Confirm' }))

    await waitFor(() => expect(transport.callsTo(`POST ${DISPATCH}`)).toHaveLength(2))
    expect(await screen.findByText('Exception approved')).toBeInTheDocument()

    const [first, second] = transport.callsTo(`POST ${DISPATCH}`)
    expect(first?.body).toEqual(second?.body)
    expect(first?.headers.get('Idempotency-Key')).toBe(second?.headers.get('Idempotency-Key'))
    // Two screens, a full form, two dialogs and an identity challenge in one test comfortably
    // clear the default 5 s budget under a loaded runner, the same way the offline test in
    // MeasurementScreens.test.tsx does.
  }, 15_000)
})
