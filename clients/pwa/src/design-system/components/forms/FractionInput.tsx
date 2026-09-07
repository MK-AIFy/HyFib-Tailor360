import { useState } from 'react'
import { useIntl } from 'react-intl'
import { fieldDescribedBy, isFieldInvalid } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import { cx } from '../../foundations/cx'
import { parseDecimal } from '../../../i18n/parseNumber'
import type { InchFractionStep } from '../../../i18n/units'
import {
  fractionOptions,
  fractionPartsToMillimetres,
  millimetresToFractionParts,
} from './measurement'
import { formattersForLocale } from './formatting'
import { FieldGroup } from './Field'
import './forms.css'

/**
 * `15 3/8 in`. The control the whole product is built around.
 *
 * A tailor holding a tape reads a whole number and a fraction, in that order, and enters them with
 * a finger guard on. So the control is a whole-number keypad beside a segmented strip of the
 * template's fraction step — eighths for a length, sixteenths for the shaping and neckline fields
 * where a quarter inch changes the fit (docs/prd/measurement-templates.md section 2). Explicitly
 * **not** a slider: rule 4 of docs/nfr/accessibility-localisation.md section 5 forbids one, because
 * a slider cannot be hit accurately with a glove on and cannot be typed at all.
 *
 * The value crossing the boundary is canonical **millimetres**, never inches. Inches are a display
 * unit; millimetres are the only thing anything is ever stored in, and converting once at the edge
 * of the control is what stops a value being converted twice on its way to the server.
 *
 * Checklist item A11Y-ME-04 is the bar this component has to clear: the strip must be reachable and
 * choosable from the keyboard alone, and `36 1/2 in` must be heard as **one value**, not as a
 * number and an orphan fraction. Three things together do that:
 *
 *  - native radios inside a `role="radiogroup"`, so arrow keys, roving focus and "3 of 8" come from
 *    the platform rather than from a re-implementation;
 *  - an `<output>` holding the assembled value, whose implicit `role="status"` announces the whole
 *    of `15 3/8 inches` whenever either part changes. Hearing "3/8, selected" and then "15 3/8
 *    inches" is not the doubling A11Y-43 forbids: the second is the value, and it is the reason the
 *    item exists;
 *  - the same `<output>` in the `aria-describedby` of both parts, so returning to the field reads
 *    the assembled value back rather than half of it.
 */
export interface FractionInputProps extends FieldProps<number> {
  /** The template field's fraction step: 8 for eighths, 16 for sixteenths. */
  readonly step?: InchFractionStep
  /** The sentence shown when the whole-inch box holds something that is not a number. */
  readonly invalidEntryMessage?: string
}

export function FractionInput(props: FractionInputProps) {
  const { label, id, size = 'standard', value, onValueChange, step = 8 } = props
  const intl = useIntl()
  const ids = useFieldIds(id)
  const formatters = formattersForLocale(intl.locale)

  /** The typed whole-inch text while it is being typed; null the rest of the time. */
  const [draft, setDraft] = useState<string | null>(null)

  const unreadable = draft !== null && draft.trim() !== '' && parseDecimal(draft) === null
  const error = props.error ?? (unreadable ? props.invalidEntryMessage : undefined)
  /*
   * Everything downstream reads the resolved error, not the prop: an error the control raised
   * itself still has to reach `aria-invalid` and `aria-describedby`.
   */
  const resolved = { ...props, ...(error === undefined ? {} : { error }) }
  const invalid = isFieldInvalid(resolved)

  const assembledId = `${ids.control}-assembled`
  const fieldDescription = fieldDescribedBy(resolved, ids)
  const describedBy =
    fieldDescription === undefined ? assembledId : `${fieldDescription} ${assembledId}`

  const parts = millimetresToFractionParts(value ?? 0, step)
  const options = fractionOptions(step)
  /*
   * -1 rather than undefined when there is no value: the radios stay controlled and simply none of
   * them is checked. Letting them fall back to uncontrolled would make React complain the first
   * time a value arrived, which on a wizard is the first time anybody touches the field.
   */
  const selected = value === undefined ? -1 : parts.numerator / parts.denominator

  const unitSymbol = intl.formatMessage({ id: 'units.inch.symbol' })
  const unitLabel = intl.formatMessage({ id: 'units.inch.label' })

  /*
   * The visible half of the assembled value carries no unit, because the unit adornment beside the
   * strip already shows `in`. `unitLabel: ''` is how the shared formatter is asked for the number
   * without one — the alternative would be assembling "15" and "3/8" here, which is the formatting
   * by hand that docs/nfr/accessibility-localisation.md section 12 forbids.
   */
  const assembledVisible =
    value === undefined
      ? ''
      : formatters.formatMeasurement(value, { unit: 'in', step, unitLabel: '' }).trim()
  const assembledSpoken =
    value === undefined ? '' : formatters.formatMeasurement(value, { unit: 'in', step, unitLabel })

  const commit = (whole: number, numerator: number, denominator: number): void => {
    if (props.disabled === true || props.readOnly === true) {
      return
    }
    onValueChange?.(fractionPartsToMillimetres({ whole, numerator, denominator }))
  }

  return (
    <FieldGroup
      describedBy={fieldDescription}
      description={props.description}
      disabled={props.disabled}
      error={error}
      ids={ids}
      label={label}
      required={props.required}
      size={size}
      showUnitSymbol={false}
      unit={{ symbol: unitSymbol, label: unitLabel }}
      warning={props.warning}
    >
      <span className="fraction">
        <span className="fraction__row">
          <input
            aria-describedby={describedBy}
            aria-invalid={invalid ? true : undefined}
            aria-label={intl.formatMessage({ id: 'forms.fraction.wholeLabel' }, { label })}
            aria-required={props.required === true ? true : undefined}
            className={cx('field__control', 'field__control--whole')}
            disabled={props.disabled}
            id={ids.control}
            inputMode="numeric"
            name={props.name}
            onBlur={() => {
              setDraft(null)
            }}
            onChange={(event) => {
              const raw = event.target.value
              setDraft(raw)
              const parsed = parseDecimal(raw)
              if (parsed !== null) {
                // A measurement is never negative, and a fraction of a negative whole would carry
                // the wrong sign into the millimetre conversion.
                commit(Math.max(0, Math.trunc(parsed)), parts.numerator, parts.denominator)
              }
            }}
            readOnly={props.readOnly}
            type="text"
            value={draft ?? (value === undefined ? '' : String(parts.whole))}
          />
          <output className="fraction__assembled" id={assembledId}>
            <span aria-hidden="true">{assembledVisible}</span>
            <span className="visually-hidden">{assembledSpoken}</span>
          </output>
          <span aria-hidden="true" className="field__unit">
            {unitSymbol}
          </span>
        </span>
        <span
          aria-describedby={describedBy}
          aria-label={intl.formatMessage({ id: 'forms.fraction.fractionLabel' }, { label })}
          className="fraction__strip"
          role="radiogroup"
        >
          {options.map((option) => (
            <label className="fraction__option" key={option.text}>
              <input
                checked={selected === option.inches}
                className="fraction__input"
                disabled={props.disabled}
                name={`${props.name}-fraction`}
                onChange={() => {
                  commit(parts.whole, option.numerator, option.denominator)
                }}
                type="radio"
                value={option.text}
              />
              <span>{option.text}</span>
            </label>
          ))}
        </span>
      </span>
    </FieldGroup>
  )
}
