import { useIntl } from 'react-intl'
import type { TemplateField } from '../admin/types'
import { Alert } from '../components/primitives/Alert'
import { MeasurementField } from '../design-system/components/forms/MeasurementField'
import { NumericStepper } from '../design-system/components/forms/NumericStepper'
import { Select } from '../design-system/components/forms/Select'
import type { MeasurementDisplayUnit } from '../design-system/components/forms/measurement'
import {
  boundsOf,
  captureControlId,
  captureKindOf,
  centimetreDecimalsOf,
  fractionStepOf,
  visibilityOf,
} from './capture'
import type { CaptureState, CapturedFieldState } from './capture'

export interface CaptureFieldProps {
  readonly field: TemplateField
  readonly state: CaptureState
  readonly unit: MeasurementDisplayUnit
  /** A message that overrides the control's own — a server finding, or a required-field error. */
  readonly error?: string
  readonly onChange: (key: string, next: CapturedFieldState) => void
}

/**
 * One field of the wizard, rendered through the control the administration preview renders it
 * through — `MeasurementField` for a tape measurement, the stepper for a count, a select for a
 * choice — so what a tailor meets cannot drift from what an administrator previewed (#96, #123).
 *
 * ## The diagram is reached from the field
 *
 * A field with a diagram carries the diagram's own description in its persistent description,
 * after the help text: "Round the fullest part, tape level. Diagram: around the fullest part of the
 * chest, tape level at the back." That is checklist item A11Y-RJ-02 step 8 — the description is
 * reachable from the control, not only from the picture, so a screen-reader user holding the tape
 * hears where it goes without leaving the field. The bundled line drawings themselves arrive with
 * the media pipeline (#31); until then the words are the diagram.
 *
 * ## A hidden field is said, not omitted
 *
 * A field whose rule hides it is stated as not asked for, because a field that simply is not there
 * looks identical to one the template forgot. A field whose rule cannot be settled is shown, and
 * says why: hiding on missing information drops a measurement the tailor needs.
 */
export function CaptureField({ field, state, unit, error, onChange }: CaptureFieldProps) {
  const intl = useIntl()
  const held = state[field.key]
  const verdict = visibilityOf(field, state)
  const kind = captureKindOf(field)
  const id = captureControlId(field.key)

  if (!verdict.isShown) {
    return (
      <p className="capture__hidden">
        {intl.formatMessage({ id: 'measurements.wizard.hidden' }, { label: field.label })}
      </p>
    )
  }

  const diagram =
    field.diagramAlt === null || field.diagramAlt === ''
      ? undefined
      : intl.formatMessage({ id: 'measurements.wizard.diagram' }, { alt: field.diagramAlt })
  const help = field.helpText === '' ? undefined : field.helpText
  const description = [help, diagram].filter((part) => part !== undefined).join(' ')

  const shared = {
    id,
    label: field.label,
    name: field.key,
    required: field.isRequired,
    ...(description === '' ? {} : { description }),
    ...(error === undefined ? {} : { error }),
  }

  return (
    <div className="capture__field">
      {verdict.decided ? null : (
        <Alert live="polite" tone="info">
          {intl.formatMessage({ id: 'measurements.wizard.undecidable' }, { label: field.label })}
        </Alert>
      )}

      {kind === 'choice' ? (
        <Select
          {...shared}
          onValueChange={(next) => {
            onChange(field.key, { choice: next, acknowledged: false })
          }}
          options={field.options.map((option) => ({ value: option.code, label: option.label }))}
          value={held?.choice ?? ''}
        />
      ) : kind === 'count' ? (
        <NumericStepper
          {...shared}
          description={[description, intl.formatMessage({ id: 'measurements.wizard.count.hint' })]
            .filter((part) => part !== '')
            .join(' ')}
          decimalPlaces={0}
          min={0}
          onValueChange={(next) => {
            onChange(field.key, { millimetres: Math.round(next), acknowledged: false })
          }}
          step={1}
          {...(held?.millimetres === undefined ? {} : { value: held.millimetres })}
        />
      ) : (
        <MeasurementField
          {...shared}
          acknowledged={held?.acknowledged ?? false}
          bounds={boundsOf(field)}
          centimetreDecimals={centimetreDecimalsOf(field)}
          displayUnit={unit}
          fractionStep={fractionStepOf(field)}
          onAcknowledgedChange={(next) => {
            onChange(field.key, { ...held, acknowledged: next })
          }}
          onValueChange={(next) => {
            // A changed value is a value nobody has checked yet: the acknowledgement is for the
            // number it was given for, and does not travel to the next one.
            onChange(field.key, { millimetres: next, acknowledged: false })
          }}
          {...(held?.millimetres === undefined ? {} : { value: held.millimetres })}
        />
      )}
    </div>
  )
}
