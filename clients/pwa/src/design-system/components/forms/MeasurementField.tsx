import { useState } from 'react'
import { useIntl } from 'react-intl'
import type { FieldProps } from '../../foundations/FieldProps'
import { centimetresToMillimetres, millimetresToCentimetres } from '../../../i18n/units'
import type { InchFractionStep } from '../../../i18n/units'
import {
  MEASUREMENT_EXAMPLE_MILLIMETRES,
  evaluateMeasurement,
  isConfirmationBand,
  isRejectedBand,
} from './measurement'
import type { MeasurementBounds, MeasurementDisplayUnit } from './measurement'
import { formattersForLocale } from './formatting'
import { Checkbox } from './Checkbox'
import { FractionInput } from './FractionInput'
import { NumericStepper } from './NumericStepper'
import './forms.css'

/**
 * One field of a measurement template: the shop-floor-critical control, assembled.
 *
 * It is the two entry controls plus the two guard bands of docs/prd/measurement-templates.md
 * section 7, wired together so that a screen cannot ship half of them:
 *
 *  - **Display unit.** Inches get `FractionInput` and its segmented strip; centimetres get
 *    `NumericStepper` at one decimal place. Millimetres are never shown to staff (section 2), so
 *    they are not an option here at all — they are only what crosses the boundary.
 *  - **Hard bounds** reject. The field goes invalid and the message names the expected range **in
 *    the unit on screen**, which is the whole point: the bounds exist to catch a centimetre value
 *    typed into an inch field, and a message quoting millimetres would not catch it (A11Y-ME-06).
 *  - **The confirmation band** never blocks. It shows the warning that document words — "This is
 *    outside the usual range. Check the tape and the unit, then confirm." — and offers a checkbox,
 *    because a warning that cannot be acknowledged from the keyboard cannot be acknowledged by
 *    everyone (A11Y-ME-07). Acknowledging it is announced, so the person knows it was recorded.
 *
 * The value is canonical millimetres in and out. Everything a person sees is converted on the way
 * to the control and converted back on the way out — once, at this boundary, never twice.
 *
 * What this component does **not** do is decide whether the capture may be confirmed. That is the
 * server's decision (section 7: "the server is authoritative"), and the wizard's job is to collect
 * the acknowledgements and send them. This field tells the truth about the value on screen.
 */
export interface MeasurementFieldProps extends Omit<FieldProps<number>, 'unit' | 'inputMode'> {
  /** The unit the reader has chosen. Shown as a persistent adornment on the control. */
  readonly displayUnit: MeasurementDisplayUnit
  /** The template field's inch step: 8 for a length, 16 for shaping and neckline. Inches only. */
  readonly fractionStep?: InchFractionStep
  /** The hard bounds and the confirmation band, in canonical millimetres. */
  readonly bounds?: MeasurementBounds
  /** Whether the confirmation band has been acknowledged for the current value. */
  readonly acknowledged?: boolean
  readonly onAcknowledgedChange?: (acknowledged: boolean) => void
}

