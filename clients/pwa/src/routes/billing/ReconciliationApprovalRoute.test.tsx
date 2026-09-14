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
  CASHIER_SESSION_ID,
  aCashierSession,
  aReconciliationBatch,
} from '../../billing/testing/fixtures'
import { ReconciliationApprovalRoute } from './ReconciliationApprovalRoute'

let transport: FetchStub
const formatters = getFormatters()

const SESSION = `/api/v1/billing/cashier-sessions/${CASHIER_SESSION_ID}/reconciliation`
const APPROVE = `${SESSION}/approve`
const PATH = `/billing/cashier-sessions/${CASHIER_SESSION_ID}/reconciliation`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [BILLING_PERMISSIONS.approveReconciliation] })),
  )
  transport.route(`GET ${SESSION}`, () =>
    jsonResponse(
      aCashierSession({ status: 'Closed', reconciliationBatch: aReconciliationBatch() }),
    ),
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
                    <RequirePermission permission={BILLING_PERMISSIONS.approveReconciliation}>
                      <ReconciliationApprovalRoute />
                    </RequirePermission>
                  }
                  path="/billing/cashier-sessions/:sessionId/reconciliation"
                />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('approving a session’s variance', () => {
  it('states the variance by mode, then records what was approved', async () => {
    const user = userEvent.setup()
    transport.route(`POST ${APPROVE}`, () =>
      jsonResponse(
        aReconciliationBatch({ status: 'Approved', approvedAt: '2026-09-12T09:00:00.000Z' }),
      ),
    )

    renderAt(PATH)

    expect(
      await screen.findByText(`${formatters.formatMoney(-170)} away from what was expected`, {
        exact: false,
      }),
    ).toBeInTheDocument()
    expect(screen.getByText(`CASH: ${formatters.formatMoney(-170)}`)).toBeInTheDocument()
    expect(screen.getByText(`UPI: ${formatters.formatMoney(0)}`)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Approve variance' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(
      within(dialog).getByLabelText('Reason'),
      'Counted twice; confirmed with the cashier.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Approve variance' }))

    expect(await screen.findByText('Variance approved')).toBeInTheDocument()
    const [request] = transport.callsTo(`POST ${APPROVE}`)
    expect(request?.body).toMatchObject({ reason: 'Counted twice; confirmed with the cashier.' })
  })

  it('says there is nothing to approve once the batch is already settled', async () => {
    transport.route(`GET ${SESSION}`, () =>
      jsonResponse(
        aCashierSession({
          status: 'Closed',
          reconciliationBatch: aReconciliationBatch({
            status: 'Approved',
            approvedAt: '2026-09-12T09:00:00.000Z',
          }),
        }),
      ),
    )
    renderAt(PATH)

    expect(await screen.findByText('Nothing to approve')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve variance' })).not.toBeInTheDocument()
  })

  it('refuses a caller holding no approval permission before any request is made', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: [] })))
    renderAt(PATH)

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${SESSION}`)).toHaveLength(0)
  })
})

describe('accessibility', () => {
  it('has no violations when loaded, awaiting approval', async () => {
    const { container } = renderAt(PATH)
    await screen.findByRole('button', { name: 'Approve variance' })
    await expectNoAccessibilityViolations(container)
  })

  /** A batch already approved, or a session whose variance never crossed the threshold at all. */
  it('has no violations when nothing needs approval', async () => {
    transport.route(`GET ${SESSION}`, () =>
      jsonResponse(aCashierSession({ status: 'Closed', reconciliationBatch: null })),
    )
    const { container } = renderAt(PATH)
    await screen.findByText('Nothing to approve')
    await expectNoAccessibilityViolations(container)
  })

  it('has no violations when the session fails to load', async () => {
    transport.route(`GET ${SESSION}`, () => problemResponse(503, 'platform.unavailable'))
    const { container } = renderAt(PATH)
    await screen.findByRole('alert')
    await expectNoAccessibilityViolations(container)
  })

  it('has no violations while offline, with the batch still on screen', async () => {
    const { container } = renderAt(PATH)
    await screen.findByRole('button', { name: 'Approve variance' })

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText('Needs connection — this will not be queued'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve variance' })).not.toBeInTheDocument()
    await expectNoAccessibilityViolations(container)

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('has no violations when forbidden', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: [] })))
    const { container } = renderAt(PATH)
    await screen.findByText('You do not have access to this')
    await expectNoAccessibilityViolations(container)
  })
})
