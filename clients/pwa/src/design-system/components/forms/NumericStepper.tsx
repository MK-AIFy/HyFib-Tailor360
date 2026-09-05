import { useState } from 'react'
import { useIntl } from 'react-intl'
import { fieldControlAttributes } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import { cx } from '../../foundations/cx'
import { parseDecimal } from '../../../i18n/parseNumber'
import { formattersForLocale } from './formatting'
import './forms.css'
import { Field } from './Field'

/**
 * A number entered by stepping or by typing, for the repetitive counting an Inventory Clerk does
 * all morning: received quantity, issued quantity, a stocktake count, a piece count.
 *
 * Checklist item A11Y-ME-05 asks two things of it, and they pull in opposite directions: the value
 * must be announced after each step, and the same value must also be typeable. The resolution here:
 *
 *  - The control is an ordinary text box, so typing behaves like typing and a screen reader reads
 *    what was typed. It is **not** a `role="spinbutton"`, which would override the textbox role and
 *    change what is announced while a person is editing.
 *  - Stepping announces through a polite live region that only the buttons write to. Typing never
 *    writes to it, because a region that fires on every keystroke is the chatter A11Y-43 forbids
 *    and a person typing already knows what they typed.
 *  - `inputmode="decimal"` rather than `type="number"`: the value is read by `parseDecimal`, which
 *    accepts the comma an Android keypad offers as readily as the full stop.
 *
 * At a bound the button is `aria-disabled` rather than `disabled`, so it keeps its name and stays
 * reachable, and pressing it says why nothing happened instead of doing nothing silently.
 */
export interface NumericStepperProps extends FieldProps<number> {
  readonly min?: number
  readonly max?: number
  /** How much one press moves the value. Defaults to 1. */
  readonly step?: number
  /** Decimal places for the displayed value. Defaults to 0 — a count is a whole number. */
  readonly decimalPlaces?: number
  /** Adds "Between {minimum} and {maximum}." to the description. On by default when both are set. */
  readonly showRangeHint?: boolean
  /**
   * The sentence shown when what has been typed is not a number at all.
   *
   * Optional, because without it the control still gives feedback — the typed text stays on screen,
   * the value does not move, and leaving the field snaps back to the last good value. With it, the
   * field additionally says what to type instead, which is what 3.3.3 Error Suggestion asks for and
   * what a measurement field always supplies.
   */
  readonly invalidEntryMessage?: string
}

export function NumericStepper(props: NumericStepperProps) {
  const {
    label,
    id,
    size = 'standard',
    value,
    onValueChange,
    min,
    max,
    step = 1,
    decimalPlaces = 0,
    showRangeHint = true,
  } = props
  const intl = useIntl()
  const ids = useFieldIds(id)
  const formatters = formattersForLocale(intl.locale)

  /**
   * What the person is typing, while they are typing it. Null the rest of the time, so the box
   * shows the canonically formatted value and no effect has to keep two copies in step.
   */
  const [draft, setDraft] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState('')

  const format = (candidate: number): string => formatters.formatNumber(candidate, decimalPlaces)
  const displayed = draft ?? (value === undefined ? '' : format(value))

  const unreadable = draft !== null && draft.trim() !== '' && parseDecimal(draft) === null
  // A caller-supplied error wins: the server knows things the keypad does not.
  const error = props.error ?? (unreadable ? props.invalidEntryMessage : undefined)

  const atMinimum = min !== undefined && value !== undefined && value <= min
  const atMaximum = max !== undefined && value !== undefined && value >= max

  const announce = (messageId: string, values: Record<string, string>): void => {
    setAnnouncement(intl.formatMessage({ id: messageId }, { label, ...values }))
  }

  const moveBy = (delta: number): void => {
    if (props.disabled === true || props.readOnly === true) {
      return
    }
    if (delta < 0 && atMinimum && min !== undefined) {
      announce('forms.stepper.atMinimum', { value: format(min) })
      return
    }
    if (delta > 0 && atMaximum && max !== undefined) {
      announce('forms.stepper.atMaximum', { value: format(max) })
      return
    }

    const base = value ?? min ?? 0
    const raised = min === undefined ? base + delta : Math.max(base + delta, min)
    const next = max === undefined ? raised : Math.min(raised, max)
    // Steps of 0.1 accumulate binary error; the field's own precision is the right place to stop.
    const rounded = Number(next.toFixed(Math.max(decimalPlaces, 0)))

    setDraft(null)
    onValueChange?.(rounded)
    announce('forms.stepper.announce', { value: format(rounded) })
  }

  const rangeHint =
    showRangeHint && min !== undefined && max !== undefined
      ? intl.formatMessage(
          { id: 'forms.stepper.range' },
          { minimum: format(min), maximum: format(max) },
        )
      : undefined
  const description =
    rangeHint === undefined
      ? props.description
      : props.description === undefined
        ? rangeHint
        : `${props.description} ${rangeHint}`

  /*
   * The aria wiring is computed from the *resolved* error and description, never from the raw
   * props. Both can be produced by the control itself — the range hint here, the "not a number"
   * message above — and a message that is rendered but not referenced by `aria-describedby` is a
   * message a screen-reader user never hears. That is the failure mode this line exists to stop.
   */
  const control = fieldControlAttributes(
    {
      ...props,
      ...(error === undefined ? {} : { error }),
      ...(description === undefined ? {} : { description }),
    },
    ids,
  )

  return (
    <Field
      description={description}
      disabled={props.disabled}
      error={error}
      ids={ids}
      label={label}
      required={props.required}
      size={size}
      unit={props.unit}
      warning={props.warning}
    >
      <span className="stepper">
        <button
          aria-controls={ids.control}
          aria-disabled={atMinimum ? true : undefined}
          aria-label={intl.formatMessage({ id: 'forms.stepper.decrease' }, { label })}
          className="stepper__button"
          disabled={props.disabled}
          onClick={() => {
            moveBy(-step)
          }}
          type="button"
        >
          <span aria-hidden="true">&minus;</span>
        </button>
        <input
          {...control}
          className={cx('field__control', 'field__control--numeric')}
          inputMode={props.inputMode ?? 'decimal'}
          onBlur={() => {
            setDraft(null)
          }}
          onChange={(event) => {
            const raw = event.target.value
            setDraft(raw)
            const parsed = parseDecimal(raw)
            if (parsed !== null) {
              onValueChange?.(parsed)
            }
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowUp') {
              event.preventDefault()
              moveBy(step)
            } else if (event.key === 'ArrowDown') {
              event.preventDefault()
              moveBy(-step)
            }
          }}
          type="text"
          value={displayed}
        />
        <button
          aria-controls={ids.control}
          aria-disabled={atMaximum ? true : undefined}
          aria-label={intl.formatMessage({ id: 'forms.stepper.increase' }, { label })}
          className="stepper__button"
          disabled={props.disabled}
          onClick={() => {
            moveBy(step)
          }}
          type="button"
        >
          <span aria-hidden="true">+</span>
        </button>
        <span className="visually-hidden" role="status">
          {announcement}
        </span>
      </span>
    </Field>
  )
}
