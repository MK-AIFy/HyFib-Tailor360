import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { App } from './App'
import { HomeRoute, NotFoundRoute } from './app/router'
import { AppIntlProvider } from './i18n/IntlProvider'

/**
 * Renders the real shell over an in-memory copy of the route table, so the test can start on any path
 * without a browser history. The route elements are the ones the browser router uses.
 */
function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: <App />,
        children: [
          { index: true, element: <HomeRoute /> },
          { path: '*', element: <NotFoundRoute /> },
        ],
      },
    ],
    { initialEntries: [path] },
  )

  return render(
    <AppIntlProvider locale="en-IN">
      <RouterProvider router={router} />
    </AppIntlProvider>,
  )
}

function stubVersionEndpoint(environment: string) {
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

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
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
    expect(await screen.findByRole('status')).toHaveTextContent('TRAINING')
    expect(await screen.findByText(/build 0abcdef/)).toBeInTheDocument()
  })

  it('sets the document language from the active locale', async () => {
    stubVersionEndpoint('production')

    renderAt('/')

    await screen.findByRole('heading', { level: 1 })
    expect(document.documentElement.lang).toBe('en-IN')
  })

  it('shows the not-found page for an unknown path, inside the shell', async () => {
    stubVersionEndpoint('development')

    renderAt('/no-such-page')

    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Page not found')
    expect(screen.getByRole('link', { name: 'Go to the home page' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
  })
})
