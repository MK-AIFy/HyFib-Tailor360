import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { versionPayload } from '../app/testing/versionFixture'
import { renderWithProviders } from '../design-system/testing/renderWithProviders'
import { AboutRoute } from './AboutRoute'

const BUILD = versionPayload({ current: '0.1.0-alpha.42', environment: 'staging' })

function stubVersionEndpoint(response: () => Promise<Response>) {
  const fetchMock = vi.fn(response)
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function ok() {
  return Promise.resolve(
    new Response(JSON.stringify(BUILD), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
  )
}

function renderAbout() {
  return renderWithProviders(
    <MemoryRouter>
      <AboutRoute />
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AboutRoute', () => {
  it('shows the version, build and environment the server reported', async () => {
    stubVersionEndpoint(ok)

    renderAbout()

    // Found by their labels rather than by position, because the point of the screen is that
    // somebody reads these three out over a telephone.
    expect(await screen.findByText(BUILD.current)).toBeInTheDocument()
    expect(screen.getByText(BUILD.commit!)).toBeInTheDocument()
    expect(screen.getByText(BUILD.environment)).toBeInTheDocument()
    expect(screen.getByText(BUILD.schemaVersion)).toBeInTheDocument()
    expect(screen.getByText('Version')).toBeInTheDocument()
    expect(screen.getByText('Build')).toBeInTheDocument()
    expect(screen.getByText('Environment')).toBeInTheDocument()
    expect(screen.getByText('Data version')).toBeInTheDocument()
  })

  it('names what is loading rather than saying only "loading"', () => {
    stubVersionEndpoint(() => new Promise<Response>(() => {}))

    renderAbout()

    // Checklist item A11Y-44: a busy state has to say which part of the screen is busy.
    expect(screen.getByText(/the build information/i)).toBeInTheDocument()
  })

  it('reports an unreachable server without claiming to know why', async () => {
    stubVersionEndpoint(() => Promise.reject(new Error('offline')))

    renderAbout()

    expect(await screen.findByText(/build information could not be read/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
  })

  it('reads the endpoint again when the person retries, and shows the answer', async () => {
    let attempt = 0
    const fetchMock = stubVersionEndpoint(() => {
      attempt += 1
      return attempt === 1 ? Promise.reject(new Error('offline')) : ok()
    })

    renderAbout()

    await userEvent.click(await screen.findByRole('button', { name: /try again/i }))

    expect(await screen.findByText(BUILD.current)).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('says how the application was opened, so an installed-mode report can be told apart', async () => {
    stubVersionEndpoint(ok)

    renderAbout()

    // jsdom's matchMedia answers false to everything, which is a browser tab.
    expect(await screen.findByText('Browser tab')).toBeInTheDocument()
    expect(screen.getByText('Opened as')).toBeInTheDocument()
  })

  it('tells the person not to put customer details in a support message', async () => {
    stubVersionEndpoint(ok)

    renderAbout()

    // The fastest route to personal data leaving the shop is a screenshot attached to a support
    // request, so the warning sits on the screen that invites one.
    expect(
      await screen.findByText(/Do not put a customer name, phone number or measurement/i),
    ).toBeInTheDocument()
  })

  it('links to the Install screen', async () => {
    stubVersionEndpoint(ok)

    renderAbout()

    expect(await screen.findByRole('link', { name: /How to install/i })).toHaveAttribute(
      'href',
      '/install',
    )
  })
})
