import { describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { renderWithProviders } from '../../testing/renderWithProviders'
import { expectNoAccessibilityViolations } from '../../testing/axe'
import { MILLIMETRES_PER_INCH } from '../../../i18n/units'
import { FractionInput } from './FractionInput'

/**
 * `15 3/8 in`, entered with a finger guard on and heard as one value.
 *
 * These tests are the automated half of checklist items A11Y-ME-04 and A11Y-59. The half they
 * cannot answer — what TalkBack actually says at the moment the strip is used — stays a manual item
 * on the reference device, which is where the checklist puts it.
 */

/** 15 3/8 in, the worked example of docs/prd/measurement-templates.md section 2. */
const FIFTEEN_AND_THREE_EIGHTHS = 390.53

function Harness({ initial, step }: { readonly initial?: number; readonly step?: 8 | 16 }) {
  const [value, setValue] = useState<number | undefined>(initial)
  return (
    <FractionInput
      label="Blouse length"
      name="blouse_full_length"
      onValueChange={setValue}
      {...(step === undefined ? {} : { step })}
      {...(value === undefined ? {} : { value })}
    />
  )
}

describe('FractionInput', () => {
  it('shows the assembled value and speaks it with the unit word, not the symbol', () => {
    renderWithProviders(
      <FractionInput
        label="Blouse length"
        name="blouse_full_length"
        value={FIFTEEN_AND_THREE_EIGHTHS}
      />,
    )

    // Seen: the number, with the unit adornment beside it. Heard: the whole thing, in words.
    expect(screen.getByText('15 3/8')).toBeInTheDocument()
    expect(screen.getByText('15 3/8 inches')).toBeInTheDocument()
  })

  it('names the group and each part, so a screen reader hears where it is', () => {
    renderWithProviders(<FractionInput label="Blouse length" name="blouse_full_length" value={0} />)

    expect(screen.getByRole('group', { name: 'Blouse length' })).toBeInTheDocument()
    expect(
      screen.getByRole('radiogroup', { name: 'Blouse length — fraction of an inch' }),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Blouse length — whole inches')).toBeInTheDocument()
  })

  it('offers eighths by default and sixteenths when the template asks for them', () => {
    const eighths = renderWithProviders(<Harness initial={0} />)
    expect(screen.getAllByRole('radio')).toHaveLength(8)
    eighths.unmount()

    renderWithProviders(<Harness initial={0} step={16} />)
    expect(screen.getAllByRole('radio')).toHaveLength(16)
  })

  it('keeps the whole inches when the fraction changes', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness initial={15 * MILLIMETRES_PER_INCH} />)

    await user.click(screen.getByRole('radio', { name: '3/8' }))

    expect(screen.getByText('15 3/8 inches')).toBeInTheDocument()
  })

  it('keeps the fraction when the whole inches change', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness initial={FIFTEEN_AND_THREE_EIGHTHS} />)

    const whole = screen.getByLabelText('Blouse length — whole inches')
    await user.clear(whole)
    await user.type(whole, '16')

    expect(screen.getByText('16 3/8 inches')).toBeInTheDocument()
  })

  it('reports canonical millimetres, never inches', async () => {
    const user = userEvent.setup()
    const onValueChange = vi.fn()
    renderWithProviders(
      <FractionInput
        label="Blouse length"
        name="blouse_full_length"
        onValueChange={onValueChange}
        value={15 * MILLIMETRES_PER_INCH}
      />,
    )

    await user.click(screen.getByRole('radio', { name: '1/2' }))

    expect(onValueChange).toHaveBeenCalledWith(393.7)
  })

  it('is operable from the keyboard alone, strip included', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness initial={15 * MILLIMETRES_PER_INCH} />)

    await user.tab()
    expect(screen.getByLabelText('Blouse length — whole inches')).toHaveFocus()

    await user.tab()
    expect(screen.getByRole('radio', { name: '0' })).toHaveFocus()

    // Arrow keys move within a native radio group and select as they go — the behaviour a
    // re-implemented segmented control would have to earn back.
    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('radio', { name: '1/8' })).toBeChecked()
    expect(screen.getByText('15 1/8 inches')).toBeInTheDocument()
  })

  it('says what to type when the whole-inch box holds something that is not a number', async () => {
    const user = userEvent.setup()
    renderWithProviders(
      <FractionInput
        invalidEntryMessage="Enter Blouse length as a number — for example 15 3/8 in."
        label="Blouse length"
        name="blouse_full_length"
        value={0}
      />,
    )

    const whole = screen.getByLabelText('Blouse length — whole inches')
    await user.type(whole, 'abc')

    expect(whole).toHaveAttribute('aria-invalid', 'true')
    expect(
      screen.getByText('Enter Blouse length as a number — for example 15 3/8 in.'),
    ).toBeInTheDocument()
  })

  it('never reports a negative measurement, whatever is typed', async () => {
    const user = userEvent.setup()
    const onValueChange = vi.fn()
    renderWithProviders(
      <FractionInput
        label="Blouse length"
        name="blouse_full_length"
        onValueChange={onValueChange}
        value={0}
      />,
    )

    await user.type(screen.getByLabelText('Blouse length — whole inches'), '-4')

    for (const call of onValueChange.mock.calls) {
      expect(call[0]).toBeGreaterThanOrEqual(0)
    }
  })

  it('passes axe with a description, an error and a sixteenths strip', async () => {
    const { container } = renderWithProviders(
      <FractionInput
        description="Shoulder seam at the neck to the intended hem."
        error="Blouse length must be between 9 7/8 in and 35 3/8 in."
        label="Blouse length"
        name="blouse_full_length"
        required
        step={16}
        value={FIFTEEN_AND_THREE_EIGHTHS}
      />,
    )

    await expectNoAccessibilityViolations(container)
  })
})
