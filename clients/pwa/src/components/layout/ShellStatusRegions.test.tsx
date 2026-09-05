import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { ReactNode } from 'react'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import type { RenderWithProvidersOptions } from '../../design-system/testing/renderWithProviders'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { ShellStatusProvider } from './ShellStatusProvider'
import type { ShellStatusProviderProps } from './ShellStatusProvider'
import { ShellStatusRegions } from './ShellStatusRegions'
import { useShellStatus } from './useShellStatus'

function renderRegions(
  providerProps: Omit<ShellStatusProviderProps, 'children'> = {},
  extra: ReactNode = null,
  options?: RenderWithProvidersOptions,
) {
  return renderWithProviders(
    <ShellStatusProvider {...providerProps}>
      <ShellStatusRegions />
      {extra}
    </ShellStatusProvider>,
    options,
  )
}

/** Publishes a scan result the way a scanning screen will. */
function ScanProbe() {
  const { announceScan } = useShellStatus()

  return (
    <button
      type="button"
      onClick={() => {
        announceScan({
          outcome: 'accepted',
          message: 'J-CBE01-2627-000512-01 — ready for cutting.',
        })
      }}
    >
      Simulate a scan
    </button>
  )
}

describe('ShellStatusRegions', () => {
  it('mounts every region before there is anything to say', () => {
    // A live region inserted at the moment its content arrives announces nothing. All four are in
    // the tree from the first paint, empty, and take no space while they are.
    const { container } = renderRegions()

    for (const channel of ['scan-rejected', 'scan-accepted', 'sync', 'autosave']) {
      const region = container.querySelector(`[data-channel="${channel}"]`)
      expect(region).not.toBeNull()
      expect(region?.textContent).toBe('')
    }
  })

  it('announces an accepted scan politely', () => {
    // Checklist item A11Y-40: acceptance is announced without interrupting what is being read.
    const { container } = renderRegions({
      initialScan: { outcome: 'accepted', message: 'J-CBE01-2627-000512-01 — ready for cutting.' },
    })

    const region = container.querySelector('[data-channel="scan-accepted"]')
    expect(region).toHaveAttribute('role', 'status')
    expect(region?.textContent).toContain('J-CBE01-2627-000512-01')
    expect(region?.textContent).toContain('Scan accepted')
  })

  it('announces a rejected scan assertively, and names what failed', () => {
    // Checklist item A11Y-41: a rejection that waits its turn is heard after the garment has moved.
    const { container } = renderRegions({
      initialScan: { outcome: 'rejected', message: 'This job belongs to another branch.' },
    })

    const region = container.querySelector('[data-channel="scan-rejected"]')
    expect(region).toHaveAttribute('role', 'alert')
    expect(region?.textContent).toContain('This job belongs to another branch.')
    expect(container.querySelector('[data-channel="scan-accepted"]')?.textContent).toBe('')
  })

  it('keeps the message on the screen until it is dismissed', async () => {
    // Never a toast: accessibility-localisation.md section 6 and checklist item A11Y-42.
    const user = userEvent.setup()
    const { container, getByRole } = renderRegions({
      initialScan: { outcome: 'accepted', message: 'J-CBE01-2627-000512-01 — ready for cutting.' },
    })

    await user.click(getByRole('button', { name: /Dismiss/ }))

    expect(container.querySelector('[data-channel="scan-accepted"]')?.textContent).toBe('')
  })

  it('gives the sync state no dismiss control at all', () => {
    // Section 8.3: a persistent, non-dismissible network state. A queued scan the person has
    // dismissed is a queued scan they have forgotten.
    const { container, queryByRole } = renderRegions({
      initialSync: { tone: 'warning', message: '3 scans are waiting to be sent.' },
    })

    expect(container.querySelector('[data-channel="sync"]')?.textContent).toContain('3 scans')
    expect(queryByRole('button', { name: /Dismiss/ })).toBeNull()
  })

  it('carries the draft state in its own region', () => {
    // "Nothing is happening" and "it failed" sound identical otherwise (checklist item A11Y-44).
    const { container } = renderRegions({ initialAutosave: 'Saved at 3:42 pm.' })

    expect(container.querySelector('[data-channel="autosave"]')?.textContent).toContain(
      'Saved at 3:42 pm.',
    )
  })

  it('lets a screen publish into a region it does not own', async () => {
    const user = userEvent.setup()
    const { container, getByRole } = renderRegions({}, <ScanProbe />)

    await user.click(getByRole('button', { name: 'Simulate a scan' }))

    expect(container.querySelector('[data-channel="scan-accepted"]')?.textContent).toContain(
      'J-CBE01-2627-000512-01',
    )
  })

  it('has no accessibility violations with every channel full', async () => {
    const { container } = renderRegions({
      initialScan: { outcome: 'rejected', message: 'This job belongs to another branch.' },
      initialSync: { tone: 'warning', message: '3 scans are waiting to be sent.' },
      initialAutosave: 'Saved at 3:42 pm.',
    })

    await expectNoAccessibilityViolations(container)
  })

  it('renders in the pseudo-locale', () => {
    const { container } = renderRegions({ initialAutosave: 'Saved at 3:42 pm.' }, null, {
      locale: PSEUDO_LOCALE,
    })

    // The channel's own word is translated; the message a screen supplied is not this layer's.
    expect(container.querySelector('[data-channel="autosave"]')?.textContent).not.toContain(
      'Draft:',
    )
  })
})

describe('useShellStatus', () => {
  it('fails loudly outside a provider', () => {
    // The alternative is a rejected scan published into nothing, which looks like it worked.
    function Orphan() {
      useShellStatus()
      return null
    }

    expect(() => renderWithProviders(<Orphan />)).toThrow(/ShellStatusProvider/)
  })
})
