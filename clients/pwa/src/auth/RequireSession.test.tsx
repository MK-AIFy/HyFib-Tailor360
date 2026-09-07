import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../i18n/IntlProvider'
import { forgetAntiforgeryToken } from './antiforgery'
import { setSessionChallengeHandler } from './apiClient'
import { RequireSession } from './RequireSession'
import { SessionProvider } from './SessionProvider'
import { useSession } from './useSession'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from './testing/fixtures'
import type { FetchStub } from './testing/fixtures'
import type { SignInStep } from './types'

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
})

/** Puts the provider into the state a sign-in would have left it in. */
function RecordStep({ step }: { readonly step: SignInStep }) {
  const { recordAuthentication, status } = useSession()
  return (
    <button
      onClick={() => {
        void recordAuthentication(step)
      }}
      type="button"
    >
      Record {step} ({status})
    </button>
  )
}

function renderGuarded(extra?: React.ReactNode) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/orders/42']}>
          {extra}
          <Routes>
            <Route element={<RequireSession />}>
              <Route element={<p>The order</p>} path="/orders/42" />
              <Route element={<p>Confirm it is you</p>} path="/sign-in/verify" />
              <Route
                element={<p>Set up your authenticator</p>}
                path="/account/security/authenticator"
              />
            </Route>
            <Route element={<p>Sign in</p>} path="/sign-in" />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('the boundary between signed in and everything else', () => {
  it('says the answer is not known yet rather than flashing the sign-in form', async () => {
    let release: (() => void) | undefined
    transport.route('GET /api/v1/me', async () => {
      await new Promise<void>((resolve) => {
        release = resolve
      })
      return jsonResponse(aCurrentUser())
    })

    renderGuarded()

    // On a slow counter connection a flash of the sign-in form is long enough to start typing into.
    expect(await screen.findByText(/Loading your account/)).toBeInTheDocument()
    release?.()
    expect(await screen.findByText('The order')).toBeInTheDocument()
  })

  it('renders the screen once there is a session', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    renderGuarded()

    expect(await screen.findByText('The order')).toBeInTheDocument()
  })

  it('sends somebody with no session to sign in', async () => {
    transport.route('GET /api/v1/me', () => problemResponse(401, 'identity.session-required'))
    renderGuarded()

    expect(await screen.findByText('Sign in')).toBeInTheDocument()
  })

  it('tells a server that is down apart from a person who is signed out', async () => {
    transport.route('GET /api/v1/me', () => problemResponse(503, 'platform.unavailable'))
    renderGuarded()

    // Sending them to a sign-in screen they cannot use would be a lie about what went wrong, and
    // they would type a password into it that could not have worked.
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent(
      'Your account could not be loaded',
    )
    expect(screen.queryByText('Sign in')).not.toBeInTheDocument()
  })

  it('holds a half-finished sign-in at the challenge', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    renderGuarded(<RecordStep step="multiFactorRequired" />)

    await screen.findByText('The order')
    screen.getByRole('button', { name: /Record multiFactorRequired/ }).click()

    expect(await screen.findByText('Confirm it is you')).toBeInTheDocument()
  })

  it('holds an account that owes an authenticator at the setup screen', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    renderGuarded(<RecordStep step="multiFactorEnrolmentRequired" />)

    await screen.findByText('The order')
    screen.getByRole('button', { name: /Record multiFactorEnrolmentRequired/ }).click()

    // And does not then bounce off it again: the guard checks where it already is.
    expect(await screen.findByText('Set up your authenticator')).toBeInTheDocument()
  })

  it('lets a finished sign-in through', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    renderGuarded(<RecordStep step="complete" />)

    await screen.findByText('The order')
    screen.getByRole('button', { name: /Record complete/ }).click()

    expect(await screen.findByText('The order')).toBeInTheDocument()
  })
})
