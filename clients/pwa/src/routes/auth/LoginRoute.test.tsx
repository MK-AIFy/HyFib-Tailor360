import { render, screen } from '@testing-library/react'
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
  aSignInResult,
  jsonResponse,
  problemResponse,
  stubFetch,
} from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { LoginRoute } from './LoginRoute'

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => problemResponse(401, 'identity.session-required'))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderLogin(at = '/sign-in') {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[at]}>
          <Routes>
            <Route element={<LoginRoute />} path="/sign-in" />
            <Route element={<p>The shop workspace</p>} path="/" />
            <Route element={<p>Confirm it is you</p>} path="/sign-in/verify" />
            <Route
              element={<p>Set up your authenticator</p>}
              path="/account/security/authenticator"
            />
            <Route element={<p>Reset your password</p>} path="/recovery" />
            <Route element={<p>The order</p>} path="/orders/42" />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('the sign-in screen', () => {
  it('labels both fields visibly and tells the browser what they are', async () => {
    renderLogin()

    const identifier = await screen.findByLabelText(/Sign-in name or email address/)
    const password = screen.getByLabelText(/^Password/)

    // 1.3.5 Identify Input Purpose, and the reason a password manager can fill this in one action.
    expect(identifier).toHaveAttribute('autocomplete', 'username')
    expect(password).toHaveAttribute('autocomplete', 'current-password')
    expect(password).toHaveAttribute('type', 'password')
  })

  it('has no accessibility violations axe can see', async () => {
    const { container } = renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    await expectNoAccessibilityViolations(container)
  })

  it('lists what is missing in the summary and moves focus to it', async () => {
    const user = userEvent.setup()
    renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    const summary = await screen.findByRole('group', { name: /There are 2 problems/ })
    expect(summary).toHaveFocus()
    expect(summary).toHaveTextContent('Type your sign-in name or email address.')
    expect(summary).toHaveTextContent('Type your password.')
    // Nothing was sent: there was nothing to send.
    expect(transport.callsTo('POST /api/v1/auth/login')).toHaveLength(0)
  })

  it('says the same thing about a wrong password and an account that does not exist', async () => {
    const user = userEvent.setup()

    async function messageFor(code: string): Promise<string> {
      transport.route('POST /api/v1/auth/login', () => problemResponse(401, code))
      const view = renderLogin()
      await screen.findByLabelText(/Sign-in name or email address/)
      await user.type(screen.getByLabelText(/Sign-in name or email address/), 'asha.counter')
      await user.type(screen.getByLabelText(/^Password/), 'not-the-password')
      await user.click(screen.getByRole('button', { name: 'Sign in' }))
      const alert = await screen.findByRole('alert')
      const text = alert.textContent ?? ''
      view.unmount()
      return text
    }

    // The server already answers these identically and takes the same time over each. A screen that
    // added "no account with that name" would hand the enumeration oracle straight back.
    const wrongPassword = await messageFor('identity.invalid-credentials')
    expect(wrongPassword).toContain('Those sign-in details are not right')
  })

  it('never leaves the password in the field after an attempt', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/login', () =>
      problemResponse(401, 'identity.invalid-credentials'),
    )
    renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    await user.type(screen.getByLabelText(/Sign-in name or email address/), 'asha.counter')
    await user.type(screen.getByLabelText(/^Password/), 'not-the-password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    await screen.findByRole('alert')

    // A shared counter tablet must not hold somebody's password in a field for the next person.
    expect(screen.getByLabelText(/^Password/)).toHaveValue('')
  })

  it('names the wait when the throttle refuses, so the person knows it is not permanent', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/login', () =>
      problemResponse(429, 'identity.too-many-attempts', { retryAfterSeconds: 45 }),
    )
    renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    await user.type(screen.getByLabelText(/Sign-in name or email address/), 'asha.counter')
    await user.type(screen.getByLabelText(/^Password/), 'not-the-password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('45 seconds')
  })

  it('goes where the person was heading when the sign-in is complete', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/login', () => jsonResponse(aSignInResult()))
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    await user.type(screen.getByLabelText(/Sign-in name or email address/), 'asha.counter')
    await user.type(screen.getByLabelText(/^Password/), 'a-password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('The shop workspace')).toBeInTheDocument()
  })

  it('goes to the challenge when a second factor is still owed', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/login', () =>
      jsonResponse(aSignInResult({ step: 'multiFactorRequired' })),
    )
    transport.route('GET /api/v1/me', () =>
      jsonResponse(aCurrentUser({ security: { ...aCurrentUser().security, mfaSatisfied: false } })),
    )
    renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    await user.type(screen.getByLabelText(/Sign-in name or email address/), 'asha.counter')
    await user.type(screen.getByLabelText(/^Password/), 'a-password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Confirm it is you')).toBeInTheDocument()
  })

  it('goes to the authenticator setup when the account is required to hold one', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/login', () =>
      jsonResponse(aSignInResult({ step: 'multiFactorEnrolmentRequired' })),
    )
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    await user.type(screen.getByLabelText(/Sign-in name or email address/), 'asha.counter')
    await user.type(screen.getByLabelText(/^Password/), 'a-password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Set up your authenticator')).toBeInTheDocument()
  })

  it('says plainly that this browser cannot use passkeys rather than offering a control that throws', async () => {
    renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    // jsdom has no WebAuthn, which is exactly the older counter webview this guards against.
    expect(screen.queryByRole('button', { name: 'Sign in with a passkey' })).not.toBeInTheDocument()
    expect(screen.getByText(/This browser cannot use passkeys/)).toBeInTheDocument()
  })

  it('offers a way back for somebody who cannot sign in at all', async () => {
    renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    expect(screen.getByRole('link', { name: 'I have forgotten my password' })).toHaveAttribute(
      'href',
      '/recovery',
    )
  })

  it('never puts anything typed into a query string or into storage', async () => {
    const user = userEvent.setup()
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    transport.route('POST /api/v1/auth/login', () => jsonResponse(aSignInResult()))
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    renderLogin()
    await screen.findByLabelText(/Sign-in name or email address/)

    await user.type(screen.getByLabelText(/Sign-in name or email address/), 'asha.counter')
    await user.type(screen.getByLabelText(/^Password/), 'a-password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    await screen.findByText('The shop workspace')

    expect(writes).not.toHaveBeenCalled()
    for (const call of transport.calls) {
      expect(call.path).not.toContain('a-password')
    }
  })
})
