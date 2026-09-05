import type { Meta, StoryObj } from '@storybook/react-vite'
import { useState } from 'react'
import { PSEUDO_LOCALE } from '../../../i18n/pseudo'
import { fieldErrorsFromProblemDetails } from '../../foundations/FieldProps'
import type { FieldErrorEntry } from '../../foundations/FieldProps'
import { FormErrorSummary } from './FormErrorSummary'
import { MeasurementField } from './MeasurementField'
import { TextField } from './TextField'
import './forms.css'

/**
 * The step-aware summary, on the wizard it exists for.
 *
 * A measurement capture is several steps long and a failed confirm produces errors from all of
 * them. The story to work through is `AcrossWizardSteps`: the summary lists a problem on a step
 * that is not on screen, names which step it is on, and — when the entry is used — opens that step
 * before moving focus to the field. A summary link that goes nowhere is worse than no link at all.
 */

const STEPS = [
  { id: 'bodice', label: 'Step 1, Bodice' },
  { id: 'sleeve', label: 'Step 2, Sleeve' },
]

const ERRORS: readonly FieldErrorEntry[] = [
  {
    name: 'waist',
    message: 'Waist must be between 45.0 cm and 150.0 cm.',
    controlId: 'waist-control',
    stepId: 'bodice',
  },
  {
    name: 'sleeve_length',
    message: 'Sleeve length is needed before the capture can be confirmed.',
    controlId: 'sleeve-control',
    stepId: 'sleeve',
  },
]

/** What an RFC 9457 validation problem from the API looks like by the time it reaches a screen. */
const SERVER_PROBLEM = {
  type: 'https://hyfib.example/problems/validation',
  title: 'One or more validation errors occurred.',
  status: 400,
  detail: 'Never rendered: this can carry an exception type or an identifier.',
  errors: {
    'measurements.waist': ['Waist must be between 45.0 cm and 150.0 cm.'],
    'customer.mobile': ['Enter a ten-digit mobile number.'],
  },
}

interface SummaryProps {
  readonly errors?: readonly FieldErrorEntry[]
  readonly reference?: string
  readonly autoFocus?: boolean
}

function SummaryOnly({ errors = ERRORS, reference, autoFocus }: SummaryProps) {
  return (
    <FormErrorSummary
      currentStepId="bodice"
      errors={errors}
      steps={STEPS}
      {...(reference === undefined ? {} : { reference })}
      {...(autoFocus === undefined ? {} : { autoFocus })}
    />
  )
}

/** A two-step wizard: only the open step's fields exist, exactly as on a real one. */
function Wizard() {
  const [step, setStep] = useState('bodice')
  const [waist, setWaist] = useState(400)
  const [sleeve, setSleeve] = useState('')

  return (
    <div className="field-stack">
      <FormErrorSummary
        currentStepId={step}
        errors={ERRORS}
        onNavigateToStep={setStep}
        steps={STEPS}
      />
      <p>{step === 'bodice' ? STEPS[0]?.label : STEPS[1]?.label}</p>
      {step === 'bodice' ? (
        <MeasurementField
          bounds={{ minimumMillimetres: 450, maximumMillimetres: 1500 }}
          description="Round the natural waist."
          displayUnit="cm"
          id="waist-control"
          label="Waist"
          name="waist"
          onValueChange={setWaist}
          required
          value={waist}
        />
      ) : (
        <TextField
          error="Sleeve length is needed before the capture can be confirmed."
          id="sleeve-control"
          inputMode="decimal"
          label="Sleeve length"
          name="sleeve_length"
          onValueChange={setSleeve}
          required
          value={sleeve}
        />
      )}
    </div>
  )
}

const meta = {
  title: 'Forms/Error summary',
  component: SummaryOnly,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof SummaryOnly>

export default meta

type Story = StoryObj<typeof meta>

/** Two problems, one of them on another step. The summary takes focus as it appears. */
export const Default: Story = {}

export const SingleProblem: Story = { args: { errors: [ERRORS[0] as FieldErrorEntry] } }

/**
 * A server problem detail mapped onto the same entries. The correlation identifier is shown for
 * support; the problem's own `title` and `detail` never reach a screen.
 */
export const FromServerProblemDetails: Story = {
  args: {
    errors: fieldErrorsFromProblemDetails(SERVER_PROBLEM, (name) =>
      name === 'measurements.waist' ? 'waist-control' : undefined,
    ),
    reference: 'a4f2c1e8-2026-09-04',
  },
}

/**
 * Focus turned off, for a form that revalidates as a person types. The polite region announces the
 * change instead — one channel or the other, never both (checklist item A11Y-43).
 */
export const AnnouncedWithoutMovingFocus: Story = { args: { autoFocus: false } }

export const AcrossWizardSteps: Story = { render: () => <Wizard /> }

export const PseudoLocale: Story = { globals: { locale: PSEUDO_LOCALE } }

export const Tamil: Story = { globals: { locale: 'ta-IN' } }

export const HighContrastSunlight: Story = { globals: { theme: 'contrast' } }

export const LargestTextSize: Story = { globals: { textSize: '150' } }

export const ReflowFloor: Story = { globals: { viewport: { value: 'reflowFloor' } } }
