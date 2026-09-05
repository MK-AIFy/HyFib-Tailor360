import { fieldControlAttributes } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import type { EnterKeyHint, TextFieldType } from './controlTypes'
import { Field } from './Field'
import './forms.css'

/**
 * A single-line text control.
 *
 * A customer field passes `autoComplete` and, for a phone number, `inputMode: 'tel'` — WCAG 1.3.5
 * and the #50 blueprint both name them, and the tokens live in `autocomplete.ts` so they are not
 * retyped and mistyped at each call site.
 *
 * The control is uncontrolled when `value` is omitted and controlled when it is given, rather than
 * being permanently controlled with a `?? ''` fallback. A permanently controlled input whose caller
 * forgets `onValueChange` is a field a person cannot type in, which on a counter tablet looks
 * exactly like a broken application.
 */
export interface TextFieldProps extends FieldProps<string> {
  readonly type?: TextFieldType
  readonly maxLength?: number
  readonly enterKeyHint?: EnterKeyHint
  /** Spellcheck is off by default: a customer's name is not a spelling mistake. */
  readonly spellCheck?: boolean
}

export function TextField(props: TextFieldProps) {
  const { label, id, size, value, onValueChange, type = 'text', spellCheck = false } = props
  const ids = useFieldIds(id)
  const control = fieldControlAttributes(props, ids)

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
      <input
        {...control}
        className="field__control"
        enterKeyHint={props.enterKeyHint}
        maxLength={props.maxLength}
        onChange={(event) => {
          onValueChange?.(event.target.value)
        }}
        spellCheck={spellCheck}
        type={type}
        {...(value === undefined ? {} : { value })}
      />
    </Field>
  )
}
