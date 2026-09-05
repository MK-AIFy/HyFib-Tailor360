import { FormattedMessage } from 'react-intl'
import { fieldControlAttributes } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import { cx } from '../../foundations/cx'
import { FieldMessages } from './Field'
import './forms.css'

/**
 * An immediate on/off setting: show diagrams beside the fields, larger text, sound on scan.
 *
 * A native checkbox carrying `role="switch"`, not a `<button>` with `aria-pressed` and not a custom
 * widget. The native input brings the space key, the label association and the form value; the role
 * is what makes a screen reader say "on"/"off" rather than "checked"/"not checked", which is the
 * distinction a person changing a setting expects to hear.
 *
 * A switch takes effect immediately by definition. Anything that needs a Save button is a checkbox,
 * because a switch that has not applied yet is a switch that lies about the state of the system.
 */
export type SwitchProps = FieldProps<boolean>

export function Switch(props: SwitchProps) {
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
          checked={value}
          className="choice__input"
          onChange={(event) => {
            if (readOnly !== true) {
              onValueChange?.(event.target.checked)
            }
          }}
          role="switch"
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
