import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'
import {
  aCurrentUser,
  aPasskey,
  jsonResponse,
  noContent,
  problemResponse,
  stubFetch,
} from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import type { CurrentUser } from '../../auth/types'
import { SecurityRoute } from './SecurityRoute'

let transport: FetchStub

const CODES = ['4RJ2-8QKD', '9WTC-2MBE']

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
  transport.route('GET /api/v1/auth/passkeys', () => jsonResponse([]))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

/** Gives jsdom just enough WebAuthn for the ceremony to be exercised. */
function stubAuthenticator(create: () => Promise<Credential | null>) {
  vi.stubGlobal('PublicKeyCredential', function PublicKeyCredentialStub() {
    /* Its existence is what `isPasskeySupported` looks for. */
  })
  vi.stubGlobal('navigator', { ...navigator, credentials: { create, get: create } })
}

/**
 * Rendered behind the real guard, because that is where it lives: the screen reads the account
 * without checking for one, and it is `RequireSession` that guarantees there is one to read.
 */
function renderSecurity() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/account/security']}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route element={<SecurityRoute />} path="/account/security" />
              <Route
                element={<p>Set up your authenticator</p>}
                path="/account/security/authenticator"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function withSecurity(overrides: Partial<CurrentUser['security']>): CurrentUser {
  const base = aCurrentUser()
  return aCurrentUser({ security: { ...base.security, ...overrides } })
}

