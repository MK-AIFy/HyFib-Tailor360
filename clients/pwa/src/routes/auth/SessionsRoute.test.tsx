import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { SessionProvider } from '../../auth/SessionProvider'
import {
  aCurrentUser,
  aSessionDevice,
  jsonResponse,
  noContent,
  problemResponse,
  stubFetch,
} from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { SessionsRoute } from './SessionsRoute'

let transport: FetchStub

const THIS_DEVICE = aSessionDevice({
  sessionId: '0199aa00-0000-7000-8000-00000000000c',
  deviceLabel: 'Workshop tablet',
  isCurrent: true,
})

const OTHER_DEVICE = aSessionDevice({
  sessionId: '0199aa00-0000-7000-8000-00000000000d',
  deviceLabel: 'Unknown phone',
  ipAddress: '203.0.113.9',
  mfaSatisfied: false,
})

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
  transport.route('GET /api/v1/sessions', () => jsonResponse([THIS_DEVICE, OTHER_DEVICE]))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderSessions() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/account/sessions']}>
          <Routes>
            <Route element={<SessionsRoute />} path="/account/sessions" />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('the device inventory', () => {
  it('marks which one the person is using, so they do not sign the wrong one out', async () => {
    renderSessions()

    const list = await screen.findByRole('list', { name: 'Devices signed in' })
    const rows = within(list).getAllByRole('listitem')
    expect(rows[0]).toHaveTextContent('Workshop tablet — This device')
    expect(rows[1]).toHaveTextContent('Unknown phone')
  })

  it('shows where each one was last seen, which is the whole reason to look', async () => {
    renderSessions()

    expect(await screen.findByText(/From 203.0.113.9/)).toBeInTheDocument()
  })

  it('says whether a device proved a second factor, in words and not in colour', async () => {
    renderSessions()

    const list = await screen.findByRole('list', { name: 'Devices signed in' })
    expect(within(list).getByText('Confirmed with a second step')).toBeInTheDocument()
    expect(within(list).getByText('Password only')).toBeInTheDocument()
  })

  it('confirms before ending a device, and says who it affects', async () => {
    const user = userEvent.setup()
    transport.route(`DELETE /api/v1/sessions/${OTHER_DEVICE.sessionId}`, () => noContent())
    renderSessions()

    const list = await screen.findByRole('list', { name: 'Devices signed in' })
    const row = within(list).getAllByRole('listitem')[1]
    await user.click(
      within(row as HTMLElement).getByRole('button', { name: 'Sign this device out' }),
    )

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAccessibleName('Sign Unknown phone out?')
    expect(dialog).toHaveTextContent(/Anything they have typed and not saved is lost/)

    await user.click(within(dialog).getByRole('button', { name: 'Sign it out' }))

    expect(await screen.findByText('Unknown phone has been signed out.')).toBeInTheDocument()
    expect(transport.callsTo(`DELETE /api/v1/sessions/${OTHER_DEVICE.sessionId}`)).toHaveLength(1)
  })

  it('warns differently when the device being ended is this one', async () => {
    const user = userEvent.setup()
    renderSessions()

    const list = await screen.findByRole('list', { name: 'Devices signed in' })
    const row = within(list).getAllByRole('listitem')[0]
    await user.click(
      within(row as HTMLElement).getByRole('button', { name: 'Sign this device out' }),
    )

    expect(await screen.findByRole('dialog')).toHaveTextContent(/This is the device you are using/)
  })

  it('offers signing out everywhere, and says that it forgets remembered devices too', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/logout-all', () => jsonResponse({ sessionsEnded: 3 }))
    renderSessions()

    await screen.findByRole('list', { name: 'Devices signed in' })
    await user.click(screen.getByRole('button', { name: 'Sign out everywhere' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent(/every remembered device is forgotten/)

    await user.click(within(dialog).getByRole('button', { name: 'Sign out everywhere' }))

    expect(transport.callsTo('POST /api/v1/auth/logout-all')).toHaveLength(1)
  })

  it('describes a failed read in plain language rather than as a status code', async () => {
    transport.route('GET /api/v1/sessions', () => problemResponse(503, 'platform.unavailable'))
    renderSessions()

    expect(await screen.findByText(/The shop system could not finish this/)).toBeInTheDocument()
  })

  it('says so when the account is signed in nowhere else', async () => {
    transport.route('GET /api/v1/sessions', () => jsonResponse([THIS_DEVICE]))
    renderSessions()

    expect(await screen.findByText('Only this device')).toBeInTheDocument()
  })

  it('has no accessibility violations axe can see', async () => {
    const { container } = renderSessions()
    await screen.findByRole('list', { name: 'Devices signed in' })

    await expectNoAccessibilityViolations(container)
  })
})
