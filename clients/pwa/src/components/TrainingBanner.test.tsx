import { act, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TrainingBanner } from './TrainingBanner'
import { useVersion } from '../app/version'
import { AppIntlProvider } from '../i18n/IntlProvider'
import { versionPayload } from '../app/testing/versionFixture'

/**
 * Renders the environment the shell has loaded. Asserting on it first makes the "no banner" test
 * meaningful: it proves the response arrived and was rendered before the absence is checked, instead
 * of passing because nothing had happened yet.
 */
function VersionProbe() {
  const version = useVersion()
  return (
    <p data-testid="probe">
      {version.status === 'ready' ? version.info.environment : version.status}
    </p>
  )
}

function stubVersionEndpoint(environment: string) {
  const fetchMock = vi.fn(() =>
    Promise.resolve(
      new Response(JSON.stringify(versionPayload({ environment })), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    ),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderShell() {
  return render(
    <AppIntlProvider locale="en-IN">
      <TrainingBanner />
      <VersionProbe />
    </AppIntlProvider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('TrainingBanner', () => {
  it('shows the training warning outside production', async () => {
    const fetchMock = stubVersionEndpoint('development')

    renderShell()

    const banner = await screen.findByRole('status')
    expect(banner).toHaveTextContent('TRAINING — not real data')
    expect(banner).toHaveTextContent('development')
    expect(fetchMock).toHaveBeenCalledWith('/api/version', expect.anything())
  })

  it('shows nothing in production', async () => {
    const fetchMock = stubVersionEndpoint('production')

    renderShell()

    expect(await screen.findByTestId('probe')).toHaveTextContent('production')
    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalled()
    })
    // Flush the state update that follows the resolved fetch before asserting an absence.
    await act(async () => {
      await Promise.resolve()
    })

    expect(screen.queryByRole('status')).toBeNull()
  })

  it('shows nothing while the version is still unknown', () => {
    // A request that never settles stands in for a slow or unreachable host.
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {})),
    )

    renderShell()

    expect(screen.queryByRole('status')).toBeNull()
  })
})
