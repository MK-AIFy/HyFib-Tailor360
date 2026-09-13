import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import { IllustrationPreview } from './IllustrationPreview'

/** The five fields a published design option admits a change to. */
export interface DesignOptionPresentationDraft {
  readonly name: string
  readonly nameTamil: string
  readonly helpText: string
  readonly illustrationAlt: string
  readonly displayOrder: number
  readonly reason: string
}

/**
 * The one edit a published design option admits.
 *
 * The illustration itself is never offered here, on purpose: the drawing a customer was shown when
 * they chose it is part of what they agreed to, so only its words — the label, help text and
 * alternative text — can be corrected (#141).
 */
export interface DesignOptionPresentationFormProps {
  readonly draft: DesignOptionPresentationDraft
  readonly subject: string
  readonly illustrationKey: string | null
  readonly busy: boolean
  readonly onChange: (draft: DesignOptionPresentationDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function DesignOptionPresentationForm(props: DesignOptionPresentationFormProps) {
  const { draft, subject, illustrationKey, busy, onChange, onSubmit, onCancel, controlId } = props
  const intl = useIntl()

  const set = <TKey extends keyof DesignOptionPresentationDraft>(
    key: TKey,
    value: DesignOptionPresentationDraft[TKey],
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
        id={controlId('correct-option-name')}
        label={intl.formatMessage({ id: 'catalog.form.name' })}
        name="correct-option-name"
        onValueChange={(next) => {
          set('name', next)
        }}
        required
        value={draft.name}
      />

      <TextField
        id={controlId('correct-option-nameTamil')}
        label={intl.formatMessage({ id: 'catalog.form.nameTamil' })}
        name="correct-option-nameTamil"
        onValueChange={(next) => {
          set('nameTamil', next)
        }}
        value={draft.nameTamil}
      />

      <TextArea
        id={controlId('correct-option-helpText')}
        label={intl.formatMessage({ id: 'catalog.design.option.helpText' })}
        name="correct-option-helpText"
        onValueChange={(next) => {
          set('helpText', next)
        }}
        value={draft.helpText}
      />

      <TextField
        id={controlId('correct-option-illustrationAlt')}
        label={intl.formatMessage({ id: 'catalog.design.option.illustrationAlt' })}
        name="correct-option-illustrationAlt"
        onValueChange={(next) => {
          set('illustrationAlt', next)
        }}
        required
        value={draft.illustrationAlt}
      />

      <IllustrationPreview alt={draft.illustrationAlt} illustrationKey={illustrationKey} />

      <NumericStepper
        id={controlId('correct-option-displayOrder')}
        label={intl.formatMessage({ id: 'catalog.form.displayOrder' })}
        min={0}
        name="correct-option-displayOrder"
        onValueChange={(next) => {
          set('displayOrder', next)
        }}
        value={draft.displayOrder}
      />

      <TextArea
        description={intl.formatMessage({ id: 'catalog.correct.reason.hint' })}
        id={controlId('correct-option-reason')}
        label={intl.formatMessage({ id: 'catalog.form.reason' })}
        name="correct-option-reason"
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
