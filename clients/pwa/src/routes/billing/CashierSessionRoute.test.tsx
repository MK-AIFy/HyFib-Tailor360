import { render, screen, within } from '@testing-library/react'
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
import {
  BRANCH_ID,
  CASHIER_ID,
  aCashierSession,
  anAvailablePaymentModeList,
} from '../../billing/testing/fixtures'
import { CashierSessionRoute } from './CashierSessionRoute'

let transport: FetchStub
const formatters = getFormatters()

const SESSIONS = '/api/v1/billing/cashier-sessions'
const MODES = '/api/v1/billing/payment-modes/available'

const COLLEAGUE_ID = '0199dd00-0000-7000-8000-00000000c9c9'
const COLLEAGUE_SESSION_ID = '0199dd00-0000-7000-8000-00000000c9d9'

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(
      aCurrentUser({
        branchId: BRANCH_ID,
        userId: CASHIER_ID,
        permissions: [BILLING_PERMISSIONS.cashierSession],
      }),
    ),
  )
  transport.route(`GET ${MODES}`, () => jsonResponse(anAvailablePaymentModeList()))
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
                    <RequirePermission permission={BILLING_PERMISSIONS.cashierSession}>
                      <CashierSessionRoute />
                    </RequirePermission>
                  }
                  path="/billing/cashier"
                />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('picking the signed-in cashier’s own open session', () => {
  it('matches the session by cashierId, not by which one the branch lists first', async () => {
    // `listCashierSessions` answers newest-first: the colleague's session — opened later — is
    // element zero, with the signed-in cashier's own, older session behind it. Picking index 0
    // unconditionally (the bug Codex flagged on PR #217) would show the colleague's session here.
    const colleagueSession = aCashierSession({
      id: COLLEAGUE_SESSION_ID,
      cashierId: COLLEAGUE_ID,
      openedAt: '2026-09-12T09:00:00.000Z',
    })
    const ownSession = aCashierSession({
      cashierId: CASHIER_ID,
      openedAt: '2026-09-12T03:30:00.000Z',
    })
    transport.route(`GET ${SESSIONS}?status=Open`, () =>
      jsonResponse([colleagueSession, ownSession]),
    )

    renderAt('/billing/cashier')

    expect(
      await screen.findByText(`Open since ${formatters.formatDateTime(ownSession.openedAt)}`),
    ).toBeInTheDocument()
    expect(
      screen.queryByText(`Open since ${formatters.formatDateTime(colleagueSession.openedAt)}`),
    ).not.toBeInTheDocument()
  })

  it('offers to open a session when only a colleague has one open, rather than showing theirs', async () => {
    const colleagueSession = aCashierSession({
      id: COLLEAGUE_SESSION_ID,
      cashierId: COLLEAGUE_ID,
    })
    transport.route(`GET ${SESSIONS}?status=Open`, () => jsonResponse([colleagueSession]))

    renderAt('/billing/cashier')

    expect(await screen.findByRole('heading', { name: 'Open a session' })).toBeInTheDocument()
    expect(screen.queryByText('Close the session')).not.toBeInTheDocument()
  })

  it('closes the signed-in cashier’s own session, never a colleague’s', async () => {
    const user = userEvent.setup()
    const colleagueSession = aCashierSession({
      id: COLLEAGUE_SESSION_ID,
      cashierId: COLLEAGUE_ID,
      openedAt: '2026-09-12T09:00:00.000Z',
    })
    const ownSession = aCashierSession({
      cashierId: CASHIER_ID,
      openedAt: '2026-09-12T03:30:00.000Z',
    })
    transport.route(`GET ${SESSIONS}?status=Open`, () =>
      jsonResponse([colleagueSession, ownSession]),
    )
    transport.route(`POST ${SESSIONS}/${ownSession.id}/close`, () =>
      jsonResponse(aCashierSession({ status: 'Closed', reconciliationBatch: null })),
    )

    renderAt('/billing/cashier')

    await screen.findByLabelText('₹10 notes')
    await user.click(screen.getByRole('button', { name: 'Close session' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Close session' }))

    expect(await screen.findByText('Session closed')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${SESSIONS}/${ownSession.id}/close`)).toHaveLength(1)
    expect(transport.callsTo(`POST ${SESSIONS}/${COLLEAGUE_SESSION_ID}/close`)).toHaveLength(0)
  })
})
