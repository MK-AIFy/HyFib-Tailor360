import { render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { App } from './App'
import { DisplaySettingsRoute, HomeRoute, NotFoundRoute } from './app/router'
import { DisplayPreferencesProvider } from './app/DisplayPreferencesProvider'
import { AppIntlProvider } from './i18n/IntlProvider'
import { forgetAntiforgeryToken } from './auth/antiforgery'
import { setSessionChallengeHandler } from './auth/apiClient'
import { SessionProvider } from './auth/SessionProvider'
import { problemResponse, stubFetch } from './auth/testing/fixtures'
import type { FetchStub } from './auth/testing/fixtures'
import { versionPayload } from './app/testing/versionFixture'

/**
 * Renders the real shell over an in-memory copy of the route table, so the test can start on any path
 * without a browser history. The route elements are the ones the browser router uses.
 *
 * `SessionProvider` sits above `DisplayPreferencesProvider` here exactly as it does in `main.tsx`
 * (#374) — this shell's own tests never sign in, so `GET /api/v1/me` is stubbed 401 by default and
 * the preferences provider falls back to its local device store, which is the same behaviour every
 * one of these tests already asserted against before there was a session in the tree at all.
 *
 * jsdom reports a 1024 px window and implements neither `ResizeObserver` nor `matchMedia`, so the
 * shell measures 1024 and chooses the desktop layout. The phone and tablet layouts are asserted in
 * `components/layout/AppShell.test.tsx`, which passes the width in.
 */
function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: <App />,
        children: [
          { index: true, element: <HomeRoute /> },
          { path: 'settings/display', element: <DisplaySettingsRoute /> },
          { path: '*', element: <NotFoundRoute /> },
        ],
      },
    ],
    { initialEntries: [path] },
  )

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <DisplayPreferencesProvider>
          <RouterProvider router={router} />
        </DisplayPreferencesProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

let transport: FetchStub

function stubVersionEndpoint(environment: string) {
  transport.route('GET /api/version', () =>
    Promise.resolve(
      new Response(JSON.stringify(versionPayload({ environment })), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    ),
  )
}

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  // Nobody signs in on this shell's own tests; a real session is exercised in auth/*.test.tsx and in
  // the preferences leak test. Overridden per test with `transport.route(...)` where it matters.
  transport.route('GET /api/v1/me', () => problemResponse(401, 'identity.session-required'))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  document.documentElement.removeAttribute('data-shell')
  document.documentElement.removeAttribute('data-keyboard')
})

describe('App shell', () => {
  it('renders the landmarks the keyboard and screen-reader journeys depend on', async () => {
    stubVersionEndpoint('development')

    renderAt('/')

    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Home')
    expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute(
      'href',
      '#main-content',
    )
    expect(screen.getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main-content')
    expect(await screen.findByText(/TRAINING/)).toBeInTheDocument()
    expect(await screen.findByText(/build 0abcdef/)).toBeInTheDocument()
  })

  it('keeps help and display settings in the shell on every screen', async () => {
    stubVersionEndpoint('production')

    renderAt('/')

    await screen.findByRole('heading', { level: 1 })
    expect(screen.getByRole('link', { name: 'Help' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Display settings' })).toHaveAttribute(
      'href',
      '/settings/display',
    )
  })

  it('sets the document language from the active locale', async () => {
    stubVersionEndpoint('production')

    renderAt('/')

    await screen.findByRole('heading', { level: 1 })
    expect(document.documentElement.lang).toBe('en-IN')
  })

  it('names the running layout on the document, so html-level tokens can follow it', async () => {
    stubVersionEndpoint('production')

    renderAt('/')

    await screen.findByRole('heading', { level: 1 })
    expect(document.documentElement).toHaveAttribute('data-shell', 'desktop')
  })

  it('shows the not-found page for an unknown path, inside the shell', async () => {
    stubVersionEndpoint('development')

    renderAt('/no-such-page')

    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Page not found')
    expect(screen.getByRole('link', { name: 'Go to the home page' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
  })

  it('reaches the display-preferences screen through the shell', async () => {
    stubVersionEndpoint('production')

    renderAt('/settings/display')

    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Display settings')
    const themeGroup = screen.getByRole('group', { name: /Theme/ })
    expect(within(themeGroup).getByRole('radio', { name: 'Follow the device' })).toBeChecked()
  })
})
