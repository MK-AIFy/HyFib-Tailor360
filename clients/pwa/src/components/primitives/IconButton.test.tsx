import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { IconButton } from './IconButton'

describe('IconButton', () => {
  it('takes its accessible name from the required label', async () => {
    const { getByRole, container } = renderWithProviders(
      <IconButton name="close" label="Dismiss this message" />,
    )

    expect(getByRole('button', { name: 'Dismiss this message' })).toBeInTheDocument()
    await expectNoAccessibilityViolations(container)
  })

  it('renders the name as text in the DOM, not as an aria-label', () => {
    const { getByRole, getByText } = renderWithProviders(
      <IconButton name="receipt" label="Print label" />,
    )

    // Text rather than an attribute: it comes through the message catalogue like every other string,
    // so it is translated, and a test asserts the same string a screen reader will read.
    expect(getByText('Print label')).toBeInTheDocument()
    expect(getByRole('button')).not.toHaveAttribute('aria-label')
  })

  it('lets a row action name its row, which is what stops eleven identical Prints in a queue', () => {
    const { getByRole } = renderWithProviders(
      <IconButton name="receipt" label="Print label, job J-CBE01-2627-000512-01" />,
    )

    // Checklist item A11Y-60.
    expect(
      getByRole('button', { name: 'Print label, job J-CBE01-2627-000512-01' }),
    ).toBeInTheDocument()
  })

  it('keeps the glyph out of the accessible name', () => {
    const { container, getByRole } = renderWithProviders(
      <IconButton name="filter" label="Show filters" />,
    )

    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    expect(getByRole('button')).toHaveAccessibleName('Show filters')
  })

  it('does not act when unavailable but stays reachable from the keyboard', async () => {
    const onClick = vi.fn()
    const { getByRole } = renderWithProviders(
      <IconButton name="close" label="Remove" unavailable onClick={onClick} />,
    )

    await userEvent.click(getByRole('button'))

    expect(onClick).not.toHaveBeenCalled()
    expect(getByRole('button')).not.toBeDisabled()
  })
})
