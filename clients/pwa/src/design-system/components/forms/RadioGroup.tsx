import { fieldDescribedBy, isFieldInvalid } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import type { ChoiceOption } from './controlTypes'
import { FieldGroup } from './Field'
import './forms.css'

/**
 * One choice from a short list where seeing all the options at once matters: a waist finish, a
 * display unit, a reason category.
 *
 * A `<fieldset>` with a `<legend>`, because checklist item A11Y-59 asks for the group name to be
 * announced before the first option and a set of radios with no group is a set of unlabelled
 * choices. Native radios inside it bring arrow-key navigation, roving focus and the "3 of 8"
 * position announcement for nothing.
 *
 * `aria-required` sits on each radio rather than on the fieldset. Putting it on the fieldset would
 * mean giving the fieldset `role="radiogroup"`, and that trade — a non-native role, for one
 * announcement instead of one per option — is not worth making on a control a Tailor uses with a
 * screen reader on a phone.
 *
 * More than about six options is a `Select`: a radio list a person has to scroll is a list they
 * cannot compare, which is the only reason to prefer radios in the first place.
 *
 * `readOnly` becomes `aria-readonly` on each option plus a change handler that does nothing: the
 * native attribute has no effect on a radio, and a control the caller declared read-only must not
 * quietly accept a change. It stays focusable, because a value under review has to be readable.
 */
export interface RadioGroupProps extends FieldProps<string> {
  readonly options: readonly ChoiceOption[]
}

export function RadioGroup(props: RadioGroupProps) {
  const { label, id, size = 'standard', value, onValueChange, options, name } = props
  const ids = useFieldIds(id)
  const describedBy = fieldDescribedBy(props, ids)
  const invalid = isFieldInvalid(props)

  return (
    <FieldGroup
      describedBy={describedBy}
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
      <div className="choice-list">
        {options.map((option, index) => {
          /*
           * The first radio carries the field's own id, because that is what the error summary
           * moves focus to and focusing the first option of a group puts a keyboard user exactly
           * where they need to be.
           */
          const optionId = index === 0 ? ids.control : `${ids.control}-${String(index)}`
          return (
            <label className="choice" key={option.value}>
              <input
                aria-invalid={invalid ? true : undefined}
                aria-readonly={props.readOnly === true ? true : undefined}
                aria-required={props.required === true ? true : undefined}
                checked={value === undefined ? undefined : value === option.value}
                className="choice__input"
                disabled={option.disabled}
                id={optionId}
                name={name}
                onChange={() => {
                  if (props.readOnly !== true) {
                    onValueChange?.(option.value)
                  }
                }}
                type="radio"
                value={option.value}
              />
              <span className="choice__label">
                {option.label}
                {option.description === undefined ? null : (
                  <span className="choice__description">{option.description}</span>
                )}
              </span>
            </label>
          )
        })}
      </div>
    </FieldGroup>
  )
}
