import { FormattedMessage, useIntl } from 'react-intl'

/**
 * The written reason every administrative change carries.
 *
 * One component rather than a field per screen, because the three things that make it work are easy
 * to omit one screen at a time: a **persistent visible label** — never a placeholder, which
 * disappears the moment somebody starts typing and takes the question with it — a hint saying where
 * the words end up, and an **error that is attached to this field** rather than announced somewhere
 * above the form. Somebody who knows the sentence is going into a record a colleague may read next
 * year writes a different sentence.
 *
 * The error wiring mirrors `Field`'s rather than inventing a second spelling of it: `aria-invalid`
 * when there is one, `aria-describedby` naming the error *before* the hint because a failed submit
 * is what brought the reader here, and a visually-hidden "Error:" prefix so the message is not
 * carried by colour and position alone.
 */
export function AdminReasonField({
  id,
  label,
  value,
  error,
  onChange,
}: {
  readonly id: string
  readonly label: string
  readonly value: string
  /** The message, in words, or undefined when the field is fine. Present means invalid. */
  readonly error?: string | undefined
  readonly onChange: (value: string) => void
}) {
  const intl = useIntl()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`

  return (
    <div className="admin__field">
      <label htmlFor={id}>{label}</label>
      <textarea
        aria-describedby={error === undefined ? hintId : `${errorId} ${hintId}`}
        id={id}
        rows={2}
        value={value}
        onChange={(event) => {
          onChange(event.target.value)
        }}
        {...(error === undefined ? {} : { 'aria-invalid': true as const })}
      />
      {error === undefined ? null : (
        <p className="field__message field__message--error" id={errorId}>
          <span className="visually-hidden">
            <FormattedMessage id="forms.error.prefix" />{' '}
          </span>
          {error}
        </p>
      )}
      <p className="admin__hint" id={hintId}>
        {intl.formatMessage({ id: 'admin.reason.hint' })}
      </p>
    </div>
  )
}
