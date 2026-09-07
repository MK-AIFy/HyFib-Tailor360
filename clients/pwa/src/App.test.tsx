import { render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { App } from './App'
import { DisplaySettingsRoute, HomeRoute, NotFoundRoute } from './app/router'
import { DisplayPreferencesProvider } from './app/DisplayPreferencesProvider'
import { createInMemoryDisplayPreferencesStore } from './app/preferences'
import { AppIntlProvider } from './i18n/IntlProvider'
import { versionPayload } from './app/testing/versionFixture'

/**
 * Renders the real shell over an in-memory copy of the route table, so the test can start on any path
 * without a browser history. The route elements are the ones the browser router uses.
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
      <DisplayPreferencesProvider store={createInMemoryDisplayPreferencesStore()}>
        <RouterProvider router={router} />
      </DisplayPreferencesProvider>
    </AppIntlProvider>,
  )
}

function stubVersionEndpoint(environment: string) {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify(versionPayload({ environment })), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    ),
  )
}

afterEach(() => {
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
