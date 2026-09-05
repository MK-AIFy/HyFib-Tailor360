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
  aSessionExpiry,
  jsonResponse,
  problemResponse,
  stubFetch,
} from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import type { FactorAvailability } from '../../auth/types'
import { MfaChallengeRoute } from './MfaChallengeRoute'

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

function withFactors(factors: Partial<FactorAvailability>, unused = 8) {
  const base = aCurrentUser()
  return aCurrentUser({
    security: {
      ...base.security,
      mfaSatisfied: false,
      unusedRecoveryCodes: unused,
      factors: { ...base.security.factors, ...factors },
    },
  })
}

function renderChallenge() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/sign-in/verify']}>
          <Routes>
            <Route element={<MfaChallengeRoute />} path="/sign-in/verify" />
            <Route element={<p>The shop workspace</p>} path="/" />
            <Route element={<p>Sign in</p>} path="/sign-in" />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

const CHALLENGE_ANSWERED = {
  remainingRecoveryCodes: 7,
  shouldReissueRecoveryCodes: false,
  deviceRemembered: false,
  session: aSessionExpiry(),
}

describe('the second-factor challenge', () => {
  it('asks for a code that a device can offer and a person can paste', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(withFactors({ recoveryCode: false })))
    renderChallenge()

    const code = await screen.findByLabelText(/Code from your authenticator/)
    // 3.3.8 Accessible Authentication: the device offers the incoming code above the keyboard, and
    // the field accepts a paste rather than demanding transcription.
    expect(code).toHaveAttribute('autocomplete', 'one-time-code')
    expect(code).toHaveAttribute('inputmode', 'numeric')
  })

  it('offers no choice of factor when there is only one', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(withFactors({ recoveryCode: false })))
    renderChallenge()

    await screen.findByLabelText(/Code from your authenticator/)
    expect(
      screen.queryByRole('group', { name: /How would you like to confirm/ }),
    ).not.toBeInTheDocument()
  })

  it('offers the recovery code as a choice when the account has both, and relabels the field', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => jsonResponse(withFactors({})))
    renderChallenge()

    await screen.findByLabelText(/Code from your authenticator/)
    await user.click(screen.getByRole('radio', { name: /printed recovery codes/ }))

    const code = screen.getByLabelText(/Recovery code/)
    // A recovery code carries letters, so a numeric keypad would hide half of it.
    expect(code).toHaveAttribute('inputmode', 'text')
  })

  it('leaves "remember this device" off, and says who should not use it', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(withFactors({})))
    renderChallenge()

    const remember = await screen.findByLabelText(/Remember this device/)
    // A default that skips the challenge is a default that quietly weakens every account signed in
    // from a shared counter tablet afterwards.
    expect(remember).not.toBeChecked()
    expect(screen.getByText(/Never on a shared counter or workshop device/)).toBeInTheDocument()
  })

  it('sends the factor, the code and the remember choice, then carries on', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => jsonResponse(withFactors({})))
    transport.route('POST /api/v1/auth/mfa/challenge', () => jsonResponse(CHALLENGE_ANSWERED))
    renderChallenge()

    await screen.findByLabelText(/Code from your authenticator/)
    await user.type(screen.getByLabelText(/Code from your authenticator/), '123456')
    await user.click(screen.getByRole('button', { name: 'Confirm' }))

    expect(await screen.findByText('The shop workspace')).toBeInTheDocument()
    expect(transport.callsTo('POST /api/v1/auth/mfa/challenge')[0]?.body).toEqual({
      factor: 'totp',
      code: '123456',
      rememberDevice: false,
    })
  })

  it('says a refused code is refused, and nothing about how close it was', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => jsonResponse(withFactors({})))
    transport.route('POST /api/v1/auth/mfa/challenge', () =>
      problemResponse(401, 'identity.mfa-code-invalid'),
    )
    renderChallenge()

    await screen.findByLabelText(/Code from your authenticator/)
    await user.type(screen.getByLabelText(/Code from your authenticator/), '000000')
    await user.click(screen.getByRole('button', { name: 'Confirm' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('That code is not right')
    expect(screen.getByLabelText(/Code from your authenticator/)).toHaveValue('')
  })

  it('counts the recovery codes down, and says where to print more when they run low', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(withFactors({}, 2)))
    renderChallenge()

    expect(await screen.findByText(/2 recovery codes left/)).toBeInTheDocument()
    expect(screen.getByText(/running low on recovery codes/)).toBeInTheDocument()
  })

  it('says so plainly when the account has nothing at all to confirm with', async () => {
    transport.route('GET /api/v1/me', () =>
      jsonResponse(withFactors({ authenticator: false, recoveryCode: false, passkey: false }, 0)),
    )
    renderChallenge()

    expect(
      await screen.findByText(/Ask your shop administrator to reset it for you/),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirm' })).not.toBeInTheDocument()
  })

  it('sends somebody whose first factor has gone back to the start', async () => {
    transport.route('GET /api/v1/me', () => problemResponse(401, 'identity.session-required'))
    renderChallenge()

    expect(await screen.findByText('Sign in')).toBeInTheDocument()
  })

  it('has no accessibility violations axe can see', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(withFactors({})))
    const { container } = renderChallenge()
    await screen.findByLabelText(/Code from your authenticator/)

    await expectNoAccessibilityViolations(container)
  })
})
