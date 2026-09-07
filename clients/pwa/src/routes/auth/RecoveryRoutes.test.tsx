import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import type { ReactElement } from 'react'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { RecoveryConfirmRoute } from './RecoveryConfirmRoute'
import { RecoveryRequestRoute } from './RecoveryRequestRoute'

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

/** These screens have no session by definition, so they do not need the provider. */
function renderRoute(element: ReactElement, path: string, at: string) {
  return render(
    <AppIntlProvider locale="en-IN">
      <MemoryRouter initialEntries={[at]}>
        <Routes>
          <Route element={element} path={path} />
          <Route element={<p>Sign in</p>} path="/sign-in" />
          <Route element={<p>Reset your password</p>} path="/recovery" />
        </Routes>
      </MemoryRouter>
    </AppIntlProvider>,
  )
}

describe('asking for a reset link', () => {
  it('answers the same way whether or not the address is known, and says why', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/recovery/request', () =>
      jsonResponse({ message: 'accepted' }, 202),
    )
    renderRoute(<RecoveryRequestRoute />, '/recovery', '/recovery')

    await user.type(screen.getByLabelText(/Email address/), 'nobody@example.invalid')
    await user.click(screen.getByRole('button', { name: 'Send me a link' }))

    expect(await screen.findByText(/If that address belongs to an account/)).toBeInTheDocument()
    // Because that can look like the form did nothing, the screen says what it can and cannot know
    // rather than leaving somebody who mistyped their address waiting for a message.
    expect(
      screen.getByText(/nobody can use this screen to find out who has an account/),
    ).toBeInTheDocument()
  })

  it('will not send an empty address, and says which field is missing', async () => {
    const user = userEvent.setup()
    renderRoute(<RecoveryRequestRoute />, '/recovery', '/recovery')

    await user.click(screen.getByRole('button', { name: 'Send me a link' }))

    expect(screen.getByText('Type the email address on your account.')).toBeInTheDocument()
    expect(transport.callsTo('POST /api/v1/auth/recovery/request')).toHaveLength(0)
  })

  it('names the wait when the throttle refuses, because each request sends somebody a message', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/recovery/request', () =>
      problemResponse(429, 'identity.too-many-attempts', { retryAfterSeconds: 900 }),
    )
    renderRoute(<RecoveryRequestRoute />, '/recovery', '/recovery')

    await user.type(screen.getByLabelText(/Email address/), 'asha.counter@example.invalid')
    await user.click(screen.getByRole('button', { name: 'Send me a link' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('900 seconds')
  })

  it('has no accessibility violations axe can see', async () => {
    const { container } = renderRoute(<RecoveryRequestRoute />, '/recovery', '/recovery')
    await expectNoAccessibilityViolations(container)
  })
})

