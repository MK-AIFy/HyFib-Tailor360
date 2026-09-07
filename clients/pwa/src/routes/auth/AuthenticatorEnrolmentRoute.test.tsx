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
  anEnrolment,
  jsonResponse,
  problemResponse,
  stubFetch,
} from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { AuthenticatorEnrolmentRoute } from './AuthenticatorEnrolmentRoute'

let transport: FetchStub

const CODES = ['4RJ2-8QKD', '9WTC-2MBE', 'H7XA-51PN', 'K3DV-QY68']

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
  transport.route('POST /api/v1/auth/mfa/enrol', () => jsonResponse(anEnrolment()))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderEnrolment() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/account/security/authenticator']}>
          <Routes>
            <Route
              element={<AuthenticatorEnrolmentRoute />}
              path="/account/security/authenticator"
            />
            <Route element={<p>Sign-in and security</p>} path="/account/security" />
            <Route element={<p>The shop workspace</p>} path="/" />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

async function startEnrolment(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole('button', { name: 'Start setting up' })
  await user.click(screen.getByRole('button', { name: 'Start setting up' }))
  await screen.findByLabelText(/Code from your authenticator/)
}

describe('setting up an authenticator', () => {
  it('does not ask the server for a secret until somebody asks for one', async () => {
    renderEnrolment()

    await screen.findByRole('button', { name: 'Start setting up' })
    expect(transport.callsTo('POST /api/v1/auth/mfa/enrol')).toHaveLength(0)
  })

  it('offers all three ways in, because on a shop floor one of them always fails', async () => {
    const user = userEvent.setup()
    renderEnrolment()
    await startEnrolment(user)

    // The QR code, for a second device with a camera.
    const qr = screen.getByRole('img')
    expect(qr).toHaveAccessibleName(/adds asha.counter at HyFib Tailor360/)
    // The link, for when the authenticator is on the same phone as this screen — a phone cannot
    // photograph itself.
    expect(screen.getByRole('link', { name: 'Open in my authenticator app' })).toHaveAttribute(
      'href',
      anEnrolment().otpAuthUri,
    )
    // The key, for a desktop browser whose authenticator will not scan an old monitor.
    expect(screen.getByText(anEnrolment().manualEntryKey)).toBeInTheDocument()
  })

  it('says what is on the screen before it shows it', async () => {
    const user = userEvent.setup()
    renderEnrolment()
    await startEnrolment(user)

    expect(screen.getByText(/Anybody who photographs it can sign in as you/)).toBeInTheDocument()
  })

  it('does not read the secret out as the picture, because that is not an alternative', async () => {
    const user = userEvent.setup()
    renderEnrolment()
    await startEnrolment(user)

    const name = screen.getByRole('img').getAttribute('aria-label') ?? ''
    // Reading a shared secret aloud to a room is not an accessible alternative to a picture of one.
    // The alternative is the setup key beside it, which a screen reader can spell out on request.
    expect(name).not.toContain(anEnrolment().manualEntryKey.replace(/ /g, ''))
    expect(name).not.toContain('otpauth://')
  })

  it('confirms with a code and then shows the recovery codes, once', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/mfa/enrol/confirm', () =>
      jsonResponse({ recoveryCodes: CODES }),
    )
    renderEnrolment()
    await startEnrolment(user)

    await user.type(screen.getByLabelText(/Code from your authenticator/), '123456')
    await user.click(screen.getByRole('button', { name: 'Confirm the authenticator' }))

    const list = await screen.findByRole('list', { name: 'Recovery codes' })
    expect(within(list).getAllByRole('listitem')).toHaveLength(CODES.length)
    expect(screen.getByText(/This is the only time these are shown/)).toBeInTheDocument()
    // The secret has done its job and is off the screen before the codes are painted.
    expect(screen.queryByText(anEnrolment().manualEntryKey)).not.toBeInTheDocument()
  })

  it('will not let somebody finish until they say they have kept the codes', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/mfa/enrol/confirm', () =>
      jsonResponse({ recoveryCodes: CODES }),
    )
    renderEnrolment()
    await startEnrolment(user)
    await user.type(screen.getByLabelText(/Code from your authenticator/), '123456')
    await user.click(screen.getByRole('button', { name: 'Confirm the authenticator' }))
    await screen.findByRole('list', { name: 'Recovery codes' })

    await user.click(screen.getByRole('button', { name: 'Finish' }))
    expect(screen.getByText(/Confirm that you have kept the codes/)).toBeInTheDocument()

    await user.click(screen.getByLabelText(/I have written these down or printed them/))
    await user.click(screen.getByRole('button', { name: 'Finish' }))

    expect(await screen.findByText('Sign-in and security')).toBeInTheDocument()
  })

  it('says a wrong code is wrong without saying how the clock is out', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/mfa/enrol/confirm', () =>
      problemResponse(403, 'identity.mfa-code-invalid'),
    )
    renderEnrolment()
    await startEnrolment(user)

    await user.type(screen.getByLabelText(/Code from your authenticator/), '000000')
    await user.click(screen.getByRole('button', { name: 'Confirm the authenticator' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That code is not right')
  })

  it('writes nothing, anywhere, at any point in the whole enrolment', async () => {
    const user = userEvent.setup()
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    transport.route('POST /api/v1/auth/mfa/enrol/confirm', () =>
      jsonResponse({ recoveryCodes: CODES }),
    )
    renderEnrolment()
    await startEnrolment(user)
    await user.type(screen.getByLabelText(/Code from your authenticator/), '123456')
    await user.click(screen.getByRole('button', { name: 'Confirm the authenticator' }))
    await screen.findByRole('list', { name: 'Recovery codes' })

    // Neither the authenticator secret nor the recovery codes reach any storage, and neither is put
    // in a URL where it would land in browser history and in every log that records one.
    expect(writes).not.toHaveBeenCalled()
    for (const call of transport.calls) {
      expect(call.path).not.toContain('secret')
      expect(call.path).not.toContain(CODES[0])
    }
  })

  it('has no accessibility violations axe can see, with the code on screen', async () => {
    const user = userEvent.setup()
    const { container } = renderEnrolment()
    await startEnrolment(user)

    await expectNoAccessibilityViolations(container)
  })
})
