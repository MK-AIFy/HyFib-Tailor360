import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'

/** The three fields a published design group admits a change to, and the reason that must come with them. */
export interface DesignGroupPresentationDraft {
  readonly name: string
  readonly nameTamil: string
  readonly displayOrder: number
  readonly reason: string
}

/**
 * The one edit a published design group admits — the same narrow shape `CatalogPresentationForm`
 * gives a category or a service type, minus the description a design group never had (#141).
 */
export interface DesignGroupPresentationFormProps {
  readonly draft: DesignGroupPresentationDraft
  readonly subject: string
  readonly busy: boolean
  readonly onChange: (draft: DesignGroupPresentationDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function DesignGroupPresentationForm(props: DesignGroupPresentationFormProps) {
  const { draft, subject, busy, onChange, onSubmit, onCancel, controlId } = props
  const intl = useIntl()

  const set = <TKey extends keyof DesignGroupPresentationDraft>(
    key: TKey,
    value: DesignGroupPresentationDraft[TKey],
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
      <h4>{title}</h4>

      <TextField
        id={controlId('correct-group-name')}
        label={intl.formatMessage({ id: 'catalog.form.name' })}
        name="correct-group-name"
        onValueChange={(next) => {
          set('name', next)
        }}
        required
        value={draft.name}
      />

      <TextField
        id={controlId('correct-group-nameTamil')}
        label={intl.formatMessage({ id: 'catalog.form.nameTamil' })}
        name="correct-group-nameTamil"
        onValueChange={(next) => {
          set('nameTamil', next)
        }}
        value={draft.nameTamil}
      />

      <NumericStepper
        id={controlId('correct-group-displayOrder')}
        label={intl.formatMessage({ id: 'catalog.form.displayOrder' })}
        min={0}
        name="correct-group-displayOrder"
        onValueChange={(next) => {
          set('displayOrder', next)
        }}
        value={draft.displayOrder}
      />

      <TextArea
        description={intl.formatMessage({ id: 'catalog.correct.reason.hint' })}
        id={controlId('correct-group-reason')}
        label={intl.formatMessage({ id: 'catalog.form.reason' })}
        name="correct-group-reason"
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
