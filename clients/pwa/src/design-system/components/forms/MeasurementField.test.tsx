import { describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { renderWithProviders } from '../../testing/renderWithProviders'
import { expectNoAccessibilityViolations } from '../../testing/axe'
import { MeasurementField } from './MeasurementField'
import type { MeasurementBounds } from './measurement'

/**
 * The composite field a measurement wizard is built from.
 *
 * The bounds below are `MT_BLOUSE_PATTERN.waist` from docs/prd/measurement-templates.md section
 * 9.1: hard 450–1500 mm, confirmation band 610–1220 mm. Using the real numbers is what makes the
 * expected sentences the real sentences — "Waist must be between 45.0 cm and 150.0 cm" is the
 * example docs/nfr/accessibility-localisation.md section 8.2 gives for what an error should sound
 * like, and this is where the product either says it or does not.
 */
const WAIST: MeasurementBounds = {
  minimumMillimetres: 450,
  maximumMillimetres: 1500,
  warnBelowMillimetres: 610,
  warnAboveMillimetres: 1220,
}

function Harness({ initial, unit }: { readonly initial: number; readonly unit: 'in' | 'cm' }) {
  const [value, setValue] = useState(initial)
  const [acknowledged, setAcknowledged] = useState(false)
  return (
    <MeasurementField
      acknowledged={acknowledged}
      bounds={WAIST}
      description="Round the natural waist."
      displayUnit={unit}
      label="Waist"
      name="waist"
      onAcknowledgedChange={setAcknowledged}
      onValueChange={setValue}
      required
      value={value}
    />
  )
}

describe('MeasurementField', () => {
  it('gives inches the fraction strip and centimetres the stepper', () => {
    const inches = renderWithProviders(<Harness initial={800} unit="in" />)
    expect(screen.getAllByRole('radio')).toHaveLength(8)
    inches.unmount()

    renderWithProviders(<Harness initial={800} unit="cm" />)
    expect(screen.queryAllByRole('radio')).toHaveLength(0)
    expect(screen.getByRole('button', { name: 'Increase Waist' })).toBeInTheDocument()
  })

  it('converts millimetres to the display unit and back, once at the boundary', async () => {
    const user = userEvent.setup()
    const onValueChange = vi.fn()
    renderWithProviders(
      <MeasurementField
        displayUnit="cm"
        label="Waist"
        name="waist"
        onValueChange={onValueChange}
        value={800}
      />,
    )

    // 800 mm is shown as 80.0 cm; one step of 0.5 cm sends 805 mm back.
    expect(screen.getByLabelText('Waist')).toHaveValue('80.0')
    await user.click(screen.getByRole('button', { name: 'Increase Waist' }))
    expect(onValueChange).toHaveBeenCalledWith(805)
  })

  it('renders a centimetre field at the precision it declares, and steps by a whole one at nought', async () => {
    const user = userEvent.setup()
    const onValueChange = vi.fn()
    renderWithProviders(
      <MeasurementField
        centimetreDecimals={0}
        displayUnit="cm"
        label="Waist"
        name="waist"
        onValueChange={onValueChange}
        value={800}
      />,
    )

    // A half-centimetre step cannot be expressed at nought decimals, so this field steps by one.
    expect(screen.getByLabelText('Waist')).toHaveValue('80')
    await user.click(screen.getByRole('button', { name: 'Increase Waist' }))
    expect(onValueChange).toHaveBeenCalledWith(810)
  })

  it('offers the coarser inch steps a field may declare', () => {
    renderWithProviders(
      <MeasurementField
        displayUnit="in"
        fractionStep={4}
        label="Waist"
        name="waist"
        onValueChange={() => undefined}
        value={374.65}
      />,
    )

    // Quarters, which the client's types rejected until #100.
    expect(screen.getByRole('radio', { name: '3/4' })).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: '3/8' })).not.toBeInTheDocument()
  })

  it('states the expected range in the unit on screen, before it is broken', () => {
    const centimetres = renderWithProviders(<Harness initial={800} unit="cm" />)
    expect(screen.getByLabelText('Waist')).toHaveAccessibleDescription(
      /Expected between 45\.0 cm and 150\.0 cm\./,
    )
    centimetres.unmount()

    renderWithProviders(<Harness initial={800} unit="in" />)
    expect(screen.getByLabelText('Waist — whole inches')).toHaveAccessibleDescription(
      /Expected between 17 3\/4 in and 59 in\./,
    )
  })

  it('rejects a value outside the hard bounds, in the words the document asks for', () => {
    renderWithProviders(<Harness initial={400} unit="cm" />)

    const control = screen.getByLabelText('Waist')
    expect(control).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByText('Waist must be between 45.0 cm and 150.0 cm.')).toBeInTheDocument()
  })

  it('quotes the range in inches when the reader is working in inches', () => {
    renderWithProviders(<Harness initial={400} unit="in" />)

    // The bounds exist to catch a centimetre value typed into an inch field. A message quoting
    // centimetres would not catch it, which is the whole of checklist item A11Y-ME-06.
    expect(screen.getByText('Waist must be between 17 3/4 in and 59 in.')).toBeInTheDocument()
  })

  it('warns inside the confirmation band without making the field invalid', () => {
    renderWithProviders(<Harness initial={500} unit="cm" />)

    expect(screen.getByLabelText('Waist')).not.toHaveAttribute('aria-invalid')
    expect(
      screen.getByText(
        'This is outside the usual range. Check the tape and the unit, then confirm.',
      ),
    ).toBeInTheDocument()
  })

  it('offers the acknowledgement from the keyboard and announces that it was recorded', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness initial={500} unit="cm" />)

    const acknowledge = screen.getByRole('checkbox', {
      name: /I have checked the tape and the unit/,
    })
    acknowledge.focus()
    await user.keyboard(' ')

    expect(acknowledge).toBeChecked()
    expect(screen.getByText('Recorded: Waist of 50.0 cm was checked and confirmed.')).toBeVisible()
  })

  it('offers nothing to acknowledge for an ordinary value', () => {
    renderWithProviders(<Harness initial={800} unit="cm" />)

    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('lets a server message win over the range it would have computed itself', () => {
    renderWithProviders(
      <MeasurementField
        bounds={WAIST}
        displayUnit="cm"
        error="This measurement belongs to a different template version."
        label="Waist"
        name="waist"
        value={400}
      />,
    )

    expect(
      screen.getByText('This measurement belongs to a different template version.'),
    ).toBeInTheDocument()
    expect(screen.queryByText(/must be between/)).not.toBeInTheDocument()
  })

  it('says what to type when the entry is not a number, with an example in the right unit', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness initial={800} unit="cm" />)

    const control = screen.getByLabelText('Waist')
    await user.clear(control)
    await user.type(control, 'abc')

    expect(screen.getByText('Enter Waist as a number — for example 39.1 cm.')).toBeInTheDocument()
  })

  it('passes axe in the rejected state and in the confirmation band', async () => {
    const rejected = renderWithProviders(<Harness initial={400} unit="in" />)
    await expectNoAccessibilityViolations(rejected.container)
    rejected.unmount()

    const banded = renderWithProviders(<Harness initial={500} unit="cm" />)
    await expectNoAccessibilityViolations(banded.container)
  })
})
