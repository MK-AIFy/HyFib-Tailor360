import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { AuthShell } from './AuthShell'

function stubVersion(environment: string) {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(
        new Response(
          JSON.stringify({ version: '0.1.0-alpha', buildHash: '0abcdef', environment }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
      ),
    ),
  )
}

beforeEach(() => {
  stubVersion('production')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderShell() {
  return render(
    <AppIntlProvider locale="en-IN">
      <MemoryRouter initialEntries={['/sign-in']}>
        <Routes>
          <Route element={<AuthShell />}>
            <Route element={<h1>Sign in</h1>} path="/sign-in" />
          </Route>
        </Routes>
      </MemoryRouter>
    </AppIntlProvider>,
  )
}

describe('the frame the signing-in screens sit in', () => {
  it('gives the screen a main landmark and nothing to get lost in', async () => {
    renderShell()

    expect(await screen.findByRole('main')).toHaveAttribute('id', 'main-content')
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Sign in')
    // Deliberately no navigation: every destination behind it would refuse somebody who has not
    // signed in, and a screen full of dead ends reads as a broken application.
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it('reaches display settings, which is where somebody who cannot read this screen has to go', async () => {
    renderShell()

    await screen.findByRole('main')
    // 150% text and the high-contrast sunlight theme are needed to read *this* screen. A preference
    // reachable only after signing in is a preference that person cannot reach at all.
    expect(screen.getByRole('link', { name: 'Display settings' })).toHaveAttribute(
      'href',
      '/settings/display',
    )
  })

  it('says nothing about the environment on a production device', async () => {
    renderShell()

    await screen.findByRole('main')
    expect(screen.queryByText(/TRAINING/)).not.toBeInTheDocument()
  })

  it('warns that this is training before anybody types a password into it', async () => {
    vi.unstubAllGlobals()
    stubVersion('staging')

    renderShell()

    // The one piece of chrome that genuinely matters here. A member of staff must know they are on
    // a training or staging device before they type a real password, not after.
    expect(await screen.findByText(/TRAINING/)).toBeInTheDocument()
  })
})
