import { useIntl } from 'react-intl'
import { fieldControlAttributes } from '../../foundations/FieldProps'
import type { FieldProps } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import { formattersForLocale } from './formatting'
import { Field } from './Field'
import './forms.css'

/**
 * A date: a due date, a delivery date, a trial date, a date of birth.
 *
 * A native `<input type="date">`. The platform picker it brings is keyboard-operable, announces its
 * own role and segments, and is the only date control that works with VoiceOver's rotor and
 * TalkBack's gestures without a re-implementation. A custom calendar grid would have to earn all of
 * that again, and the shop floor is not where an incomplete one should be found.
 *
 * The value crossing this component's boundary is always an ISO `yyyy-MM-dd` string — the form the
 * platform control uses and the form the API takes. What the person *sees* is whatever their
 * browser renders for their locale, which for `en-IN` is the day-first order this product uses
 * everywhere. The hint states the expected order in words and gives a worked example formatted by
 * the shared `formatters` module, so the field says `04-09-2026` in both catalogues and no component
 * formats a date by hand (docs/nfr/accessibility-localisation.md section 12.2).
 */
export interface DateFieldProps extends FieldProps<string> {
  /** ISO `yyyy-MM-dd`. */
  readonly min?: string
  /** ISO `yyyy-MM-dd`. */
  readonly max?: string
  /** Adds the worked-example hint to the description. On by default. */
  readonly showFormatHint?: boolean
}

/** The example date the hint shows. A fixed instant, so the hint never changes under a reader. */
const EXAMPLE_DATE = Date.UTC(2026, 8, 4)

export function DateField(props: DateFieldProps) {
  const { label, id, size, value, onValueChange, showFormatHint = true } = props
  const intl = useIntl()
  const ids = useFieldIds(id)

  const example = formattersForLocale(intl.locale).formatShortDate(EXAMPLE_DATE)
  const hint = intl.formatMessage({ id: 'forms.date.hint' }, { example })
  const description =
    showFormatHint === false
      ? props.description
      : props.description === undefined
        ? hint
        : `${props.description} ${hint}`

  // Wired from the resolved description: the format hint is produced here, and a hint that is not
  // in `aria-describedby` is a hint only sighted users get.
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
        type="date"
        {...(value === undefined ? {} : { value })}
      />
    </Field>
  )
}
