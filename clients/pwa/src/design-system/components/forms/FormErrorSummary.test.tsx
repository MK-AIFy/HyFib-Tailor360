import { describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { renderWithProviders } from '../../testing/renderWithProviders'
import { expectNoAccessibilityViolations } from '../../testing/axe'
import { fieldErrorsFromProblemDetails } from '../../foundations/FieldProps'
import type { FieldErrorEntry, ValidationProblemDetails } from '../../foundations/FieldProps'
import { FormErrorSummary } from './FormErrorSummary'
import { TextField } from './TextField'

/**
 * The step-aware summary, which is the component checklist item A11Y-36 is written about: "On a
 * failed save, does focus move to the error summary, is the summary announced, and does each entry
 * move focus to its field?"
 */

const STEPS = [
  { id: 'bodice', label: 'Step 2, Bodice' },
  { id: 'sleeve', label: 'Step 3, Sleeve' },
]

const WAIST_ERROR: FieldErrorEntry = {
  name: 'waist',
  message: 'Waist must be between 45.0 cm and 150.0 cm.',
  controlId: 'waist-control',
  stepId: 'bodice',
}

const SLEEVE_ERROR: FieldErrorEntry = {
  name: 'sleeve_length',
  message: 'Sleeve length is required.',
  controlId: 'sleeve-control',
  stepId: 'sleeve',
}

/** A two-step wizard: only the fields of the open step are in the DOM, as on a real wizard. */
function WizardHarness() {
  const [step, setStep] = useState('bodice')
  return (
    <div>
      <FormErrorSummary
        currentStepId={step}
        errors={[WAIST_ERROR, SLEEVE_ERROR]}
        onNavigateToStep={setStep}
        steps={STEPS}
      />
      {step === 'bodice' ? (
        <TextField id="waist-control" label="Waist" name="waist" />
      ) : (
        <TextField id="sleeve-control" label="Sleeve length" name="sleeve_length" />
      )}
    </div>
  )
}

describe('FormErrorSummary', () => {
  it('renders nothing at all when there is nothing wrong', () => {
    const { container } = renderWithProviders(<FormErrorSummary errors={[]} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('takes focus when it appears, which is what announces it', () => {
    renderWithProviders(<FormErrorSummary errors={[WAIST_ERROR]} />)

    expect(screen.getByRole('group', { name: 'There is a problem' })).toHaveFocus()
  })

  it('counts the problems, in the plural the catalogue chooses', () => {
    const one = renderWithProviders(<FormErrorSummary errors={[WAIST_ERROR]} />)
    expect(screen.getByRole('heading', { name: 'There is a problem' })).toBeInTheDocument()
    one.unmount()

    renderWithProviders(<FormErrorSummary errors={[WAIST_ERROR, SLEEVE_ERROR]} />)
    expect(screen.getByRole('heading', { name: 'There are 2 problems' })).toBeInTheDocument()
  })

  it('moves focus to a field that is already on screen', async () => {
    const user = userEvent.setup()
    renderWithProviders(
      <div>
        <FormErrorSummary currentStepId="bodice" errors={[WAIST_ERROR]} steps={STEPS} />
        <TextField id="waist-control" label="Waist" name="waist" />
      </div>,
    )

    await user.click(screen.getByRole('button', { name: WAIST_ERROR.message }))

    expect(screen.getByLabelText('Waist')).toHaveFocus()
  })

  it('names the step an entry belongs to when it is not the step on screen', () => {
    renderWithProviders(
      <FormErrorSummary
        currentStepId="bodice"
        errors={[WAIST_ERROR, SLEEVE_ERROR]}
        steps={STEPS}
      />,
    )

    // The entry for the open step reads as itself; the one for another step says where it is.
    expect(screen.getByRole('button', { name: WAIST_ERROR.message })).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Sleeve length is required. — Step 3, Sleeve' }),
    ).toBeInTheDocument()
  })

  it('opens the step a field is on, then moves focus to it', async () => {
    const user = userEvent.setup()
    renderWithProviders(<WizardHarness />)

    expect(screen.queryByLabelText('Sleeve length')).not.toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: 'Sleeve length is required. — Step 3, Sleeve' }),
    )

    expect(screen.getByLabelText('Sleeve length')).toHaveFocus()
  })

  it('takes server problem details without ever rendering the problem itself', () => {
    const problem: ValidationProblemDetails = {
      type: 'https://tools.ietf.org/html/rfc9457',
      title: 'One or more validation errors occurred.',
      status: 400,
      detail: 'MeasurementValidationException at Tailor360.Customers.Measurements.Confirm',
      errors: {
        'measurements.waist': ['Waist must be between 45.0 cm and 150.0 cm.'],
        'measurements.unknown_field': ['This field is not on the screen.'],
      },
    }

    const entries = fieldErrorsFromProblemDetails(problem, (name) =>
      name === 'measurements.waist' ? 'waist-control' : undefined,
    )

    renderWithProviders(<FormErrorSummary errors={entries} reference="c0ffee-1234" />)

    expect(
      screen.getByRole('button', { name: 'Waist must be between 45.0 cm and 150.0 cm.' }),
    ).toBeInTheDocument()
    // A summary line that goes nowhere is worse than no line, so an unmapped field is dropped.
    expect(screen.queryByText('This field is not on the screen.')).not.toBeInTheDocument()
    // The correlation identifier is for support; the detail is a stack and never reaches a screen.
    expect(screen.getByText(/c0ffee-1234/)).toBeInTheDocument()
    expect(screen.queryByText(/MeasurementValidationException/)).not.toBeInTheDocument()
    expect(screen.queryByText(/One or more validation errors occurred/)).not.toBeInTheDocument()
  })

  it('returns to the summary on a second failed submit', () => {
    const { rerender } = renderWithProviders(
      <div>
        <FormErrorSummary errors={[WAIST_ERROR]} submissionId={1} />
        <TextField id="waist-control" label="Waist" name="waist" />
      </div>,
    )

    screen.getByLabelText('Waist').focus()
    expect(screen.getByLabelText('Waist')).toHaveFocus()

    rerender(
      <div>
        <FormErrorSummary errors={[WAIST_ERROR]} submissionId={2} />
        <TextField id="waist-control" label="Waist" name="waist" />
      </div>,
    )

    expect(screen.getByRole('group', { name: 'There is a problem' })).toHaveFocus()
  })

  it('announces politely instead of moving focus when the caller asks it not to', () => {
    renderWithProviders(<FormErrorSummary autoFocus={false} errors={[WAIST_ERROR]} />)

    // One channel or the other, never both — the doubling checklist item A11Y-43 asks about.
    expect(screen.getByRole('group', { name: 'There is a problem' })).not.toHaveFocus()
    expect(screen.getByRole('status')).toHaveTextContent('There is a problem')
  })

  it('carries no live role of its own while it is the focus target', () => {
    renderWithProviders(<FormErrorSummary errors={[WAIST_ERROR]} />)

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toBeEmptyDOMElement()
  })

  it('passes axe', async () => {
    const { container } = renderWithProviders(
      <FormErrorSummary
        currentStepId="bodice"
        errors={[WAIST_ERROR, SLEEVE_ERROR]}
        onNavigateToStep={vi.fn()}
        reference="c0ffee-1234"
        steps={STEPS}
      />,
    )

    await expectNoAccessibilityViolations(container)
  })
})
