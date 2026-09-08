import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import type { ReactElement } from 'react'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import {
  aBranch,
  aDeadLetter,
  aFeatureFlag,
  aPermission,
  aRole,
  versionedResponse,
} from '../../admin/testing/fixtures'
import { AuditTrailRoute } from './AuditTrailRoute'
import { BranchListRoute } from './BranchListRoute'
import { FeatureFlagRoute } from './FeatureFlagRoute'
import { OutboxRoute } from './OutboxRoute'
import { RoleDetailRoute } from './RoleDetailRoute'

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderScreen(element: ReactElement, path = '/admin', entry = '/admin') {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[entry]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route element={element} path={path} />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/** Answers the confirmation a command raises, with a reason. */
async function confirmWith(
  user: ReturnType<typeof userEvent.setup>,
  confirmLabel: string,
  reason: string,
) {
  await user.type(await screen.findByRole('textbox'), reason)

  const dialog = await screen.findByRole('dialog')
  const button = Array.from(dialog.querySelectorAll('button')).find(
    (candidate) => candidate.textContent === confirmLabel,
  )

  await user.click(button as HTMLButtonElement)
}

describe('branches', () => {
  const OPEN = aBranch()
  const CLOSED = aBranch({
    branchId: '0199bb00-0000-7000-8000-0000000000ab',
    code: 'ERD01',
    name: 'Erode counter',
    status: 'Closed',
  })

  beforeEach(() => {
    transport.route('GET /api/v1/admin/branches/', () => jsonResponse([OPEN, CLOSED]))
  })

  it('offers closing a trading branch and reopening a closed one, never deleting either', async () => {
    renderScreen(<BranchListRoute />)

    const table = within(await screen.findByRole('table'))
    expect(table.getByText('Coimbatore counter')).toBeInTheDocument()

    // No delete, anywhere: a branch code is in every document number it ever produced.
    expect(screen.queryByRole('button', { name: /delete/i })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close branch' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reopen branch' })).toBeInTheDocument()
  })

  it('reads the branch again before closing it, so the version is the one that is true now', async () => {
    const user = userEvent.setup()
    const moved = aBranch({ version: 'W/"9"' })

    transport.route(`GET /api/v1/admin/branches/${OPEN.branchId}`, () =>
      versionedResponse(moved, moved.version),
    )
    transport.route(`POST /api/v1/admin/branches/${OPEN.branchId}/close`, () =>
      versionedResponse({ ...OPEN, status: 'Closed' }, 'W/"10"'),
    )

    renderScreen(<BranchListRoute />)
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: 'Close branch' }))
    await confirmWith(user, 'Close branch', 'The lease ended on 30 September.')

    const sent = transport.callsTo(`POST /api/v1/admin/branches/${OPEN.branchId}/close`)[0]
    if (sent === undefined) {
      throw new Error('The close command was never sent.')
    }

    // The version from the fresh read, not the one the table had been showing.
    expect(sent.headers.get('If-Match')).toBe('W/"9"')
  })

  it('says the branch is refused while people still work there', async () => {
    const user = userEvent.setup()
    transport.route(`GET /api/v1/admin/branches/${OPEN.branchId}`, () =>
      versionedResponse(OPEN, OPEN.version),
    )
    transport.route(`POST /api/v1/admin/branches/${OPEN.branchId}/close`, () =>
      problemResponse(409, 'identity.branch-still-in-use'),
    )

    renderScreen(<BranchListRoute />)
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: 'Close branch' }))
    await confirmWith(user, 'Close branch', 'The lease ended on 30 September.')

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderScreen(<BranchListRoute />)
    await screen.findByRole('table')
    await expectNoAccessibilityViolations(container)
  })
})

