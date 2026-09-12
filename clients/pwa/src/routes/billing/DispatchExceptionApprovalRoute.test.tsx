import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { getFormatters } from '../../i18n/formatters'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { ShellStatusProvider } from '../../components/layout/ShellStatusProvider'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import { ORDER_ID, anOrderBalance } from '../../billing/testing/fixtures'
import { DispatchExceptionApprovalRoute } from './DispatchExceptionApprovalRoute'

let transport: FetchStub
const formatters = getFormatters()

const balanceUrl = (orderId: string) => `/api/v1/billing/orders/${orderId}/balance`
const DISPATCH = '/api/v1/billing/dispatch-exceptions'
const OTHER_ORDER_ID = '0199dd00-0000-7000-8000-0000000da1da'

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [BILLING_PERMISSIONS.approveDispatchException] })),
  )
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

// Codex review, PR #217: switching the order reference used to leave `maxOutstandingOverride` (and
// the previous order's balance) in place, so a form prepared for order A could be submitted with
// A's allowance against order B — the approval endpoint does not cross-check the amount against the
// current balance, so the wrong dispatch threshold could be persisted.
describe('changing the order reference clears order-A-specific state', () => {
  it('resets the quick-filled maximum and hides the stale balance the moment the order changes', async () => {
    const user = userEvent.setup()
    // A plain `let` closed over inside the responder below defeats TypeScript's narrowing (it
    // proves the variable can never be reassigned before its later, optional call, which is wrong
    // at runtime); a mutable holder object sidesteps that.
    const otherBalance: { resolve: (() => void) | null } = { resolve: null }

    transport.route(`GET ${balanceUrl(ORDER_ID)}`, () =>
      jsonResponse(anOrderBalance({ orderId: ORDER_ID, outstanding: 500 })),
    )
    transport.route(`GET ${balanceUrl(OTHER_ORDER_ID)}`, () => {
      return new Promise<Response>((resolve) => {
        otherBalance.resolve = () => {
          resolve(jsonResponse(anOrderBalance({ orderId: OTHER_ORDER_ID, outstanding: 20 })))
        }
      })
    })

    renderAt(`/billing/dispatch-exceptions/new?orderId=${ORDER_ID}`)

    // Order A's balance loads and quick-fills the maximum.
    await screen.findByText(`${formatters.formatMoney(500)} outstanding on this order`)
    expect(screen.getByLabelText('Maximum outstanding allowed')).toHaveValue('500.00')

    // Switching to order B: A's quick-filled amount and A's balance must not survive the switch.
    // A single change (rather than typing character by character) is what a paste, or a barcode
    // scan into the field, looks like, and it avoids asserting on the transient garbage requests a
    // partially typed order reference would otherwise fire.
    const orderField = screen.getByLabelText('Order reference')
    fireEvent.change(orderField, { target: { value: OTHER_ORDER_ID } })

    expect(screen.getByLabelText('Maximum outstanding allowed')).toHaveValue('')
    expect(
      screen.queryByText(`${formatters.formatMoney(500)} outstanding on this order`),
    ).not.toBeInTheDocument()

    // Trying to submit while order B's balance is still in flight is refused rather than silently
    // carrying A's allowance forward.
    await user.type(screen.getByLabelText('Garment jobs'), 'job-1')
    await user.type(screen.getByLabelText('Reason code'), 'CUSTOMER_TRAVELLING')
    await user.type(screen.getByLabelText('Reason'), 'Waiting on a fresh balance for order B.')
    await user.click(screen.getByRole('button', { name: 'Approve exception' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByText('The amount must be more than zero.')).toBeInTheDocument()

    // Once order B's own balance lands, the maximum quick-fills again — to B's figure, not A's.
    otherBalance.resolve?.()
    await waitFor(() => {
      expect(screen.getByLabelText('Maximum outstanding allowed')).toHaveValue('20.00')
    })
    expect(
      await screen.findByText(`${formatters.formatMoney(20)} outstanding on this order`),
    ).toBeInTheDocument()
  })

  it('submits the current order’s balance as the maximum, not a stale one from a previous order', async () => {
    const user = userEvent.setup()

    transport.route(`GET ${balanceUrl(ORDER_ID)}`, () =>
      jsonResponse(anOrderBalance({ orderId: ORDER_ID, outstanding: 500 })),
    )
    transport.route(`GET ${balanceUrl(OTHER_ORDER_ID)}`, () =>
      jsonResponse(anOrderBalance({ orderId: OTHER_ORDER_ID, outstanding: 20 })),
    )
    transport.route(`POST ${DISPATCH}`, () =>
      jsonResponse(
        {
          id: '0199dd00-0000-7000-8000-000000009002',
          branchId: '0199dd00-0000-7000-8000-0000000000b1',
          orderId: OTHER_ORDER_ID,
          jobIds: ['job-1'],
          maxOutstandingAmount: 20,
          currency: 'INR',
          policyVersion: 'W/"1"',
          reasonCode: 'CUSTOMER_TRAVELLING',
          approvedBy: '0199aa00-0000-7000-8000-000000000001',
          approvedAt: '2026-09-12T12:00:00.000Z',
          expiresAt: '2026-09-15T12:00:00.000Z',
          status: 'Active',
        },
        201,
      ),
    )

    renderAt(`/billing/dispatch-exceptions/new?orderId=${ORDER_ID}`)

    await screen.findByText(`${formatters.formatMoney(500)} outstanding on this order`)

    const orderField = screen.getByLabelText('Order reference')
    fireEvent.change(orderField, { target: { value: OTHER_ORDER_ID } })
    await screen.findByText(`${formatters.formatMoney(20)} outstanding on this order`)

    await user.type(screen.getByLabelText('Garment jobs'), 'job-1')
    await user.type(screen.getByLabelText('Reason code'), 'CUSTOMER_TRAVELLING')
    await user.type(screen.getByLabelText('Reason'), 'Customer travelling; small allowance.')
    await user.click(screen.getByRole('button', { name: 'Approve exception' }))

    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Approve exception' }))

    expect(await screen.findByText('Exception approved')).toBeInTheDocument()
    const [request] = transport.callsTo(`POST ${DISPATCH}`)
    expect(request?.body).toMatchObject({ orderId: OTHER_ORDER_ID, maxOutstandingAmount: 20 })
  })
})
