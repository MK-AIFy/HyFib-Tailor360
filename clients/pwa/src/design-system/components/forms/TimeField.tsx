import { useIntl } from 'react-intl'
import { fieldControlAttributes } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import { formattersForLocale } from './formatting'
import { Field } from './Field'
import './forms.css'

/**
 * A wall-clock time: quiet hours, an appointment, a collection slot.
 *
 * `DateField`'s sibling and built the same way, for the same reason: a native `<input type="time">`
 * brings a platform picker that is keyboard-operable, announces its own segments, and works with
 * VoiceOver's rotor and TalkBack's gestures without a re-implementation. A hand-built hour-and-minute
 * pair would have to earn all of that again.
 *
 * The value crossing this component's boundary is always 24-hour `HH:mm` — the form the platform
 * control uses. What the person *sees* is whatever their browser renders for their locale, which for
 * `en-IN` is the 12-hour clock this product uses everywhere; the hint says so in words with a worked
 * example formatted by the shared `formatters` module, so no component formats a time by hand
 * (docs/nfr/accessibility-localisation.md section 12.2).
 *
 * ## What this is not
 *
 * It is not an instant. A time with no date is a wall-clock reading at a branch, and the two are not
 * interchangeable: quiet hours run 21:00 to 08:00 across a midnight that belongs to no day, and a
 * component that resolved either end to an instant would have to invent one.
 */
export interface TimeFieldProps extends FieldProps<string> {
  /** 24-hour `HH:mm`. */
  readonly min?: string
  /** 24-hour `HH:mm`. */
  readonly max?: string
  /** Adds the worked-example hint to the description. On by default. */
  readonly showFormatHint?: boolean
}

/** The example the hint shows. A fixed instant, so the hint never changes under a reader. */
const EXAMPLE_TIME = Date.UTC(2026, 8, 4, 11, 0)

export function TimeField(props: TimeFieldProps) {
  const { label, id, size, value, onValueChange, showFormatHint = true } = props
  const intl = useIntl()
  const ids = useFieldIds(id)

  const example = formattersForLocale(intl.locale).formatTime(EXAMPLE_TIME)
  const hint = intl.formatMessage({ id: 'forms.time.hint' }, { example })
  const description =
    showFormatHint === false
      ? props.description
      : props.description === undefined
        ? hint
        : `${props.description} ${hint}`

  // Wired from the resolved description, exactly as `DateField` does: a format hint that is not in
  // `aria-describedby` is a hint only sighted users get.
  const control = fieldControlAttributes(
    { ...props, ...(description === undefined ? {} : { description }) },
    ids,
  )

  return (
    <Field
      description={description}
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
        max={props.max}
        min={props.min}
        onChange={(event) => {
          onValueChange?.(event.target.value)
        }}
        type="time"
        {...(value === undefined ? {} : { value })}
      />
    </Field>
  )
}