describe('roles', () => {
  const ROLE = aRole()
  const STEP_UP = aPermission({
    key: 'admin.roles',
    description: 'Edit roles and what each one allows.',
    module: 'Identity',
    scope: 'Organisation',
    requiresMfa: true,
    requiresStepUp: true,
    requiresReason: true,
  })

  beforeEach(() => {
    transport.route(`GET /api/v1/admin/roles/${ROLE.roleId}`, () =>
      versionedResponse(ROLE, ROLE.version),
    )
    transport.route('GET /api/v1/admin/permissions/', () => jsonResponse([aPermission(), STEP_UP]))
  })

  function renderRole() {
    return renderScreen(<RoleDetailRoute />, '/admin/roles/:roleId', `/admin/roles/${ROLE.roleId}`)
  }

  it('shows what each permission costs whoever holds the role, in words', async () => {
    renderRole()

    await screen.findByRole('heading', { name: 'Reception' })

    // The flags are the consequence of the tick. A list of dotted keys would hide the fact that
    // everybody holding this role gets asked to re-authenticate mid-task.
    expect(screen.getByText(/Needs a fresh check of identity each time/)).toBeInTheDocument()
    expect(screen.getByText(/Needs a written reason each time/)).toBeInTheDocument()
    expect(screen.getByText(/Needs a second factor/)).toBeInTheDocument()
  })

  it('sends the whole set the boxes show, not just what changed', async () => {
    const user = userEvent.setup()
    transport.route(`PUT /api/v1/admin/roles/${ROLE.roleId}/permissions`, () =>
      versionedResponse({ ...ROLE, permissionKeys: ['orders.read', 'admin.roles'] }, 'W/"2"'),
    )

    renderRole()
    await screen.findByRole('heading', { name: 'Reception' })

    await user.click(screen.getByRole('checkbox', { name: /Edit roles and what each one allows/ }))
    await user.type(
      screen.getByRole('textbox', { name: /Why are you changing what this role allows/ }),
      'Approved at the September operations review.',
    )
    await user.click(screen.getByRole('button', { name: 'Save permissions' }))

    const sent = transport.callsTo(`PUT /api/v1/admin/roles/${ROLE.roleId}/permissions`)[0]
    if (sent === undefined) {
      throw new Error('The permission replacement was never sent.')
    }

    expect(sent.body).toEqual({
      permissionKeys: ['orders.read', 'admin.roles'],
      reason: 'Approved at the September operations review.',
    })
    expect(sent.headers.get('If-Match')).toBe(ROLE.version)
  })

  it('explains a grant the administrator does not hold themselves', async () => {
    const user = userEvent.setup()
    transport.route(`PUT /api/v1/admin/roles/${ROLE.roleId}/permissions`, () =>
      problemResponse(403, 'identity.permission-not-held-by-granter'),
    )

    renderRole()
    await screen.findByRole('heading', { name: 'Reception' })

    await user.click(screen.getByRole('checkbox', { name: /Edit roles and what each one allows/ }))
    await user.type(
      screen.getByRole('textbox', { name: /Why are you changing what this role allows/ }),
      'Trying to give myself this.',
    )
    await user.click(screen.getByRole('button', { name: 'Save permissions' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderRole()
    await screen.findByRole('heading', { name: 'Reception' })
    await expectNoAccessibilityViolations(container)
  })
})

describe('feature settings', () => {
  const FLAG = aFeatureFlag()

  beforeEach(() => {
    transport.route('GET /api/v1/admin/feature-flags/', () => jsonResponse([FLAG]))
  })

  it('tells the operator how long the tills take to agree, before they change it twice', async () => {
    const user = userEvent.setup()
    renderScreen(<FeatureFlagRoute />)

    await screen.findByRole('table')
    await user.click(screen.getByRole('button', { name: 'Turn on' }))

    expect(await screen.findByText(/within about 30 seconds/)).toBeInTheDocument()
  })

  it('sends the version it read, so a flag edited elsewhere is refused rather than overwritten', async () => {
    const user = userEvent.setup()
    transport.route(`PUT /api/v1/admin/feature-flags/${FLAG.key}`, () =>
      versionedResponse({ ...FLAG, enabled: true }, 'W/"3"'),
    )

    renderScreen(<FeatureFlagRoute />)
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: 'Turn on' }))
    await confirmWith(user, 'Turn on', 'Enabling the pilot for the Madurai counter from Monday.')

    const sent = transport.callsTo(`PUT /api/v1/admin/feature-flags/${FLAG.key}`)[0]
    if (sent === undefined) {
      throw new Error('The flag change was never sent.')
    }

    expect(sent.body).toEqual({
      enabled: true,
      reason: 'Enabling the pilot for the Madurai counter from Monday.',
    })
    expect(sent.headers.get('If-Match')).toBe(FLAG.version)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderScreen(<FeatureFlagRoute />)
    await screen.findByRole('table')
    await expectNoAccessibilityViolations(container)
  })
})

