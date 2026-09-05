import { useState } from 'react'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AppIntlProvider } from '../i18n/IntlProvider'
import { forgetAntiforgeryToken } from './antiforgery'
import { apiRequest, setSessionChallengeHandler } from './apiClient'
import { SessionProvider } from './SessionProvider'
import { useSession } from './useSession'
import {
  aCurrentUser,
  aSessionExpiry,
  aSignInResult,
  jsonResponse,
  problemResponse,
  stubFetch,
} from './testing/fixtures'
import type { FetchStub } from './testing/fixtures'

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

/** Shows who is signed in, so a test can assert the provider's own state without a screen. */
function WhoAmI() {
  const { status, user } = useSession()
  return (
    <p>
      {status}
      {user === null ? '' : `:${user.displayName}`}
    </p>
  )
}

/**
 * A stand-in for the half-finished measurement wizard the whole design is about: it holds typed
 * input, and saving it is one ordinary request.
 */
function MeasurementStandIn() {
  const [note, setNote] = useState('')
  const [saved, setSaved] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)

  return (
    <div>
      <label htmlFor="note">Waist note</label>
      <input
        id="note"
        onChange={(event) => {
          setNote(event.target.value)
        }}
        value={note}
      />
      <button
        onClick={() => {
          setFailed(false)
          void apiRequest<{ note: string }>('/api/v1/orders', { method: 'POST', body: { note } })
            .then((result) => {
              setSaved(result.note)
            })
            .catch(() => {
              setFailed(true)
            })
        }}
        type="button"
      >
        Save the measurement
      </button>
      {saved === null ? null : <p>Saved: {saved}</p>}
      {failed ? <p>Not saved</p> : null}
    </div>
  )
}

function renderProvider(children: React.ReactNode) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>{children}</SessionProvider>
    </AppIntlProvider>,
  )
}

describe('reading the account at start-up', () => {
  it('reports the signed-in account once the server has answered', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))

    renderProvider(<WhoAmI />)

    expect(await screen.findByText('active:Asha (counter)')).toBeInTheDocument()
  })

  it('reports nobody when the server says nobody, without raising a dialog over an empty page', async () => {
    transport.route('GET /api/v1/me', () => problemResponse(401, 'identity.session-required'))

    renderProvider(<WhoAmI />)

    expect(await screen.findByText('anonymous')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('tells a server that is down apart from a person who is signed out', async () => {
    transport.route('GET /api/v1/me', () => problemResponse(503, 'platform.unavailable'))

    renderProvider(<WhoAmI />)

    // Sending somebody to a sign-in screen they cannot use would be a lie about what went wrong.
    expect(await screen.findByText('unavailable')).toBeInTheDocument()
  })
})

describe('the session about to end', () => {
  function expiringIn(minutes: number) {
    return aSessionExpiry({
      idleExpiresAt: new Date(Date.now() + minutes * 60_000).toISOString(),
      absoluteExpiresAt: new Date(Date.now() + 11 * 60 * 60_000).toISOString(),
      warningLeadSeconds: 120,
    })
  }

  it('warns two minutes ahead, and says nothing has been lost', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ session: expiringIn(3) })))

    renderProvider(<WhoAmI />)
    await screen.findByText('active:Asha (counter)')

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(61_000)
    })

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAccessibleName('You will be signed out soon')
    expect(dialog).toHaveTextContent(/Nothing you have typed will be lost/)
    // 2.2.1 Timing Adjustable: extending is one press, and it is the focused control.
    expect(screen.getByRole('button', { name: 'Carry on working' })).toHaveFocus()
  })

  it('counts down in a place a screen reader is not told about every second', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ session: expiringIn(3) })))

    renderProvider(<WhoAmI />)
    await screen.findByText('active:Asha (counter)')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(61_000)
    })
    await screen.findByRole('dialog')

    // The visible number ticks; the announcement is a separate region that changes only at
    // thresholds, so nobody is talked over once a second for two minutes.
    const announcement = screen.getByRole('status')
    expect(announcement).toHaveTextContent(/You will be signed out in about/)
    const before = announcement.textContent

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3_000)
    })

    expect(announcement.textContent).toBe(before)
  })

  it('carries on working by making one ordinary request, which slides the deadline', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let reads = 0
    transport.route('GET /api/v1/me', () => {
      reads += 1
      return jsonResponse(aCurrentUser({ session: expiringIn(reads === 1 ? 3 : 30) }))
    })

    renderProvider(<WhoAmI />)
    await screen.findByText('active:Asha (counter)')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(61_000)
    })
    await screen.findByRole('dialog')

    await act(async () => {
      screen.getByRole('button', { name: 'Carry on working' }).click()
      await vi.advanceTimersByTimeAsync(0)
    })

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })
    expect(reads).toBe(2)
  })

  it('turns into the re-authentication dialog when nobody answers in time', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ session: expiringIn(3) })))

    renderProvider(<WhoAmI />)
    await screen.findByText('active:Asha (counter)')

    await act(async () => {
      await vi.advanceTimersByTimeAsync(61_000)
    })
    await screen.findByRole('dialog')

    await act(async () => {
      await vi.advanceTimersByTimeAsync(125_000)
    })

    expect(await screen.findByRole('dialog')).toHaveAccessibleName('Your session ended')
  })
})

