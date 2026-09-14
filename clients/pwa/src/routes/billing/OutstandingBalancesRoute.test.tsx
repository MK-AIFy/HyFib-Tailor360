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
import { aCurrentUser, jsonResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { ShellStatusProvider } from '../../components/layout/ShellStatusProvider'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import { anOutstandingBalancePage, anOutstandingBalanceRow } from '../../billing/testing/fixtures'
import { OutstandingBalancesRoute } from './OutstandingBalancesRoute'

let transport: FetchStub

const OUTSTANDING = '/api/v1/billing/outstanding-balances'

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [BILLING_PERMISSIONS.createInvoice] })),
  )
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
          <MemoryRouter initialEntries={['/billing/outstanding']}>
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
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('the outstanding balances screen', () => {
  it('shows the first page, announces the count, and appends the second page on Show more', async () => {
    const user = userEvent.setup()
    const first = anOutstandingBalanceRow({ orderNumber: 'O-CBE01-2627-000512' })
    const second = anOutstandingBalanceRow({
      invoiceId: '0199dd00-0000-7000-8000-000000006001',
      orderNumber: 'O-CBE01-2627-000900',
      invoiceNumber: 'I-CBE01-2627-000900',
    })

    transport.route(`GET ${OUTSTANDING}?limit=20`, () =>
      jsonResponse(anOutstandingBalancePage({ rows: [first], nextCursor: 'cursor-2' })),
    )
    transport.route(`GET ${OUTSTANDING}?cursor=cursor-2&limit=20`, () =>
      jsonResponse(anOutstandingBalancePage({ rows: [second], nextCursor: null })),
    )

    renderAt()

    await screen.findByText('O-CBE01-2627-000512')
    expect(screen.getByRole('status')).toHaveTextContent('1 invoice outstanding')

    await user.click(await screen.findByRole('button', { name: 'Show more' }))

    await waitFor(() => {
      expect(screen.getByText('O-CBE01-2627-000900')).toBeInTheDocument()
    })
    // The first page's row is still there: Show more appends rather than replaces.
    expect(screen.getByText('O-CBE01-2627-000512')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('2 invoices outstanding')
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
  })

  it('shows the empty state only once the branch is exhausted with nothing outstanding', async () => {
    transport.route(`GET ${OUTSTANDING}?limit=20`, () =>
      jsonResponse(anOutstandingBalancePage({ rows: [], nextCursor: null })),
    )

    renderAt()

    expect(await screen.findByText('Nothing outstanding')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
  })

  it('keeps Show more rather than the empty state when a page finds nothing yet', async () => {
    // The scan bound's own exception (#421): a page can be empty and still carry a cursor, which
    // means "nothing found so far", not "nothing outstanding at this branch".
    transport.route(`GET ${OUTSTANDING}?limit=20`, () =>
      jsonResponse(anOutstandingBalancePage({ rows: [], nextCursor: 'cursor-2' })),
    )

    renderAt()

    await screen.findByRole('button', { name: 'Show more' })
    expect(screen.queryByText('Nothing outstanding')).not.toBeInTheDocument()
  })

  it('refuses a caller holding no billing permission before any request is made', async () => {
    renderAt([])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${OUTSTANDING}?limit=20`)).toHaveLength(0)
  })

  it('has no accessibility violations on the loaded state', async () => {
    transport.route(`GET ${OUTSTANDING}?limit=20`, () =>
      jsonResponse(anOutstandingBalancePage({ rows: [anOutstandingBalanceRow()] })),
    )
    const { container } = renderAt()
    await screen.findByText('O-CBE01-2627-000512')
    await expectNoAccessibilityViolations(container)
  })

  it('has no accessibility violations on the empty state', async () => {
    transport.route(`GET ${OUTSTANDING}?limit=20`, () =>
      jsonResponse(anOutstandingBalancePage({ rows: [], nextCursor: null })),
    )
    const { container } = renderAt()
    await screen.findByText('Nothing outstanding')
    await expectNoAccessibilityViolations(container)
  })
})
