import { describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router'
import type { ReactNode } from 'react'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { PrimaryActionFab } from './PrimaryActionFab'

function renderAt(path: string, ui: ReactNode) {
  return renderWithProviders(<MemoryRouter initialEntries={[path]}>{ui}</MemoryRouter>)
}

describe('PrimaryActionFab', () => {
  it('carries its label as text rather than as a tooltip', () => {
    // An icon-only circle with a tooltip is hover-only information, which the blueprint forbids and
    // which no touch device can show at all.
    const { getByRole } = renderAt('/', <PrimaryActionFab label="Scan" href="/scan" icon="scan" />)

    const link = getByRole('link', { name: 'Scan' })
    expect(link).toHaveTextContent('Scan')
    expect(link).not.toHaveAttribute('title')
    expect(link).not.toHaveAttribute('aria-label')
  })

  it('goes to the action rather than performing it', () => {
    const { getByRole } = renderAt('/', <PrimaryActionFab label="Scan" href="/scan" icon="scan" />)

    expect(getByRole('link', { name: 'Scan' })).toHaveAttribute('href', '/scan')
  })

  it('marks itself current when the person is already there', () => {
    // Otherwise the Scan action looks available while the scanner is what is already open.
    const { getByRole } = renderAt(
      '/scan',
      <PrimaryActionFab label="Scan" href="/scan" icon="scan" />,
    )

    expect(getByRole('link', { name: 'Scan' })).toHaveAttribute('aria-current', 'page')
  })

  it('leaves the layout and the accessibility tree together when hidden', () => {
    // 2.4.11: while the keyboard is up, the field and the form's own primary action are what matter.
    // Nothing is announced that cannot be reached.
    const { queryByRole, container } = renderAt(
      '/',
      <PrimaryActionFab label="Scan" href="/scan" icon="scan" hidden />,
    )

    expect(queryByRole('link', { name: 'Scan' })).toBeNull()
    expect(container.querySelector('.primary-action-fab')).toHaveAttribute('hidden')
  })

  it('has no accessibility violations', async () => {
    const { container } = renderAt(
      '/',
      <PrimaryActionFab label="Take payment" href="/billing/new" icon="rupee" />,
    )

    await expectNoAccessibilityViolations(container)
  })
})
