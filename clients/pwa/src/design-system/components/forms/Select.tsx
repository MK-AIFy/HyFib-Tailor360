import { useIntl } from 'react-intl'
import { fieldControlAttributes } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import { cx } from '../../foundations/cx'
import type { ChoiceOption } from './controlTypes'
import { Field } from './Field'
import './forms.css'

/**
 * A choice from a short, fixed list: a waist finish, an age band, a display unit, a payment mode.
 *
 * A native `<select>`, not a custom listbox. The native control brings the platform picker — a
 * wheel on iOS, a full-screen list on Android — which is operable one-handed with a garment in the
 * other hand, announces its own role and state, and works with a hardware keyboard, voice control
 * and a screen reader without a line of JavaScript. A custom listbox would have to re-earn all of
 * that, and the shop floor is not where a re-implementation should be discovered to be incomplete.
 *
 * The empty option exists so that a required field does not start with a value nobody chose. It is
 * not a label: the visible label is above the control, always, by the contract.
 *
 * `readOnly` has no native equivalent on a `<select>` — the attribute exists on an `<input>` only —
 * so it is honoured as `aria-readonly` plus a change handler that does nothing. The control stays
 * focusable and readable, which is what the contract means by read-only: a value under review is
 * not a disabled value, and a disabled control is one a screen-reader user cannot reach at all.
 */
export interface SelectProps extends FieldProps<string> {
  readonly options: readonly ChoiceOption[]
  /**
   * The empty option's text. Defaults to the catalogue's "Choose an option". Pass `null` to omit
   * the empty option entirely, for a field that genuinely always has a value.
   */
  readonly emptyLabel?: string | null
}

export function Select(props: SelectProps) {
  const { label, id, size, value, onValueChange, options, emptyLabel } = props
  const intl = useIntl()
  const ids = useFieldIds(id)
  const { readOnly, ...control } = fieldControlAttributes(props, ids)
  const empty =
    emptyLabel === null ? null : (emptyLabel ?? intl.formatMessage({ id: 'forms.select.choose' }))

  return (
    <Field
      description={props.description}
      disabled={props.disabled}
      error={props.error}
      ids={ids}
      label={label}
      required={props.required}
      size={size}
      unit={props.unit}
      warning={props.warning}
    >
      <select
        {...control}
        className={cx('field__control', 'field__control--select')}
        aria-readonly={readOnly}
        onChange={(event) => {
          if (readOnly !== true) {
            onValueChange?.(event.target.value)
          }
        }}
        {...(value === undefined ? {} : { value })}
      >
        {empty === null ? null : <option value="">{empty}</option>}
        {options.map((option) => (
          <option disabled={option.disabled} key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </Field>
  )
}