describe('the audit trail', () => {
  it('shows who did what and why, with the states behind a disclosure', async () => {
    transport.route('GET /api/v1/admin/audit/', () =>
      jsonResponse({
        entries: [
          {
            sequence: 42,
            occurredAt: '2026-09-06T09:00:00.000Z',
            action: 'identity.user.suspended',
            entityType: 'StaffUser',
            entityId: '0199bb00-0000-7000-8000-000000000001',
            actorId: '0199bb00-0000-7000-8000-000000000009',
            actorDisplayName: 'Asha (counter)',
            branchId: null,
            correlationId: 'req-synthetic-0007',
            reason: 'Left the company on 5 September.',
            summary: 'The account was suspended.',
            before: '{"Status":"Active"}',
            after: '{"Status":"Suspended"}',
          },
        ],
        nextCursor: null,
      }),
    )

    renderScreen(<AuditTrailRoute />)

    expect(await screen.findByText('The account was suspended.')).toBeInTheDocument()
    expect(screen.getByText('Left the company on 5 September.')).toBeInTheDocument()
    expect(screen.getByText(/Asha \(counter\)/)).toBeInTheDocument()

    // The evidence is one press away rather than in the row, because it is wanted for one entry in
    // fifty and putting it everywhere buries the forty-nine.
    const disclosure = screen.getByRole('group')
    expect(within(disclosure).getByText('{"Status":"Active"}')).toBeInTheDocument()
  })

  it('says so plainly when nothing matches', async () => {
    transport.route('GET /api/v1/admin/audit/', () =>
      jsonResponse({ entries: [], nextCursor: null }),
    )

    renderScreen(<AuditTrailRoute />)

    expect(await screen.findByText('Nothing matches what you are looking for.')).toBeInTheDocument()
  })
})

describe('failed messages', () => {
  const MESSAGE = aDeadLetter()

  beforeEach(() => {
    transport.route('GET /api/v1/admin/outbox/dead-letters', () => jsonResponse([MESSAGE]))
  })

  it('warns that sending again may deliver twice, and offers no drain-everything', async () => {
    const user = userEvent.setup()
    renderScreen(<OutboxRoute />)

    await screen.findByRole('table')

    // The console keeps the drain: one operator's "everything" is another's duplicate storm.
    expect(screen.queryByRole('button', { name: /all|everything/i })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Send again' }))

    expect(await screen.findByText(/whoever receives it gets it twice/)).toBeInTheDocument()
  })

  it('says a message somebody else already replayed is no longer waiting', async () => {
    const user = userEvent.setup()
    transport.route(`POST /api/v1/admin/outbox/${MESSAGE.id}/replay`, () =>
      problemResponse(404, 'platform.outbox-message-not-dead-lettered'),
    )

    renderScreen(<OutboxRoute />)
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: 'Send again' }))
    await confirmWith(user, 'Send again', 'The provider outage was resolved at 09:40.')

    // Not "not found", which reads as a fault in the application rather than as the fact it is.
    expect(await screen.findByText(/no longer waiting/)).toBeInTheDocument()
  })

  it('reports nothing failing as good news rather than as an empty table', async () => {
    transport.route('GET /api/v1/admin/outbox/dead-letters', () => jsonResponse([]))

    renderScreen(<OutboxRoute />)

    expect(
      await screen.findByText(/Everything the shop has published has been delivered/),
    ).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderScreen(<OutboxRoute />)
    await screen.findByRole('table')
    await expectNoAccessibilityViolations(container)
  })
})
