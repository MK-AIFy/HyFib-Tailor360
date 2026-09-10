import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactElement } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AppIntlProvider } from '../../i18n/IntlProvider'
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
import {
  aBranch,
  aDeadLetter,
  aFeatureFlag,
  aRole,
  aStaffUser,
  versionedResponse,
} from '../../admin/testing/fixtures'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { BranchListRoute } from './BranchListRoute'
import { FeatureFlagRoute } from './FeatureFlagRoute'
import { OutboxRoute } from './OutboxRoute'
import { RoleDetailRoute } from './RoleDetailRoute'
import { StaffDetailRoute } from './StaffDetailRoute'

let transport: FetchStub
const branch = aBranch()
const flag = aFeatureFlag()
const role = aRole()
const staff = aStaffUser()
const message = aDeadLetter()
const reason = 'Approved synthetic administration change.'
const refused = () => problemResponse(403, 'security.step-up-required')

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
  transport.route('POST /api/v1/auth/login', () => jsonResponse(aSignInResult()))
  transport.route('GET /api/v1/admin/branches/', () => jsonResponse([branch]))
  transport.route(`GET /api/v1/admin/branches/${branch.branchId}`, () =>
    versionedResponse(branch, branch.version),
  )
  transport.route('GET /api/v1/admin/feature-flags/', () => jsonResponse([flag]))
  transport.route(`GET /api/v1/admin/roles/${role.roleId}`, () =>
    versionedResponse(role, role.version),
  )
  transport.route('GET /api/v1/admin/permissions/', () => jsonResponse([]))
  transport.route(`GET /api/v1/admin/users/${staff.userId}`, () =>
    versionedResponse(staff, staff.version),
  )
  transport.route('GET /api/v1/admin/outbox/dead-letters', () => jsonResponse([message]))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
})

interface Scenario {
  readonly label: string
  readonly command: string
  readonly element: ReactElement
  readonly path: string
  readonly entry: string
  readonly response: unknown
  readonly version?: string
}

const scenarios: readonly Scenario[] = [
  {
    label: 'Close branch',
    command: `POST /api/v1/admin/branches/${branch.branchId}/close`,
    element: <BranchListRoute />,
    path: '/admin',
    entry: '/admin',
    response: branch,
    version: branch.version,
  },
  {
    label: 'Turn on',
    command: `PUT /api/v1/admin/feature-flags/${flag.key}`,
    element: <FeatureFlagRoute />,
    path: '/admin',
    entry: '/admin',
    response: flag,
    version: flag.version,
  },
  {
    label: 'Send again',
    command: `POST /api/v1/admin/outbox/${message.id}/replay`,
    element: <OutboxRoute />,
    path: '/admin',
    entry: '/admin',
    response: {},
  },
  {
    label: 'Suspend',
    command: `POST /api/v1/admin/users/${staff.userId}/suspend`,
    element: <StaffDetailRoute />,
    path: '/admin/users/:userId',
    entry: `/admin/users/${staff.userId}`,
    response: staff,
    version: staff.version,
  },
  {
    label: 'Save permissions',
    command: `PUT /api/v1/admin/roles/${role.roleId}/permissions`,
    element: <RoleDetailRoute />,
    path: '/admin/roles/:roleId',
    entry: `/admin/roles/${role.roleId}`,
    response: role,
    version: role.version,
  },
]

async function start(scenario: Scenario, user: ReturnType<typeof userEvent.setup>) {
  render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[scenario.entry]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route element={scenario.element} path={scenario.path} />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
  const action = await screen.findByRole('button', { name: scenario.label })
  if (scenario.label === 'Save permissions') {
    await user.type(screen.getByRole('textbox'), reason)
    await user.click(action)
  } else {
    await user.click(action)
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox'), reason)
    await user.click(within(dialog).getByRole('button', { name: scenario.label }))
  }
  return await screen.findByRole('dialog', { name: 'Confirm it is you' })
}

it.each(scenarios)(
  'recovers $label through the shared identity dialog with the exact decision',
  async (scenario) => {
    const user = userEvent.setup()
    let attempts = 0
    transport.route(scenario.command, () =>
      ++attempts === 1 ? refused() : versionedResponse(scenario.response, 'W/"2"'),
    )
    const dialog = await start(scenario, user)
    expect(transport.callsTo(scenario.command)).toHaveLength(1)
    await expectNoAccessibilityViolations(dialog)
    await user.type(within(dialog).getByLabelText('Password'), 'synthetic-password')
    await user.click(within(dialog).getByRole('button', { name: 'Confirm' }))
    await waitFor(() => expect(transport.callsTo(scenario.command)).toHaveLength(2))
    const [first, second] = transport.callsTo(scenario.command)
    expect(first?.body).toMatchObject({ reason })
    expect(second?.body).toEqual(first?.body)
    expect(first?.headers.get('Idempotency-Key')).toBeTruthy()
    expect(second?.headers.get('Idempotency-Key')).toBe(first?.headers.get('Idempotency-Key'))
    if (scenario.version !== undefined) {
      expect(first?.headers.get('If-Match')).toBe(scenario.version)
      expect(second?.headers.get('If-Match')).toBe(scenario.version)
    }
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Confirm it is you' })).not.toBeInTheDocument(),
    )
  },
)

it('keeps role input and its retry key when the identity question is declined', async () => {
  const scenario = scenarios.find((item) => item.label === 'Save permissions')!
  const user = userEvent.setup()
  transport.route(scenario.command, refused)
  const dialog = await start(scenario, user)
  await user.click(within(dialog).getByRole('button', { name: 'Not now' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('fresh check')
  expect(screen.getByRole('textbox')).toHaveValue(reason)
  expect(transport.callsTo(scenario.command)).toHaveLength(1)
  await user.click(screen.getByRole('button', { name: 'Save permissions' }))
  await screen.findByRole('dialog', { name: 'Confirm it is you' })
  const [first, second] = transport.callsTo(scenario.command)
  expect(second?.headers.get('Idempotency-Key')).toBe(first?.headers.get('Idempotency-Key'))
  expect(second?.body).toEqual(first?.body)
})

it('shows a specific failure after a second refusal without opening another identity dialog', async () => {
  const scenario = scenarios.find((item) => item.label === 'Save permissions')!
  const user = userEvent.setup()
  transport.route(scenario.command, refused)
  const dialog = await start(scenario, user)
  await user.type(within(dialog).getByLabelText('Password'), 'synthetic-password')
  await user.click(within(dialog).getByRole('button', { name: 'Confirm' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('fresh check')
  expect(screen.queryByRole('dialog', { name: 'Confirm it is you' })).not.toBeInTheDocument()
  expect(transport.callsTo(scenario.command)).toHaveLength(2)
  expect(screen.getByRole('textbox')).toHaveValue(reason)
})