describe('a session that ends in the middle of somebody typing', () => {
  it('re-authenticates in place and finishes the request that was refused', async () => {
    const user = userEvent.setup()
    let attempts = 0
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    transport.route('POST /api/v1/orders', (call) => {
      attempts += 1
      return attempts === 1
        ? problemResponse(401, 'identity.session-required', {}, { 'X-Session-State': 'expired' })
        : jsonResponse(call.body)
    })
    transport.route('POST /api/v1/auth/login', () => jsonResponse(aSignInResult()))

    renderProvider(<MeasurementStandIn />)
    await screen.findByRole('button', { name: 'Save the measurement' })

    await user.type(screen.getByLabelText('Waist note'), 'left sleeve 24.5')
    await user.click(screen.getByRole('button', { name: 'Save the measurement' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAccessibleName('Your session ended')
    expect(dialog).toHaveTextContent(/Nothing you have typed has been lost/)
    // It knows who it is asking, so only the password is wanted.
    expect(dialog).toHaveTextContent('Signed in as Asha (counter)')

    await user.type(screen.getByLabelText('Password'), 'a-different-password')
    await user.click(screen.getByRole('button', { name: 'Sign back in' }))

    // The measurement went, unchanged, without the screen unmounting or anything being retyped.
    expect(await screen.findByText('Saved: left sleeve 24.5')).toBeInTheDocument()
    expect(screen.getByLabelText('Waist note')).toHaveValue('left sleeve 24.5')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(attempts).toBe(2)
  })

  it('carries on into the second factor without leaving the screen', async () => {
    const user = userEvent.setup()
    let attempts = 0
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    transport.route('POST /api/v1/orders', (call) => {
      attempts += 1
      return attempts === 1
        ? problemResponse(401, 'identity.session-required')
        : jsonResponse(call.body)
    })
    transport.route('POST /api/v1/auth/login', () =>
      jsonResponse(aSignInResult({ step: 'multiFactorRequired' })),
    )
    transport.route('POST /api/v1/auth/mfa/challenge', () =>
      jsonResponse({
        remainingRecoveryCodes: 7,
        shouldReissueRecoveryCodes: false,
        deviceRemembered: false,
        session: aSessionExpiry(),
      }),
    )

    renderProvider(<MeasurementStandIn />)
    await screen.findByRole('button', { name: 'Save the measurement' })
    await user.type(screen.getByLabelText('Waist note'), 'hem 31')
    await user.click(screen.getByRole('button', { name: 'Save the measurement' }))

    await screen.findByRole('dialog')
    await user.type(screen.getByLabelText('Password'), 'a-different-password')
    await user.click(screen.getByRole('button', { name: 'Sign back in' }))

    // The dialog asks the next question itself rather than sending anybody to another screen.
    expect(await screen.findByText(/Type the code from your authenticator/)).toBeInTheDocument()
    await user.type(screen.getByLabelText(/Code from your authenticator/), '123456')
    await user.click(screen.getByRole('button', { name: 'Sign back in' }))

    expect(await screen.findByText('Saved: hem 31')).toBeInTheDocument()
  })

  it('keeps the typed input when the person declines to sign back in', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    transport.route('POST /api/v1/orders', () => problemResponse(401, 'identity.session-required'))

    renderProvider(<MeasurementStandIn />)
    await screen.findByRole('button', { name: 'Save the measurement' })
    await user.type(screen.getByLabelText('Waist note'), 'cuff 9')
    await user.click(screen.getByRole('button', { name: 'Save the measurement' }))

    await screen.findByRole('dialog')
    await user.click(screen.getByRole('button', { name: 'Not now' }))

    // Declining is a real answer: the save failed and says so, and nothing typed has gone.
    expect(await screen.findByText('Not saved')).toBeInTheDocument()
    expect(screen.getByLabelText('Waist note')).toHaveValue('cuff 9')
  })

  it('says the account was signed out elsewhere when that is what happened', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    transport.route('POST /api/v1/orders', () =>
      problemResponse(401, 'identity.session-required', {}, { 'X-Session-State': 'revoked' }),
    )

    renderProvider(<MeasurementStandIn />)
    await screen.findByRole('button', { name: 'Save the measurement' })
    await user.click(screen.getByRole('button', { name: 'Save the measurement' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAccessibleName('You were signed out')
    expect(dialog).toHaveTextContent(/change your password/)
  })

  it('shows the refusal in the dialog and stays open when the password is wrong', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    transport.route('POST /api/v1/orders', () => problemResponse(401, 'identity.session-required'))
    transport.route('POST /api/v1/auth/login', () =>
      problemResponse(401, 'identity.invalid-credentials'),
    )

    renderProvider(<MeasurementStandIn />)
    await screen.findByRole('button', { name: 'Save the measurement' })
    await user.click(screen.getByRole('button', { name: 'Save the measurement' }))
    await screen.findByRole('dialog')

    await user.type(screen.getByLabelText('Password'), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Sign back in' }))

    expect(await screen.findByText(/Those sign-in details are not right/)).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })
})

describe('what the whole journey writes to the device', () => {
  it('writes nothing to any storage a script can read back', async () => {
    const user = userEvent.setup()
    const localWrites = vi.spyOn(Storage.prototype, 'setItem')
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    transport.route('POST /api/v1/orders', (call) => jsonResponse(call.body))
    transport.route('POST /api/v1/auth/login', () => jsonResponse(aSignInResult()))

    renderProvider(<MeasurementStandIn />)
    await screen.findByRole('button', { name: 'Save the measurement' })
    await user.type(screen.getByLabelText('Waist note'), 'neck 15')
    await user.click(screen.getByRole('button', { name: 'Save the measurement' }))
    await screen.findByText('Saved: neck 15')

    // The session is an httpOnly cookie and the anti-forgery token is a module variable. Neither
    // ends up anywhere a second script, a second tab, or the next person on a shared counter tablet
    // could read it.
    expect(localWrites).not.toHaveBeenCalled()
    expect(document.cookie).toBe('')
  })
})
