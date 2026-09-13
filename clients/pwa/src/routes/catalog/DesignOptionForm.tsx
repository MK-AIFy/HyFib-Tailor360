import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import type { DesignOptionDraft } from '../../catalog/designOptionEntry'
import { IllustrationPreview } from './IllustrationPreview'

/**
 * The form for one option within a design option group (#141).
 *
 * `NONE` is the reserved code the picker always offers so a customer can say "not this" — the form
 * does not special-case it, because a reserved code is still a code an administrator types like any
 * other; the server is what refuses one that collides.
 *
 * The illustration key is a plain text field, the same shape the five service-type links already
 * take (`catalog.form.links.hint`): there is no picker for it because there is no live illustration
 * catalogue to pick from yet (#31).
 */
export interface DesignOptionFormProps {
  readonly draft: DesignOptionDraft
  readonly existingCode: string | null
  readonly busy: boolean
  readonly onChange: (draft: DesignOptionDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function DesignOptionForm(props: DesignOptionFormProps) {
  const { draft, existingCode, busy, onChange, onSubmit, onCancel, controlId } = props
  const intl = useIntl()

  const set = <TKey extends keyof DesignOptionDraft>(
    key: TKey,
    value: DesignOptionDraft[TKey],
  ): void => {
    onChange({ ...draft, [key]: value })
  }

  const title =
    existingCode === null
      ? intl.formatMessage({ id: 'catalog.design.option.addTitle' })
      : intl.formatMessage({ id: 'catalog.design.option.editTitle' }, { name: draft.name })

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
        description={intl.formatMessage({
          id:
            existingCode === null
              ? 'catalog.design.option.code.hint'
              : 'catalog.form.code.fixedHint',
        })}
        id={controlId('option-code')}
        label={intl.formatMessage({ id: 'catalog.form.code' })}
        name="option-code"
        {...(existingCode === null
          ? {
              onValueChange: (next: string) => {
                set('code', next.toUpperCase())
              },
              required: true,
            }
          : { readOnly: true })}
        value={draft.code}
      />

      <TextField
        id={controlId('option-name')}
        label={intl.formatMessage({ id: 'catalog.form.name' })}
        name="option-name"
        onValueChange={(next) => {
          set('name', next)
        }}
        required
        value={draft.name}
      />

      <TextField
        id={controlId('option-nameTamil')}
        label={intl.formatMessage({ id: 'catalog.form.nameTamil' })}
        name="option-nameTamil"
        onValueChange={(next) => {
          set('nameTamil', next)
        }}
        value={draft.nameTamil}
      />

      <TextArea
        id={controlId('option-helpText')}
        label={intl.formatMessage({ id: 'catalog.design.option.helpText' })}
        name="option-helpText"
        onValueChange={(next) => {
          set('helpText', next)
        }}
        required
        value={draft.helpText}
      />

      <TextField
        description={intl.formatMessage({ id: 'catalog.design.option.illustrationKey.hint' })}
        id={controlId('option-illustrationKey')}
        label={intl.formatMessage({ id: 'catalog.design.option.illustrationKey' })}
        name="option-illustrationKey"
        onValueChange={(next) => {
          set('illustrationKey', next)
        }}
        value={draft.illustrationKey}
      />

      <TextField
        description={intl.formatMessage({ id: 'catalog.design.option.illustrationAlt.hint' })}
        id={controlId('option-illustrationAlt')}
        label={intl.formatMessage({ id: 'catalog.design.option.illustrationAlt' })}
        name="option-illustrationAlt"
        onValueChange={(next) => {
          set('illustrationAlt', next)
        }}
        required
        value={draft.illustrationAlt}
      />

      <IllustrationPreview
        alt={draft.illustrationAlt}
        illustrationKey={draft.illustrationKey.trim() === '' ? null : draft.illustrationKey}
      />

      <TextField
        id={controlId('option-priceListItemCode')}
        label={intl.formatMessage({ id: 'catalog.link.priceListItemCode' })}
        name="option-priceListItemCode"
        onValueChange={(next) => {
          set('priceListItemCode', next)
        }}
        value={draft.priceListItemCode}
      />

      <NumericStepper
        id={controlId('option-timeImpactDays')}
        label={intl.formatMessage({ id: 'catalog.design.option.timeImpactDays' })}
        min={0}
        name="option-timeImpactDays"
        onValueChange={(next) => {
          set('timeImpactDays', next)
        }}
        value={draft.timeImpactDays}
      />

      <NumericStepper
        id={controlId('option-displayOrder')}
        label={intl.formatMessage({ id: 'catalog.form.displayOrder' })}
        min={0}
        name="option-displayOrder"
        onValueChange={(next) => {
          set('displayOrder', next)
        }}
        value={draft.displayOrder}
      />

      <Checkbox
        description={intl.formatMessage({ id: 'catalog.design.option.active.hint' })}
        id={controlId('option-active')}
        label={intl.formatMessage({ id: 'catalog.design.option.active' })}
        name="option-active"
        onValueChange={(next) => {
          set('active', next)
        }}
        value={draft.active}
      />

      <TextArea
        id={controlId('option-reason')}
        label={intl.formatMessage({ id: 'catalog.form.reason' })}
        name="option-reason"
        onValueChange={(next) => {
          set('reason', next)
        }}
        value={draft.reason}
      />

      <Button busy={busy} type="submit" variant="primary">
        {intl.formatMessage({ id: 'catalog.form.save' })}
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        {intl.formatMessage({ id: 'admin.cancel' })}
      </Button>
    </form>
  )
}