export function MeasurementField(props: MeasurementFieldProps) {
  const {
    label,
    name,
    value,
    onValueChange,
    displayUnit,
    fractionStep = 8,
    bounds = {},
    acknowledged = false,
    onAcknowledgedChange,
  } = props
  const intl = useIntl()
  const formatters = formattersForLocale(intl.locale)
  const [acknowledgement, setAcknowledgement] = useState('')

  const unitSymbol = intl.formatMessage({
    id: displayUnit === 'in' ? 'units.inch.symbol' : 'units.centimetre.symbol',
  })
  const unitLabel = intl.formatMessage({
    id: displayUnit === 'in' ? 'units.inch.label' : 'units.centimetre.label',
  })

  /** A bound or a value, rendered in the unit on screen with its symbol. */
  const display = (millimetres: number): string =>
    formatters.formatMeasurement(millimetres, {
      unit: displayUnit,
      step: fractionStep,
      unitLabel: unitSymbol,
    })

  const band = value === undefined ? 'ok' : evaluateMeasurement(value, bounds)
  const { minimumMillimetres: minimum, maximumMillimetres: maximum } = bounds

  const boundsError = (): string | undefined => {
    if (!isRejectedBand(band)) {
      return undefined
    }
    if (minimum !== undefined && maximum !== undefined) {
      return intl.formatMessage(
        { id: 'forms.measurement.outOfRange' },
        { label, minimum: display(minimum), maximum: display(maximum) },
      )
    }
    if (band === 'below-minimum' && minimum !== undefined) {
      return intl.formatMessage(
        { id: 'forms.measurement.tooSmall' },
        { label, minimum: display(minimum) },
      )
    }
    if (band === 'above-maximum' && maximum !== undefined) {
      return intl.formatMessage(
        { id: 'forms.measurement.tooLarge' },
        { label, maximum: display(maximum) },
      )
    }
    return undefined
  }

  const rangeHint = (): string | undefined => {
    if (minimum !== undefined && maximum !== undefined) {
      return intl.formatMessage(
        { id: 'forms.measurement.expectedRange' },
        { minimum: display(minimum), maximum: display(maximum) },
      )
    }
    if (minimum !== undefined) {
      return intl.formatMessage(
        { id: 'forms.measurement.expectedMinimum' },
        { minimum: display(minimum) },
      )
    }
    if (maximum !== undefined) {
      return intl.formatMessage(
        { id: 'forms.measurement.expectedMaximum' },
        { maximum: display(maximum) },
      )
    }
    return undefined
  }

  // A caller-supplied message always wins: a server problem detail knows something the client does
  // not, and silently replacing it with a locally computed range would hide the real reason.
  const error = props.error ?? boundsError()
  const warning =
    props.warning ??
    (isConfirmationBand(band)
      ? intl.formatMessage({ id: 'forms.measurement.outsideUsual' })
      : undefined)

  const hint = rangeHint()
  const description =
    hint === undefined
      ? props.description
      : props.description === undefined
        ? hint
        : `${props.description} ${hint}`

  /*
   * "Enter Waist as a number — for example 15 3/8 in". The example is one canonical millimetre
   * value formatted in whichever unit is on screen, so a person typing in centimetres is never
   * shown an inch example (3.3.3 Error Suggestion, checklist item A11Y-ME-06).
   */
  const invalidEntryMessage = intl.formatMessage(
    { id: 'forms.measurement.unreadable' },
    { label, example: display(MEASUREMENT_EXAMPLE_MILLIMETRES) },
  )
  const describedByIds = props.describedByIds

  const shared = {
    label,
    name,
    invalidEntryMessage,
    ...(props.id === undefined ? {} : { id: props.id }),
    ...(props.size === undefined ? {} : { size: props.size }),
    ...(description === undefined ? {} : { description }),
    ...(error === undefined ? {} : { error }),
    ...(warning === undefined ? {} : { warning }),
    ...(props.required === undefined ? {} : { required: props.required }),
    ...(props.disabled === undefined ? {} : { disabled: props.disabled }),
    ...(props.readOnly === undefined ? {} : { readOnly: props.readOnly }),
    ...(props.autoComplete === undefined ? {} : { autoComplete: props.autoComplete }),
    ...(describedByIds === undefined ? {} : { describedByIds }),
  }

  return (
    <div className="measurement">
      {displayUnit === 'in' ? (
        <FractionInput
          {...shared}
          step={fractionStep}
          {...(value === undefined ? {} : { value })}
          {...(onValueChange === undefined ? {} : { onValueChange })}
        />
      ) : (
        <NumericStepper
          {...shared}
          decimalPlaces={1}
          showRangeHint={false}
          step={0.5}
          unit={{ symbol: unitSymbol, label: unitLabel }}
          {...(minimum === undefined ? {} : { min: millimetresToCentimetres(minimum) })}
          {...(maximum === undefined ? {} : { max: millimetresToCentimetres(maximum) })}
          {...(value === undefined ? {} : { value: millimetresToCentimetres(value) })}
          {...(onValueChange === undefined
            ? {}
            : {
                onValueChange: (centimetres: number) => {
                  onValueChange(centimetresToMillimetres(centimetres))
                },
              })}
        />
      )}

      {isConfirmationBand(band) && value !== undefined ? (
        <div className="measurement__acknowledge">
          <Checkbox
            label={intl.formatMessage({ id: 'forms.measurement.acknowledge' })}
            name={`${name}-acknowledged`}
            onValueChange={(next) => {
              onAcknowledgedChange?.(next)
              setAcknowledgement(
                next
                  ? intl.formatMessage(
                      { id: 'forms.measurement.acknowledged' },
                      { label, value: display(value) },
                    )
                  : '',
              )
            }}
            value={acknowledged}
          />
        </div>
      ) : null}

      <span className="visually-hidden" role="status">
        {acknowledgement}
      </span>
    </div>
  )
}
