import { fieldDescribedBy, isFieldInvalid } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import type { ChoiceOption } from './controlTypes'
import { FieldGroup } from './Field'
import './forms.css'

/**
 * One choice from a short list, chosen by a large, always-visible row of buttons: a payment mode,
 * a dispatch outcome, anything a person on a shop floor picks without reading past the first word.
 *
 * ## The same group as `RadioGroup`, styled as buttons
 *
 * The accessibility contract is `RadioGroup`'s, unchanged: a `<fieldset>` with a `<legend>`
 * (checklist item A11Y-59, the group name announced before the first option), native radios for
 * arrow-key navigation and roving focus, and `aria-required` on each option rather than a
 * non-native `role="radiogroup"` on the fieldset. What differs is only the segment itself, drawn
 * the way `FractionInput`'s fraction strip already draws its segments: the native radio sits over
 * the whole segment, made transparent rather than removed, so the focus ring lands on the button a
 * person actually sees and the platform still owns the keyboard behaviour.
 *
 * Reach for this over `RadioGroup` when the options are few, short, and meant to be tapped rather
 * than read — a payment mode chosen with a garment in the other hand. Reach for `RadioGroup` when
 * an option carries a description, because a segment has no room for one.
 *
 * Defaults to the `primary` target size (56 px): this is the shop-floor-primary-action control
 * (docs/nfr/accessibility-localisation.md section 5), not a settings toggle.
 */
export interface SegmentedControlProps extends FieldProps<string> {
  readonly options: readonly ChoiceOption[]
}

export function SegmentedControl(props: SegmentedControlProps) {
  const { label, id, size = 'primary', value, onValueChange, options, name } = props
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
      <div className="segmented">
        {options.map((option, index) => {
          // The first radio carries the field's own id, for the same reason RadioGroup's does: it
          // is what the error summary moves focus to.
          const optionId = index === 0 ? ids.control : `${ids.control}-${String(index)}`
          return (
            <label className="segmented__option" key={option.value}>
              <input
                aria-invalid={invalid ? true : undefined}
                aria-readonly={props.readOnly === true ? true : undefined}
                aria-required={props.required === true ? true : undefined}
                checked={value === undefined ? undefined : value === option.value}
                className="segmented__input"
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
              <span className="segmented__label">{option.label}</span>
            </label>
          )
        })}
      </div>
    </FieldGroup>
  )
}