describe('spending a reset link', () => {
  const LINK = '/recovery/confirm?token=aaaabbbbccccddddeeeeffff'

  it('says the link is incomplete rather than failing at the server', () => {
    renderRoute(<RecoveryConfirmRoute />, '/recovery/confirm', '/recovery/confirm')

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('That link is not complete')
    expect(screen.getByText(/Copying it by hand often loses the end of it/)).toBeInTheDocument()
  })

  it('states the password rules before the field, not one refusal at a time', () => {
    renderRoute(<RecoveryConfirmRoute />, '/recovery/confirm', LINK)

    expect(screen.getByText(/At least 12 characters/)).toBeInTheDocument()
    expect(screen.getByLabelText(/^New password/)).toHaveAttribute('autocomplete', 'new-password')
  })

  it('catches a mistyped repeat before it costs another twenty minutes', async () => {
    const user = userEvent.setup()
    renderRoute(<RecoveryConfirmRoute />, '/recovery/confirm', LINK)

    await user.type(screen.getByLabelText(/^New password/), 'a-long-enough-passphrase')
    await user.type(
      screen.getByLabelText(/Type the new password again/),
      'a-long-enough-passphrase-typo',
    )
    await user.click(screen.getByRole('button', { name: 'Set the new password' }))

    expect(await screen.findByRole('group', { name: /There is a problem/ })).toHaveTextContent(
      'The two passwords are not the same.',
    )
    expect(transport.callsTo('POST /api/v1/auth/recovery/confirm')).toHaveLength(0)
  })

  it("puts the server's own field message in the field and in the summary", async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/recovery/confirm', () =>
      problemResponse(400, 'identity.password-breached', {
        errors: {
          password: ['This password appears in a public list of exposed passwords.'],
        },
      }),
    )
    renderRoute(<RecoveryConfirmRoute />, '/recovery/confirm', LINK)

    await user.type(screen.getByLabelText(/^New password/), 'password12345')
    await user.type(screen.getByLabelText(/Type the new password again/), 'password12345')
    await user.click(screen.getByRole('button', { name: 'Set the new password' }))

    // The server knows things this screen cannot — a breach list, the account's own name — so its
    // per-field messages belong in the field rather than in a banner at the top.
    const summary = await screen.findByRole('group', { name: /There is a problem/ })
    expect(summary).toHaveTextContent('public list of exposed passwords')
    expect(screen.getByLabelText(/^New password/)).toHaveAttribute('aria-invalid', 'true')
  })

  it('says the same thing about a spent link, an expired one and one that never existed', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/recovery/confirm', () =>
      problemResponse(400, 'identity.recovery-token-invalid'),
    )
    renderRoute(<RecoveryConfirmRoute />, '/recovery/confirm', LINK)

    await user.type(screen.getByLabelText(/^New password/), 'a-long-enough-passphrase')
    await user.type(
      screen.getByLabelText(/Type the new password again/),
      'a-long-enough-passphrase',
    )
    await user.click(screen.getByRole('button', { name: 'Set the new password' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/That link cannot be used/)
  })

  it('says how many devices were signed out, and that the second step still applies', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/recovery/confirm', () =>
      jsonResponse({
        multiFactorStillRequired: true,
        sessionsRevoked: 2,
        mustChangePassword: false,
      }),
    )
    renderRoute(<RecoveryConfirmRoute />, '/recovery/confirm', LINK)

    await user.type(screen.getByLabelText(/^New password/), 'a-long-enough-passphrase')
    await user.type(
      screen.getByLabelText(/Type the new password again/),
      'a-long-enough-passphrase',
    )
    await user.click(screen.getByRole('button', { name: 'Set the new password' }))

    expect(await screen.findByText(/2 devices were signed out/)).toBeInTheDocument()
    expect(
      screen.getByText(/still be asked for a code from your authenticator/),
    ).toBeInTheDocument()
  })

  it('sends the token in the body and never puts it anywhere else', async () => {
    const user = userEvent.setup()
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    transport.route('POST /api/v1/auth/recovery/confirm', () =>
      jsonResponse({
        multiFactorStillRequired: false,
        sessionsRevoked: 0,
        mustChangePassword: false,
      }),
    )
    renderRoute(<RecoveryConfirmRoute />, '/recovery/confirm', LINK)

    await user.type(screen.getByLabelText(/^New password/), 'a-long-enough-passphrase')
    await user.type(
      screen.getByLabelText(/Type the new password again/),
      'a-long-enough-passphrase',
    )
    await user.click(screen.getByRole('button', { name: 'Set the new password' }))
    await screen.findByText(/Your password is set/)

    const sent = transport.callsTo('POST /api/v1/auth/recovery/confirm')[0]
    expect(sent?.body).toEqual({
      token: 'aaaabbbbccccddddeeeeffff',
      newPassword: 'a-long-enough-passphrase',
    })
    expect(writes).not.toHaveBeenCalled()
  })

  it('has no accessibility violations axe can see', async () => {
    const { container } = renderRoute(<RecoveryConfirmRoute />, '/recovery/confirm', LINK)
    await expectNoAccessibilityViolations(container)
  })
})
