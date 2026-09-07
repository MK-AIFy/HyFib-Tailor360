import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AppIntlProvider } from '../i18n/IntlProvider'
import { forgetAntiforgeryToken } from './antiforgery'
import { setSessionChallengeHandler } from './apiClient'
import { SessionProvider } from './SessionProvider'
import { useSession, useStepUp } from './useSession'
import { aCurrentUser, aSignInResult, jsonResponse, stubFetch } from './testing/fixtures'
import type { FetchStub } from './testing/fixtures'

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
})

/** What #25 will do before a sensitive administrative action. */
function AdministrativeAction() {
  const stepUp = useStepUp()
  const [outcome, setOutcome] = useState<string | null>(null)

  return (
    <div>
      <button
        onClick={() => {
          void stepUp('resetting this authenticator').then((confirmed) => {
            setOutcome(confirmed ? 'proceeded' : 'abandoned')
          })
        }}
        type="button"
      >
        Reset the authenticator
      </button>
      {outcome === null ? null : <p>{outcome}</p>}
    </div>
  )
}

function renderWithProvider(children: React.ReactNode) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>{children}</SessionProvider>
    </AppIntlProvider>,
  )
}

describe('the step-up a sensitive action asks for', () => {
  it('raises the same dialog the expiry path does, and names what it is protecting', async () => {
    const user = userEvent.setup()
    transport.route('POST /api/v1/auth/login', () => jsonResponse(aSignInResult()))
    renderWithProvider(<AdministrativeAction />)

    await screen.findByRole('button', { name: 'Reset the authenticator' })
    await user.click(screen.getByRole('button', { name: 'Reset the authenticator' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAccessibleName('Confirm it is you')
    // A prompt that appears out of nowhere is a prompt people answer without reading.
    expect(dialog).toHaveTextContent(
      /Before resetting this authenticator, type your password again/,
    )

    await user.type(screen.getByLabelText('Password'), 'a-password')
    await user.click(screen.getByRole('button', { name: 'Confirm' }))

    expect(await screen.findByText('proceeded')).toBeInTheDocument()
  })

  it('reports an abandoned prompt as abandoned, so the action does not go ahead', async () => {
    const user = userEvent.setup()
    renderWithProvider(<AdministrativeAction />)

    await screen.findByRole('button', { name: 'Reset the authenticator' })
    await user.click(screen.getByRole('button', { name: 'Reset the authenticator' }))
    await screen.findByRole('dialog')
    await user.click(screen.getByRole('button', { name: 'Not now' }))

    expect(await screen.findByText('abandoned')).toBeInTheDocument()
  })

  it('closes on Escape, because 2.1.2 No Keyboard Trap has no exceptions', async () => {
    const user = userEvent.setup()
    renderWithProvider(<AdministrativeAction />)

    await screen.findByRole('button', { name: 'Reset the authenticator' })
    await user.click(screen.getByRole('button', { name: 'Reset the authenticator' }))
    await screen.findByRole('dialog')

    await user.keyboard('{Escape}')

    expect(await screen.findByText('abandoned')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})

describe('using the session outside a provider', () => {
  function Orphan() {
    useSession()
    return null
  }

  it('throws rather than quietly reporting that nobody is signed in', () => {
    // A hook that answered "nobody" for a missing provider would let a screen render its signed-out
    // state because of a wiring mistake, which looks like a feature until permissions disappear.
    const noise = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    expect(() => render(<Orphan />)).toThrow(/must be used inside a SessionProvider/)
    noise.mockRestore()
  })
})
