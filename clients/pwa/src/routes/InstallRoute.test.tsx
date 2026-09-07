import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders } from '../design-system/testing/renderWithProviders'
import { InstallInstructions, InstallRoute } from './InstallRoute'
import { INSTALL_PLATFORMS } from './installPlatform'
import type { InstallPlatform } from './installPlatform'
import type { InstallPromptState } from './useInstallPrompt'

function promptState(overrides: Partial<InstallPromptState> = {}): InstallPromptState {
  return {
    available: false,
    installed: false,
    busy: false,
    outcome: 'idle',
    promptToInstall: () => {},
    ...overrides,
  }
}

function renderInstructions(platform: InstallPlatform, prompt = promptState()) {
  return renderWithProviders(
    <MemoryRouter>
      <InstallInstructions platform={platform} prompt={prompt} />
    </MemoryRouter>,
  )
}

describe('InstallInstructions', () => {
  it.each(INSTALL_PLATFORMS)('gives %s a heading and something to do', (platform) => {
    renderInstructions(platform)

    // Every branch is a real answer, not a shrug: there is always a heading and always a
    // paragraph, including for a browser that cannot install at all.
    expect(screen.getByRole('heading', { level: 2 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2 }).textContent?.length ?? 0).toBeGreaterThan(0)
  })

  it('gives an iPhone the Share menu steps in order, because no button can be offered', () => {
    renderInstructions('ios-safari')

    const steps = screen.getByRole('list', { name: 'Steps to install' })
    const items = within(steps).getAllByRole('listitem')
    expect(items).toHaveLength(3)
    expect(items[0]).toHaveTextContent('Share button in the Safari toolbar')
    expect(items[1]).toHaveTextContent('Add to Home Screen')
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('tells a non-Safari browser on iOS to open the page in Safari', () => {
    renderInstructions('ios-other')

    // The mitigation docs/nfr/support-matrix.md section 6 records for this accepted limitation.
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Safari')
    expect(screen.getByText(/only Safari can install an application/i)).toBeInTheDocument()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('offers the install button on Android once the browser has made the offer', async () => {
    const promptToInstall = vi.fn()
    renderInstructions('android', promptState({ available: true, promptToInstall }))

    const button = screen.getByRole('button', { name: 'Install Tailor360' })
    await userEvent.click(button)

    expect(promptToInstall).toHaveBeenCalledOnce()
  })

  it('still shows the browser-menu route when no offer has been made', () => {
    renderInstructions('android')

    // The button is the shortcut, never the only path: beforeinstallprompt fires once, and a
    // person whose browser has already spent it would otherwise be left with nothing to do.
    expect(screen.queryByRole('button', { name: 'Install Tailor360' })).toBeNull()
    expect(screen.getByText(/open the browser menu/i)).toBeInTheDocument()
  })

  it('shows the browser-menu route beside the button as well, not only instead of it', () => {
    renderInstructions('android', promptState({ available: true }))

    expect(screen.getByRole('button', { name: 'Install Tailor360' })).toBeInTheDocument()
    expect(screen.getByText(/open the browser menu/i)).toBeInTheDocument()
  })

  it('says so politely when the person dismissed the prompt', () => {
    renderInstructions('desktop', promptState({ outcome: 'dismissed' }))

    const notice = screen.getByRole('status')
    expect(notice).toHaveTextContent('Installation was not completed')
  })
})

describe('InstallRoute', () => {
  it('warns that installing does not make the shop work without a connection', () => {
    renderWithProviders(
      <MemoryRouter>
        <InstallRoute />
      </MemoryRouter>,
    )

    // The single most important sentence on the screen. There is no service worker in this issue
    // (#51 owns it), and a Delivery Staff member who believes a doorstep confirmation will be kept
    // offline is a data problem rather than a disappointment.
    expect(
      screen.getByText(/Installing does not make the shop work without a connection/i),
    ).toBeInTheDocument()
    expect(screen.getByText(/still needs the network for every action/i)).toBeInTheDocument()
  })

  it('links to the About screen, where the build is reported from', () => {
    renderWithProviders(
      <MemoryRouter>
        <InstallRoute />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: /About screen/i })).toHaveAttribute('href', '/about')
  })

  it('has exactly one first-level heading', () => {
    renderWithProviders(
      <MemoryRouter>
        <InstallRoute />
      </MemoryRouter>,
    )

    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
  })
})
