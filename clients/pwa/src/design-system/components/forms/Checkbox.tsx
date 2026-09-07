import { FormattedMessage } from 'react-intl'
import { fieldControlAttributes } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import { cx } from '../../foundations/cx'
import { FieldMessages } from './Field'
import './forms.css'

/**
 * A single yes/no choice: an acknowledgement, an opt-in, a "reuse the previous measurements".
 *
 * The label wraps the box rather than sitting above it, which is what makes the whole row a target
 * — a 24 px box inside a 44 px row is compliant, because the target is the hit area and not the ink
 * (docs/nfr/accessibility-localisation.md section 5, rule 1).
 *
 * The required marker sits inside the label but carries `aria-hidden`, so it is visible without
 * joining the accessible name; the name stays exactly the visible words, which is what voice
 * control matches on (2.5.3).
 *
 * `readOnly` becomes `aria-readonly` and a change handler that does nothing: the attribute exists on
 * a checkbox but the browser ignores it, and silently accepting a change on a field the caller
 * declared read-only is worse than not offering one.
 */
export interface CheckboxProps extends FieldProps<boolean> {
  /** Marks the group this checkbox belongs to as partly chosen. Visual and `aria-checked` only. */
  readonly indeterminate?: boolean
}

export function Checkbox(props: CheckboxProps) {
  const { label, id, size = 'standard', value, onValueChange } = props
  const ids = useFieldIds(id)
  const { readOnly, ...control } = fieldControlAttributes(props, ids)

  return (
    <div
      className={cx('field', 'field--choice')}
      data-disabled={props.disabled === true ? 'true' : undefined}
      data-invalid={props.error === undefined ? undefined : 'true'}
      data-size={size}
    >
      <label className="choice">
        <input
          {...control}
          aria-readonly={readOnly}
          checked={props.indeterminate === true ? false : value}
          className="choice__input"
          onChange={(event) => {
            if (readOnly !== true) {
              onValueChange?.(event.target.checked)
            }
          }}
          ref={(element) => {
            if (element !== null) {
              element.indeterminate = props.indeterminate === true
            }
          }}
          type="checkbox"
        />
        <span className="choice__label">
          {label}
          {props.required === true ? (
            <span aria-hidden="true" className="field__required">
              {' '}
              <FormattedMessage id="forms.required" />
            </span>
          ) : null}
        </span>
      </label>
      <FieldMessages
        description={props.description}
        error={props.error}
        ids={ids}
        unit={props.unit}
        warning={props.warning}
      />
    </div>
  )
}
