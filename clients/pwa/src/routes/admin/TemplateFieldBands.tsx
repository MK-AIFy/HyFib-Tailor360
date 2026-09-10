import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { FractionInput } from '../../design-system/components/forms/FractionInput'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import { formatMeasurementRange } from '../../design-system/components/forms/measurementRange'
import type { MeasurementDisplayUnit } from '../../design-system/components/forms/measurement'
import { centimetresToMillimetres, millimetresToCentimetres } from '../../i18n/units'
import type { CentimetreDecimals, InchFractionStep } from '../../i18n/units'
import { BAND_KEYS, hasBounds } from '../../admin/templateFieldForm'
import type { BandKey, FieldFormState } from '../../admin/templateFieldForm'
import type { MessageKey } from '../../i18n/en-IN'

/**
 * The four numbers that decide what a tailor may enter, said the way a tailor reads them.
 *
 * ## Why this is four controls and not one
 *
 * Nothing in the design system enters a *pair*, let alone this quad. `MeasurementField` is a
 * single-value capture control with bounds it enforces; what a template author needs is to set those
 * bounds, which is the same numbers with the opposite direction of authority. So each bound is an
 * ordinary entry control, and the quad is held together by the fieldset and by the rules below.
 *
 * ## Why the numbers are converted here and nowhere else
 *
 * The form holds canonical millimetres, exactly as `MeasurementField` does, and the conversion
 * happens once at each control. Holding display units in the form would convert on every keystroke
 * and again on every render, and a value that is converted twice drifts by a rounding step every
 * time somebody opens the form and saves it without touching anything. The rounding is `#100`'s,
 * which is the server's, so a bound typed as 14 1/2 in reaches storage as 368.30 mm rather than
 * near it — and reads back as 14 1/2 in rather than 14 15/32.
 *
 * ## Why inches are never a decimal box
 *
 * `docs/prd/measurement-templates.md` section 2: a tailor works in halves, quarters, eighths and
 * sixteenths, because that is how a tape is divided and how the measurement is spoken. 3.19 in is
 * not a number anybody can find on a tape. `FractionInput` is a whole-number box plus a segmented
 * fraction strip, both operable from the keyboard, so 14½ in can be set without a pointer.
 *
 * ## Why "accept any measurement" is a control rather than four empty boxes
 *
 * The two hard bounds are not nullable on the wire, and the only way to say "no bounds" is the
 * `ValidationBands.None` sentinel — exactly `0 / 0 / null / null`. Clearing one box would therefore
 * be a half-stated range the server refuses, so the state is set as a whole, and the sentence says
 * which state the field is in rather than leaving it to be inferred from empty boxes.
 */
export interface TemplateFieldBandsProps {
  readonly form: FieldFormState
  /** Which of the field's units the four numbers are being read in. */
  readonly unit: MeasurementDisplayUnit
  /** The units this field declares a precision for. One control when there is only one. */
  readonly available: readonly MeasurementDisplayUnit[]
  readonly onUnitChange: (unit: MeasurementDisplayUnit) => void
  readonly onChange: (form: FieldFormState) => void
  /** The refusal for each control, when there is one. */
  readonly errors: Readonly<Partial<Record<BandKey, string>>>
  readonly controlId: (name: string) => string
}

const LABELS: Readonly<Record<BandKey, MessageKey>> = {
  minimumMillimetres: 'admin.field.bands.minimum',
  warnBelowMillimetres: 'admin.field.bands.warnBelow',
  warnAboveMillimetres: 'admin.field.bands.warnAbove',
  maximumMillimetres: 'admin.field.bands.maximum',
}

