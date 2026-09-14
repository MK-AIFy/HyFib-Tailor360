import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import type { PriceListDraft } from './priceListDraft'

/**
 * The form that creates a price list, and the form that renames one.
 *
 * ## Why the rename form offers the name only
 *
 * A code is what every seed and export refers to once a list exists (`docs/security/permission-matrix.md`
 * line 416), so the server never accepts a change to it — the same reasoning
 * `CatalogEntryForm.tsx`'s "Why the code is read-only once it exists" comment records for the
 * catalogue. There, the control stays and turns read-only; here there would be nothing for a
 * read-only code box to do beside a heading that already names it, so the control is dropped rather
 * than kept and disabled, and the heading and a sentence say why instead.
 */
export interface PriceListFormProps {
  readonly draft: PriceListDraft
  readonly mode: 'create' | 'rename'
  /** The code of the list being renamed. Ignored in `create` mode. */
  readonly existingCode: string | null
  readonly busy: boolean
  readonly codeError?: string
  readonly onChange: (draft: PriceListDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function PriceListForm({
  draft,
  mode,
  existingCode,
  busy,
  codeError,
  onChange,
  onSubmit,
  onCancel,
  controlId,
}: PriceListFormProps) {
  const intl = useIntl()

  const set = <TKey extends keyof PriceListDraft>(key: TKey, value: PriceListDraft[TKey]): void => {
    onChange({ ...draft, [key]: value })
  }

  const title =
    mode === 'create'
      ? intl.formatMessage({ id: 'pricing.priceList.form.addTitle' })
      : intl.formatMessage({ id: 'pricing.priceList.form.editTitle' }, { code: existingCode ?? '' })

  return (
    <form
      aria-label={title}
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit()
      }}
    >
      <h3>{title}</h3>

      {mode === 'create' ? (
        <TextField
          description={intl.formatMessage({ id: 'pricing.priceList.form.code.hint' })}
          id={controlId('code')}
          label={intl.formatMessage({ id: 'pricing.priceList.form.code' })}
          name="code"
          onValueChange={(next) => {
            set('code', next.toUpperCase())
          }}
          required
          value={draft.code}
          {...(codeError === undefined ? {} : { error: codeError })}
        />
      ) : (
        <p>{intl.formatMessage({ id: 'pricing.priceList.form.code.fixedHint' })}</p>
      )}

      <TextField
        id={controlId('name')}
        label={intl.formatMessage({ id: 'pricing.priceList.form.name' })}
        name="name"
        onValueChange={(next) => {
          set('name', next)
        }}
        required
        value={draft.name}
      />

      <TextArea
        id={controlId('reason')}
        label={intl.formatMessage({ id: 'pricing.priceList.form.reason' })}
        name="reason"
        onValueChange={(next) => {
          set('reason', next)
        }}
        value={draft.reason}
      />

      <Button busy={busy} type="submit" variant="primary">
        {intl.formatMessage({ id: 'pricing.priceList.form.save' })}
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        {intl.formatMessage({ id: 'admin.cancel' })}
      </Button>
    </form>
  )
}
