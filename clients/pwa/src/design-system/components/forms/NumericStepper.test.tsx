import { describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { renderWithProviders } from '../../testing/renderWithProviders'
import { expectNoAccessibilityViolations } from '../../testing/axe'
import { NumericStepper } from './NumericStepper'

/**
 * The repetitive-entry control: received quantity, issued quantity, a stocktake count.
 *
 * Checklist item A11Y-ME-05 wants the value announced after each step and the same value typeable.
 * Both halves are asserted here; what a screen reader does with the polite region on the reference
 * device stays a manual item.
 */

function Harness({ initial }: { readonly initial?: number }) {
  const [value, setValue] = useState<number | undefined>(initial)
  return (
    <NumericStepper
      label="Received quantity"
      max={20}
      min={0}
      name="received_quantity"
      onValueChange={setValue}
      unit={{ symbol: 'pc', label: 'pieces' }}
      {...(value === undefined ? {} : { value })}
    />
  )
}

describe('NumericStepper', () => {
  it('names its buttons for what they do, and for which field', () => {
    renderWithProviders(<Harness initial={4} />)

    expect(screen.getByRole('button', { name: 'Increase Received quantity' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Decrease Received quantity' })).toBeInTheDocument()
  })

  it('announces the value after each step, because changing an input announces nothing', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness initial={4} />)

    await user.click(screen.getByRole('button', { name: 'Increase Received quantity' }))

    expect(screen.getByRole('status')).toHaveTextContent('Received quantity, 5')
    expect(screen.getByLabelText('Received quantity')).toHaveValue('5')
  })

  it('says why nothing happened at a bound instead of failing silently', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness initial={0} />)

    const decrease = screen.getByRole('button', { name: 'Decrease Received quantity' })
    expect(decrease).toHaveAttribute('aria-disabled', 'true')
    // aria-disabled, not disabled: the button keeps its name and stays reachable from the keyboard.
    expect(decrease).toBeEnabled()

    await user.click(decrease)

    expect(screen.getByRole('status')).toHaveTextContent(
      'Received quantity is already at its lowest value, 0.',
    )
  })

  it('accepts a typed value, with either decimal separator', async () => {
    const user = userEvent.setup()
    const onValueChange = vi.fn()
    renderWithProviders(
      <NumericStepper
        decimalPlaces={1}
        label="Length"
        name="length"
        onValueChange={onValueChange}
        value={0}
      />,
    )

    const input = screen.getByLabelText('Length')
    await user.clear(input)
    await user.type(input, '36,5')

    // The Android keypad offers whichever separator the device locale prefers; a rejected 36,5 at
    // the counter is a customer waiting while somebody works out why the form will not take it.
    expect(onValueChange).toHaveBeenLastCalledWith(36.5)
  })

  it('steps with the arrow keys while focus is in the box', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness initial={4} />)

    const input = screen.getByLabelText('Received quantity')
    await user.click(input)
    await user.keyboard('{ArrowUp}{ArrowUp}{ArrowDown}')

    expect(input).toHaveValue('5')
  })

  it('states the range in the description, so it is heard before it is broken', () => {
    renderWithProviders(<Harness initial={4} />)

    expect(screen.getByLabelText('Received quantity')).toHaveAccessibleDescription(
      /Between 0 and 20\./,
    )
  })

  it('shows the unit symbol and speaks the unit word', () => {
    renderWithProviders(<Harness initial={4} />)

    expect(screen.getByText('pc')).toBeInTheDocument()
    expect(screen.getByLabelText('Received quantity')).toHaveAccessibleDescription(/pieces/)
  })

  it('does not step while disabled', async () => {
    const user = userEvent.setup()
    const onValueChange = vi.fn()
    renderWithProviders(
      <NumericStepper
        disabled
        label="Received quantity"
        name="received_quantity"
        onValueChange={onValueChange}
        value={4}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'Increase Received quantity' }))

    expect(onValueChange).not.toHaveBeenCalled()
  })

  it('passes axe with a unit, a range and an error', async () => {
    const { container } = renderWithProviders(
      <NumericStepper
        error="Received quantity must be 20 or less."
        label="Received quantity"
        max={20}
        min={0}
        name="received_quantity"
        required
        unit={{ symbol: 'pc', label: 'pieces' }}
        value={40}
      />,
    )

    await expectNoAccessibilityViolations(container)
  })
})