describe('sign-in and security', () => {
  it('says where the account stands with its second factor', async () => {
    renderSecurity()

    expect(await screen.findByText('Set up and working.')).toBeInTheDocument()
    expect(screen.getByText('8 unused codes left')).toBeInTheDocument()
  })

  it('leads an account with no authenticator to setting one up', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () =>
      jsonResponse(withSecurity({ mfaEnrolment: 'NotEnrolled' })),
    )
    renderSecurity()

    expect(await screen.findByText('Not set up yet.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Set up an authenticator' }))

    expect(await screen.findByText('Set up your authenticator')).toBeInTheDocument()
  })

  it('says an administrator reset is something the holder has to finish', async () => {
    transport.route('GET /api/v1/me', () =>
      jsonResponse(withSecurity({ mfaEnrolment: 'ResetRequired' })),
    )
    renderSecurity()

    expect(await screen.findByText(/Reset by an administrator/)).toBeInTheDocument()
  })

  it('warns before printing a new sheet, because it destroys the one in the drawer', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/mfa/recovery-codes', () =>
      jsonResponse({ recoveryCodes: CODES }),
    )
    renderSecurity()

    await screen.findByText('Set up and working.')
    await user.click(screen.getByRole('button', { name: 'Print a new sheet of recovery codes' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent(/stop working straight away/)

    await user.click(within(dialog).getByRole('button', { name: 'Print a new sheet' }))

    const list = await screen.findByRole('list', { name: 'Recovery codes' })
    expect(within(list).getAllByRole('listitem')).toHaveLength(2)
    expect(screen.getByText(/This is the only time these are shown/)).toBeInTheDocument()
  })

  it('says plainly that the browser cannot do passkeys rather than offering a control that throws', async () => {
    renderSecurity()

    expect(await screen.findByText(/This browser cannot use passkeys/)).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Add a passkey on this device' }),
    ).not.toBeInTheDocument()
  })

  it('asks what to call a passkey before the ceremony, while the device is in front of them', async () => {
    const user = userEvent.setup()
    stubAuthenticator(() =>
      Promise.resolve({
        id: 'Y3JlZC1pZA',
        rawId: new Uint8Array([1, 2]).buffer,
        type: 'public-key',
        response: {
          attestationObject: new Uint8Array([3]).buffer,
          clientDataJSON: new Uint8Array([4]).buffer,
          getTransports: () => ['internal'],
        },
        getClientExtensionResults: () => ({}),
      } as unknown as Credential),
    )
    transport.route('POST /api/v1/auth/passkeys/register/options', () =>
      jsonResponse({
        ceremonyId: 'ceremony-1',
        options: {
          rp: { id: 'shop.example' },
          user: { id: 'dXNlcg', name: 'a', displayName: 'A' },
          challenge: 'AQIDBA',
          pubKeyCredParams: [],
        },
        expiresAt: new Date().toISOString(),
      }),
    )
    transport.route('POST /api/v1/auth/passkeys/register', () =>
      jsonResponse(aPasskey({ label: 'Counter tablet' })),
    )
    renderSecurity()

    await screen.findByText('Set up and working.')
    await user.click(screen.getByRole('button', { name: 'Add a passkey on this device' }))

    // A list of passkeys all called "Passkey" is a list nobody can safely remove anything from, and
    // the moment somebody needs to is the moment they have lost a device.
    await user.click(screen.getByRole('button', { name: 'Add this passkey' }))
    expect(screen.getByText('Give the passkey a name you will recognise.')).toBeInTheDocument()
    expect(transport.callsTo('POST /api/v1/auth/passkeys/register/options')).toHaveLength(0)

    await user.type(screen.getByLabelText(/What should this passkey be called/), 'Counter tablet')
    await user.click(screen.getByRole('button', { name: 'Add this passkey' }))

    expect(await screen.findByText('Counter tablet has been added.')).toBeInTheDocument()
    expect(transport.callsTo('POST /api/v1/auth/passkeys/register')[0]?.body).toMatchObject({
      ceremonyId: 'ceremony-1',
      label: 'Counter tablet',
    })
  })

  it('treats a dismissed platform dialog as nothing having happened', async () => {
    const user = userEvent.setup()
    stubAuthenticator(() => Promise.reject(new DOMException('refused', 'NotAllowedError')))
    transport.route('POST /api/v1/auth/passkeys/register/options', () =>
      jsonResponse({ ceremonyId: 'ceremony-1', options: {}, expiresAt: new Date().toISOString() }),
    )
    renderSecurity()

    await screen.findByText('Set up and working.')
    await user.click(screen.getByRole('button', { name: 'Add a passkey on this device' }))
    await user.type(screen.getByLabelText(/What should this passkey be called/), 'My phone')
    await user.click(screen.getByRole('button', { name: 'Add this passkey' }))

    // Not an error banner. People dismiss that dialog constantly, and a red message teaches them the
    // feature is broken.
    expect(
      await screen.findByText('That was cancelled. Nothing has been added.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('confirms removing a passkey, and says the other factors are unaffected', async () => {
    const user = userEvent.setup()
    stubAuthenticator(() => Promise.resolve(null))
    const passkey = aPasskey({ label: 'Workshop laptop' })
    transport.route('GET /api/v1/auth/passkeys', () => jsonResponse([passkey]))
    transport.route(`DELETE /api/v1/auth/passkeys/${passkey.passkeyId}`, () => noContent())
    renderSecurity()

    await screen.findByText('Workshop laptop')
    await user.click(screen.getByRole('button', { name: 'Remove' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAccessibleName('Remove Workshop laptop?')
    expect(dialog).toHaveTextContent(/Your password and authenticator are unaffected/)

    await user.click(within(dialog).getByRole('button', { name: 'Remove this passkey' }))

    expect(await screen.findByText('Workshop laptop has been removed.')).toBeInTheDocument()
  })

  it('says why the server refused to remove the only factor an account has', async () => {
    const user = userEvent.setup()
    stubAuthenticator(() => Promise.resolve(null))
    const passkey = aPasskey({ label: 'Only key' })
    transport.route('GET /api/v1/auth/passkeys', () => jsonResponse([passkey]))
    transport.route(`DELETE /api/v1/auth/passkeys/${passkey.passkeyId}`, () =>
      problemResponse(409, 'identity.last-factor-cannot-be-removed'),
    )
    renderSecurity()

    await screen.findByText('Only key')
    await user.click(screen.getByRole('button', { name: 'Remove' }))
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', {
        name: 'Remove this passkey',
      }),
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      /This is the only way you can confirm it is you/,
    )
  })

  it('offers signing out of this device without a confirmation in the way', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/logout', () => noContent())
    renderSecurity()

    await screen.findByText('Set up and working.')
    await user.click(screen.getByRole('button', { name: 'Sign out of this device' }))

    // The person doing it is usually walking away from a shared counter right now.
    expect(transport.callsTo('POST /api/v1/auth/logout')).toHaveLength(1)
  })

  it('says when the account still owes a new password', async () => {
    transport.route('GET /api/v1/me', () =>
      jsonResponse(withSecurity({ mustChangePassword: true })),
    )
    renderSecurity()

    expect(
      await screen.findByText(/Your administrator has asked for a new password/),
    ).toBeInTheDocument()
  })

  it('has no accessibility violations axe can see', async () => {
    const { container } = renderSecurity()
    await screen.findByText('Set up and working.')

    await expectNoAccessibilityViolations(container)
  })
})
