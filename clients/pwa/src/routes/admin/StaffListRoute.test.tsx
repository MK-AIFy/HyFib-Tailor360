import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { aStaffSummary } from '../../admin/testing/fixtures'
import { StaffListRoute } from './StaffListRoute'

let transport: FetchStub

const ACTIVE = aStaffSummary()
const SUSPENDED = aStaffSummary({
  userId: '0199bb00-0000-7000-8000-000000000002',
  displayName: 'Ravi (workshop)',
  userName: 'ravi.workshop',
  status: 'Suspended',
  roleKeys: ['tailor'],
  lastSignInAt: null,
})

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
  transport.route('GET /api/v1/admin/users/', () =>
    jsonResponse({ users: [ACTIVE, SUSPENDED], nextCursor: null }),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderList() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/admin/users']}>
          <StaffListRoute />
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

it('lists every account with a status that is a word and not only a colour', async () => {
  renderList()

  expect(await screen.findByRole('link', { name: 'Meera (counter)' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Ravi (workshop)' })).toBeInTheDocument()

  // The rule that matters most in a table of people: a suspended account has to say "Suspended",
  // not merely be tinted differently from an active one. Scoped to the table, because the status
  // filter beside it offers the same four words and matching either would prove nothing.
  const table = within(screen.getByRole('table'))
  expect(table.getByText('Suspended')).toBeInTheDocument()
  expect(table.getByText('Active')).toBeInTheDocument()

  // An account nobody has signed into says so, rather than showing an empty cell somebody has to
  // interpret.
  expect(table.getByText('Never')).toBeInTheDocument()
})

it('says that the search matches names only, before anybody types a phone number into it', async () => {
  renderList()

  await screen.findByRole('link', { name: 'Meera (counter)' })

  const box = screen.getByRole('searchbox', { name: 'Search by name' })
  const hint = screen.getByText(/Matches names only/)

  expect(hint).toBeInTheDocument()
  expect(box.getAttribute('aria-describedby')).toBe(hint.id)
})

it('asks the server again only when the search is submitted', async () => {
  const user = userEvent.setup()
  renderList()

  await screen.findByRole('link', { name: 'Meera (counter)' })
  expect(transport.callsTo('GET /api/v1/admin/users/')).toHaveLength(1)

  await user.type(screen.getByRole('searchbox', { name: 'Search by name' }), 'ravi')

  // Typing is not a query. A request per keystroke would be a request per keystroke against an
  // endpoint that is rate-limited, and the person has not finished saying what they want.
  expect(transport.callsTo('GET /api/v1/admin/users/')).toHaveLength(1)

  transport.route('GET /api/v1/admin/users/?search=ravi', () =>
    jsonResponse({ users: [SUSPENDED], nextCursor: null }),
  )

  await user.click(screen.getByRole('button', { name: 'Search by name' }))

  expect(await screen.findByRole('link', { name: 'Ravi (workshop)' })).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: 'Meera (counter)' })).not.toBeInTheDocument()
})

it('shows an empty state rather than an empty table when nothing matches', async () => {
  transport.route('GET /api/v1/admin/users/', () => jsonResponse({ users: [], nextCursor: null }))

  renderList()

  expect(
    await screen.findByText('No account matches what you are looking for.'),
  ).toBeInTheDocument()
})

it('explains a refusal instead of showing a blank screen', async () => {
  transport.route('GET /api/v1/admin/users/', () =>
    problemResponse(403, 'security.permission-denied'),
  )

  renderList()

  expect(await screen.findByRole('alert')).toBeInTheDocument()
})

it('has no accessibility violations', async () => {
  const { container } = renderList()

  await screen.findByRole('link', { name: 'Meera (counter)' })

  await expectNoAccessibilityViolations(container)
})