export function TemplateFieldBands(props: TemplateFieldBandsProps) {
  const { form, unit, available, onUnitChange, onChange, errors, controlId } = props
  const intl = useIntl()

  const declared = hasBounds(form)
  const step = (form.inchFraction > 0 ? form.inchFraction : 8) as InchFractionStep
  const decimals = (form.centimetreDecimals > 0 ? form.centimetreDecimals : 1) as CentimetreDecimals

  /** The stored value as the control shows it, or undefined for a bound nobody has set. */
  const toControl = (millimetres: number | null): number | undefined => {
    if (millimetres === null) {
      return undefined
    }
    // `FractionInput` reads and writes millimetres itself, so only the centimetre control converts.
    return unit === 'in' ? millimetres : millimetresToCentimetres(millimetres, decimals)
  }

  const fromControl = (value: number | undefined): number | null => {
    if (value === undefined) {
      return null
    }
    return unit === 'in' ? value : centimetresToMillimetres(value)
  }

  /** An unset bound is an absent prop, not an undefined one, under `exactOptionalPropertyTypes`. */
  const valueProp = (value: number | undefined): { value?: number } =>
    value === undefined ? {} : { value }

  const set = (key: BandKey, value: number | undefined): void => {
    onChange({ ...form, [key]: fromControl(value) })
  }

  const summary =
    declared && form.minimumMillimetres !== null && form.maximumMillimetres !== null
      ? formatMeasurementRange(
          intl,
          {
            minimumMillimetres: form.minimumMillimetres,
            maximumMillimetres: form.maximumMillimetres,
          },
          { unit, step, decimals },
        )
      : undefined

  return (
    <fieldset>
      <legend>{intl.formatMessage({ id: 'admin.field.bands' })}</legend>
      <p>{intl.formatMessage({ id: 'admin.field.bands.hint' })}</p>

      {declared ? null : (
        <>
          <p>{intl.formatMessage({ id: 'admin.field.bands.none' })}</p>
          <Button
            onClick={() => {
              // Both bounds at once: a half-stated range is not something the server can be told.
              onChange({ ...form, minimumMillimetres: 0, maximumMillimetres: 0 })
            }}
            type="button"
            variant="secondary"
          >
            {intl.formatMessage({ id: 'admin.field.bands.declare' })}
          </Button>
        </>
      )}

      {declared ? (
        <>
          {available.length > 1 ? (
            <RadioGroup
              description={intl.formatMessage({ id: 'admin.field.bands.unit.hint' })}
              id={controlId('bandUnit')}
              label={intl.formatMessage({ id: 'admin.field.bands.unit' })}
              name="bandUnit"
              onValueChange={(next) => {
                onUnitChange(next as MeasurementDisplayUnit)
              }}
              options={available.map((candidate) => ({
                value: candidate,
                label: intl.formatMessage({
                  id:
                    candidate === 'in' ? 'admin.field.bands.unit.in' : 'admin.field.bands.unit.cm',
                }),
              }))}
              value={unit}
            />
          ) : null}

          {BAND_KEYS.map((key) =>
            unit === 'in' ? (
              <FractionInput
                key={key}
                id={controlId(key)}
                label={intl.formatMessage({ id: LABELS[key] })}
                name={key}
                onValueChange={(next) => {
                  set(key, next)
                }}
                step={step}
                unit={{
                  symbol: intl.formatMessage({ id: 'units.inch.symbol' }),
                  label: intl.formatMessage({ id: 'units.inch.label' }),
                }}
                {...valueProp(toControl(form[key]))}
                {...(errors[key] === undefined ? {} : { error: errors[key] })}
              />
            ) : (
              <NumericStepper
                key={key}
                decimalPlaces={decimals}
                id={controlId(key)}
                label={intl.formatMessage({ id: LABELS[key] })}
                name={key}
                onValueChange={(next) => {
                  set(key, next)
                }}
                step={1 / 10 ** decimals}
                unit={{
                  symbol: intl.formatMessage({ id: 'units.centimetre.symbol' }),
                  label: intl.formatMessage({ id: 'units.centimetre.label' }),
                }}
                {...valueProp(toControl(form[key]))}
                {...(errors[key] === undefined ? {} : { error: errors[key] })}
              />
            ),
          )}

          {summary === undefined ? null : <p>{summary}</p>}

          <Button
            onClick={() => {
              // The sentinel is set as a whole. Clearing the bounds and leaving a threshold behind
              // would be refused, so the threshold goes with them.
              onChange({
                ...form,
                minimumMillimetres: null,
                maximumMillimetres: null,
                warnBelowMillimetres: null,
                warnAboveMillimetres: null,
              })
            }}
            type="button"
            variant="secondary"
          >
            {intl.formatMessage({ id: 'admin.field.bands.clear' })}
          </Button>
        </>
      ) : null}
    </fieldset>
  )
}
