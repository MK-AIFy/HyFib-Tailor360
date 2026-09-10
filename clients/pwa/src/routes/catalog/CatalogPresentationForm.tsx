import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'

/** The four fields a published version admits a change to, and the reason that must come with them. */
export interface PresentationDraft {
  readonly name: string
  readonly nameTamil: string
  readonly description: string
  readonly displayOrder: number
  readonly reason: string
}

/**
 * The one edit a published version admits.
 *
 * ## Why this is a different form from the entry form
 *
 * Not a narrowed version of it — a different one. The entry form's other controls are not disabled
 * here, they are absent, because the boundary between a label and a behaviour is the whole safety
 * argument: a correction changes what is *shown* and nothing that was priced, worked to or reported.
 * A form that greyed out the code and the five links would invite somebody to ask why, and a form
 * that merely hid them would still be one state flag away from sending one.
 *
 * ## Why the reason is required here and optional in a draft
 *
 * The server demands one (`CatalogVersion.CheckReason`), and it demands one because this is a change
 * to something confirmed orders are pinned to. The reason is the audit entry: a month later, "who
 * renamed this and why" is answerable from it or from nothing.
 */
export interface CatalogPresentationFormProps {
  readonly draft: PresentationDraft
  /** What is being corrected, for the form's own accessible name. */
  readonly subject: string
  readonly busy: boolean
  readonly onChange: (draft: PresentationDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function CatalogPresentationForm(props: CatalogPresentationFormProps) {
  const { draft, subject, busy, onChange, onSubmit, onCancel, controlId } = props
  const intl = useIntl()

  const set = <TKey extends keyof PresentationDraft>(
    key: TKey,
    value: PresentationDraft[TKey],
  ): void => {
    onChange({ ...draft, [key]: value })
  }

  const title = intl.formatMessage({ id: 'catalog.correct.title' }, { name: subject })

  return (
    <form
      aria-label={title}
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit()
      }}
    >
      <h3>{title}</h3>
      <p>{intl.formatMessage({ id: 'catalog.correct.body' })}</p>

      <TextField
        id={controlId('correct-name')}
        label={intl.formatMessage({ id: 'catalog.form.name' })}
        name="correct-name"
        onValueChange={(next) => {
          set('name', next)
        }}
        required
        value={draft.name}
      />

      <TextField
        id={controlId('correct-nameTamil')}
        label={intl.formatMessage({ id: 'catalog.form.nameTamil' })}
        name="correct-nameTamil"
        onValueChange={(next) => {
          set('nameTamil', next)
        }}
        value={draft.nameTamil}
      />

      <TextArea
        id={controlId('correct-description')}
        label={intl.formatMessage({ id: 'catalog.form.description' })}
        name="correct-description"
        onValueChange={(next) => {
          set('description', next)
        }}
        value={draft.description}
      />

      <NumericStepper
        id={controlId('correct-displayOrder')}
        label={intl.formatMessage({ id: 'catalog.form.displayOrder' })}
        min={0}
        name="correct-displayOrder"
        onValueChange={(next) => {
          set('displayOrder', next)
        }}
        value={draft.displayOrder}
      />

      <TextArea
        description={intl.formatMessage({ id: 'catalog.correct.reason.hint' })}
        id={controlId('correct-reason')}
        label={intl.formatMessage({ id: 'catalog.form.reason' })}
        name="correct-reason"
        onValueChange={(next) => {
          set('reason', next)
        }}
        required
        value={draft.reason}
      />

      <Button busy={busy} type="submit" variant="primary">
        {intl.formatMessage({ id: 'catalog.correct.save' })}
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        {intl.formatMessage({ id: 'admin.cancel' })}
      </Button>
    </form>
  )
}
