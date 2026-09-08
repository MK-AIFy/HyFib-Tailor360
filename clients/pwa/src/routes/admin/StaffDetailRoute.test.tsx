import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { aStaffUser, versionedResponse } from '../../admin/testing/fixtures'
import { StaffDetailRoute } from './StaffDetailRoute'

let transport: FetchStub

const SUBJECT = aStaffUser()
const READ = `GET /api/v1/admin/users/${SUBJECT.userId}`
const SUSPEND = `POST /api/v1/admin/users/${SUBJECT.userId}/suspend`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
  transport.route(READ, () => versionedResponse(SUBJECT, SUBJECT.version))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

/**
 * Rendered inside `RequireSession`, as the router does. The screen reads the signed-in account
 * without checking for one — the self-administration guard needs it — and it is `RequireSession`
 * that guarantees there is one to read.
 */
function renderAt(userId: string) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/admin/users/${userId}`]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route element={<StaffDetailRoute />} path="/admin/users/:userId" />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function renderDetail() {
  return renderAt(SUBJECT.userId)
}

/** Opens a command's confirmation and answers it with a reason. */
async function confirm(user: ReturnType<typeof userEvent.setup>, command: string, reason: string) {
  await user.click(screen.getByRole('button', { name: command }))
  await user.type(await screen.findByRole('textbox'), reason)

  const dialog = await screen.findByRole('dialog')
  const confirmButton = Array.from(dialog.querySelectorAll('button')).find(
    (candidate) => candidate.textContent === command,
  )

  await user.click(confirmButton as HTMLButtonElement)
}

it('says what suspending does to the person before the control is pressed', async () => {
  const user = userEvent.setup()
  renderDetail()

  await screen.findByRole('heading', { name: 'Meera (counter)' })
  await user.click(screen.getByRole('button', { name: 'Suspend' }))

  // Not "the status will change to Suspended" — what an administrator is weighing is what happens
  // to the person standing at the till.
  expect(await screen.findByText(/signed out of every device immediately/)).toBeInTheDocument()
})

it('sends the reason and the version it read, and reads the account again afterwards', async () => {
  const user = userEvent.setup()
  transport.route(SUSPEND, () =>
    versionedResponse({ ...SUBJECT, status: 'Suspended', version: 'W/"2"' }, 'W/"2"'),
  )

  renderDetail()
  await screen.findByRole('heading', { name: 'Meera (counter)' })

  await confirm(user, 'Suspend', 'Left the company on 5 September.')

  const sent = transport.callsTo(SUSPEND)[0]
  if (sent === undefined) {
    throw new Error('The suspend command was never sent.')
  }

  expect(sent.body).toEqual({ reason: 'Left the company on 5 September.' })

  // The version the screen was showing, presented back. Without it the server cannot tell this
  // administrator's decision from one made against a row that has since moved.
  expect(sent.headers.get('If-Match')).toBe(SUBJECT.version)
  expect(sent.headers.get('Idempotency-Key')).not.toBeNull()

  expect(await screen.findByText('Meera (counter) — done.')).toBeInTheDocument()
  expect(transport.callsTo(READ).length).toBeGreaterThan(1)
})

it('reuses one retry key across a retry of the same command', async () => {
  const user = userEvent.setup()
  transport.route(SUSPEND, () => problemResponse(503, 'platform.unavailable'))

  renderDetail()
  await screen.findByRole('heading', { name: 'Meera (counter)' })

  await confirm(user, 'Suspend', 'Left the company on 5 September.')
  await screen.findByRole('alert')

  const first = transport.callsTo(SUSPEND)[0]?.headers.get('Idempotency-Key')
  expect(first).not.toBeNull()

  // A second, separate decision gets its own key: the guarantee is that a *retry* replays, not that
  // two suspensions collapse into one.
  await confirm(user, 'Suspend', 'Trying again.')

  const second = transport.callsTo(SUSPEND)[1]?.headers.get('Idempotency-Key')
  expect(second).not.toBe(first)
})

it('explains a lost race rather than reporting a failure', async () => {
  const user = userEvent.setup()
  transport.route(SUSPEND, () => problemResponse(409, 'identity.version-conflict'))

  renderDetail()
  await screen.findByRole('heading', { name: 'Meera (counter)' })

  await confirm(user, 'Suspend', 'Left the company on 5 September.')

  expect(await screen.findByText(/edited while you had it open/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
})

it('offers no commands on the administrator’s own account, and says why', async () => {
  const self = aStaffUser({ userId: aCurrentUser().userId, displayName: 'Asha (counter)' })
  transport.route(`GET /api/v1/admin/users/${self.userId}`, () =>
    versionedResponse(self, self.version),
  )

  renderAt(self.userId)

  expect(await screen.findByText(/This is your own account/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Suspend' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Close account' })).not.toBeInTheDocument()
})

it('keeps closing an account away from lifting a suspension', async () => {
  renderDetail()
  await screen.findByRole('heading', { name: 'Meera (counter)' })

  // Adjacency is the hazard: the hand that reaches for "lift suspension" at the end of a shift must
  // not find "close account". They are in separate groups, and the destructive one is second.
  const suspend = screen.getByRole('button', { name: 'Suspend' })
  const close = screen.getByRole('button', { name: 'Close account' })

  expect(suspend.closest('.admin__actions')).not.toBe(close.closest('.admin__actions'))
  expect(close.closest('.admin__actions--destructive')).not.toBeNull()
})

it('has no accessibility violations', async () => {
  const { container } = renderDetail()

  await screen.findByRole('heading', { name: 'Meera (counter)' })

  await expectNoAccessibilityViolations(container)
})
