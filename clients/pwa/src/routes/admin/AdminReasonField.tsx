import { useIntl } from 'react-intl'

/**
 * The written reason every administrative change carries.
 *
 * One component rather than a field per screen, because the two things that make it work are easy to
 * omit one screen at a time: a **persistent visible label** — never a placeholder, which disappears
 * the moment somebody starts typing and takes the question with it — and a hint saying where the
 * words end up. Somebody who knows the sentence is going into a record a colleague may read next year
 * writes a different sentence.
 */
export function AdminReasonField({
  id,
  label,
  value,
  onChange,
}: {
  readonly id: string
  readonly label: string
  readonly value: string
  readonly onChange: (value: string) => void
}) {
  const intl = useIntl()

  return (
    <div className="admin__field">
      <label htmlFor={id}>{label}</label>
      <textarea
        aria-describedby={`${id}-hint`}
        id={id}
        rows={2}
        value={value}
        onChange={(event) => {
          onChange(event.target.value)
        }}
      />
      <p className="admin__hint" id={`${id}-hint`}>
        {intl.formatMessage({ id: 'admin.reason.hint' })}
      </p>
    </div>
  )
}
