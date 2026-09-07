import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { NetworkStatusBanner } from './NetworkStatusBanner'
import type { NetworkState } from './useNetworkState'

const online: NetworkState = { online: true, restored: false, acknowledgeRestored: () => undefined }
const offline: NetworkState = {
  online: false,
  restored: false,
  acknowledgeRestored: () => undefined,
}
const restored: NetworkState = {
  online: true,
  restored: true,
  acknowledgeRestored: () => undefined,
}

describe('NetworkStatusBanner', () => {
  it('keeps its live region on the page even with nothing to say', () => {
    // A live region inserted at the moment its content appears is the commonest reason an
    // announcement is missed: the region has to exist before it changes.
    const { getByRole } = renderWithProviders(<NetworkStatusBanner state={online} />)

    const region = getByRole('status')
    expect(region).toBeInTheDocument()
    expect(region).toBeEmptyDOMElement()
  })

  it('says the connection has gone, and what still works', () => {
    const { getByRole } = renderWithProviders(<NetworkStatusBanner state={offline} />)

    const region = getByRole('status')
    expect(region).toHaveTextContent('No connection')
    // Not merely "offline": a person needs to know which half of their job still works.
    expect(region).toHaveTextContent(/taking payment, posting an invoice and moving stock/i)
  })

  it('offers no way to dismiss the offline message', () => {
    // Section 8.3 calls it "a persistent, non-dismissible network banner", and checklist item
    // A11Y-OF-01 asks whether it stays until the connection returns. There is no prop for this.
    const { queryByRole } = renderWithProviders(<NetworkStatusBanner state={offline} />)

    expect(queryByRole('button')).toBeNull()
  })

  it('states the return rather than merely stopping the warning', () => {
    const { getByRole } = renderWithProviders(<NetworkStatusBanner state={restored} />)

    expect(getByRole('status')).toHaveTextContent('Connection returned')
  })

  it('lets the person put the restored message away, with no timer doing it for them', async () => {
    const acknowledgeRestored = vi.fn()
    const { getByRole } = renderWithProviders(
      <NetworkStatusBanner state={{ ...restored, acknowledgeRestored }} />,
    )

    await userEvent.click(getByRole('button', { name: 'Hide this message' }))

    expect(acknowledgeRestored).toHaveBeenCalledTimes(1)
  })

  it('carries the detail a later issue adds inside the same region', () => {
    // #51 adds the queued count and the "not yet sent" list. Announced with the banner rather than
    // after it, which is why it is a child rather than a sibling.
    const { getByRole } = renderWithProviders(
      <NetworkStatusBanner state={offline}>
        <p>4 scans are waiting to be sent.</p>
      </NetworkStatusBanner>,
    )

    expect(getByRole('status')).toHaveTextContent('4 scans are waiting to be sent.')
  })

  it('reads its own words from the catalogue, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(<NetworkStatusBanner state={offline} />, {
      locale: 'en-XA',
    })

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations in either state', async () => {
    const gone = renderWithProviders(<NetworkStatusBanner state={offline} />)
    await expectNoAccessibilityViolations(gone.container)
    gone.unmount()

    const back = renderWithProviders(<NetworkStatusBanner state={restored} />)
    await expectNoAccessibilityViolations(back.container)
  })
})
