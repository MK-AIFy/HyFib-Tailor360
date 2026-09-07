import { fieldControlAttributes } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import { cx } from '../../foundations/cx'
import { Field } from './Field'
import './forms.css'

/**
 * A multi-line text control: a reason, a note, an alteration description, a defect note.
 *
 * Almost everything typed into one of these is user-generated content, which
 * docs/nfr/accessibility-localisation.md section 11.4 says is stored and shown exactly as entered —
 * never machine-translated, never transliterated, never auto-corrected. That rule is why
 * `spellCheck` defaults to false and why nothing in this component transforms the value on the way
 * out: a reason string is audit evidence, and evidence is quoted rather than tidied.
 *
 * `rows` sets a starting height only. The box grows with its content and the user may drag it
 * taller, because a fixed height would clip at the 150% text-size preference (checklist A11Y-72).
 */
export interface TextAreaProps extends FieldProps<string> {
  readonly rows?: number
  readonly maxLength?: number
  readonly spellCheck?: boolean
}

export function TextArea(props: TextAreaProps) {
  const { label, id, size, value, onValueChange, rows = 3, spellCheck = false } = props
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
      <textarea
        {...control}
        className={cx('field__control', 'field__control--multiline')}
        maxLength={props.maxLength}
        onChange={(event) => {
          onValueChange?.(event.target.value)
        }}
        rows={rows}
        spellCheck={spellCheck}
        {...(value === undefined ? {} : { value })}
      />
    </Field>
  )
}
