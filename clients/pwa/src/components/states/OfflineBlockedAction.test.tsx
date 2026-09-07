import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { OfflineBlockedAction } from './OfflineBlockedAction'

describe('OfflineBlockedAction', () => {
  it('carries the blueprint’s sentence word for word', () => {
    const { getByRole } = renderWithProviders(
      <OfflineBlockedAction action="Taking a payment" online={false} />,
    )

    // Plan Section 4.6 fixes this string. It is the product's most important offline promise:
    // money is never accepted into a queue.
    expect(getByRole('status')).toHaveTextContent('Needs connection — this will not be queued')
  })

  it('names what was refused', () => {
    const { getByRole } = renderWithProviders(
      <OfflineBlockedAction action="Posting the invoice" online={false} />,
    )

    expect(getByRole('status')).toHaveTextContent('Posting the invoice needs a connection.')
  })

  it('says it will not be sent later, and that nothing typed was lost', () => {
    const { getByRole } = renderWithProviders(
      <OfflineBlockedAction action="Issuing material" online={false} />,
    )

    // Checklist item A11Y-OF-02: does it say it will not be queued, and does the typed input stay.
    const region = getByRole('status')
    expect(region).toHaveTextContent('it will not be sent later')
    expect(region).toHaveTextContent('Everything you have typed is still on the screen.')
  })

  it('announces politely, because it is a state rather than an interruption', () => {
    const { getByRole, queryByRole } = renderWithProviders(
      <OfflineBlockedAction action="Taking a payment" online={false} />,
    )

    expect(getByRole('status')).toBeInTheDocument()
    expect(queryByRole('alert')).toBeNull()
  })

  it('withholds the retry while there is still no connection', () => {
    // A Try again that cannot work teaches people to press it twice.
    const { queryByRole } = renderWithProviders(
      <OfflineBlockedAction action="Taking a payment" onRetry={() => undefined} online={false} />,
    )

    expect(queryByRole('button', { name: 'Try again' })).toBeNull()
  })

  it('offers the retry once the connection is back', async () => {
    const onRetry = vi.fn()
    const { getByRole } = renderWithProviders(
      <OfflineBlockedAction action="Taking a payment" onRetry={onRetry} online />,
    )

    await userEvent.click(getByRole('button', { name: 'Try again' }))

    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('reads its own words from the catalogue, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(
      <OfflineBlockedAction action="Taking a payment" online={false} />,
      { locale: 'en-XA' },
    )

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <OfflineBlockedAction action="Taking a payment" onRetry={() => undefined} online>
        <p>The order itself has been saved.</p>
      </OfflineBlockedAction>,
    )

    await expectNoAccessibilityViolations(container)
  })
})
