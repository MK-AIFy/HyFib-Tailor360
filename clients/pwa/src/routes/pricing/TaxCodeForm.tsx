import { useIntl } from 'react-intl'
import type { TaxCode } from '../../billing/pricingAdminTypes'
import { Button } from '../../components/primitives/Button'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import { RATE_COMPONENTS, RATE_LABEL_KEY } from './taxCodeDraft'
import type { RateComponent, TaxCodeDraft } from './taxCodeDraft'

/** A field error for every component rate a refusal named, keyed by the component it is about. */
export type RateFieldErrors = Readonly<Partial<Record<RateComponent, string>>>

export interface TaxCodeFormProps {
  readonly draft: TaxCodeDraft
  /** Null while adding; the code being edited otherwise. */
  readonly existing: TaxCode | null
  readonly busy: boolean
  readonly codeError?: string
  readonly descriptionError?: string
  readonly classificationError?: string
  readonly kindError?: string
  readonly activeError?: string
  readonly reasonError?: string
  readonly rateErrors: RateFieldErrors
  readonly onChange: (draft: TaxCodeDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

/**
 * Add or edit one tax code of a draft tax configuration version.
 *
 * `kind` and `active` are radio groups with no pre-selected option: a statutory classification and
 * whether a code may be given to a price-list item are the accountant's to decide, not this
 * product's to assume, and the server refuses an omitted `kind` for the same reason (#41). Each
 * component rate is optional and entered as a percentage — never pre-filled and never suggested —
 * through the shared formatters' unit rather than a bare number field.
 */
export function TaxCodeForm({
  draft,
  existing,
  busy,
  codeError,
  descriptionError,
  classificationError,
  kindError,
  activeError,
  reasonError,
  rateErrors,
  onChange,
  onSubmit,
  onCancel,
  controlId,
}: TaxCodeFormProps) {
  const intl = useIntl()

  const set = <TKey extends keyof TaxCodeDraft>(key: TKey, value: TaxCodeDraft[TKey]): void => {
    onChange({ ...draft, [key]: value })
  }

  const setRate = (component: RateComponent, value: string): void => {
    onChange({ ...draft, rates: { ...draft.rates, [component]: value } })
  }

  const title =
    existing === null
      ? intl.formatMessage({ id: 'pricing.taxCode.form.addTitle' })
      : intl.formatMessage({ id: 'pricing.taxCode.form.editTitle' }, { code: existing.code })

  const percentUnit = {
    symbol: intl.formatMessage({ id: 'units.percent.symbol' }),
    label: intl.formatMessage({ id: 'units.percent.label' }),
  }

  return (
    <form
      aria-label={title}
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit()
      }}
    >
      <h3>{title}</h3>

      <TextField
        id={controlId('code')}
        label={intl.formatMessage({ id: 'pricing.taxCode.form.code' })}
        description={intl.formatMessage({ id: 'pricing.taxCode.form.code.hint' })}
        name="code"
        onValueChange={(next) => {
          set('code', next)
        }}
        required
        value={draft.code}
        {...(codeError === undefined ? {} : { error: codeError })}
      />

      <TextField
        id={controlId('description')}
        label={intl.formatMessage({ id: 'pricing.taxCode.form.description' })}
        name="description"
        onValueChange={(next) => {
          set('description', next)
        }}
        required
        value={draft.description}
        {...(descriptionError === undefined ? {} : { error: descriptionError })}
      />

      <TextField
        id={controlId('classification')}
        label={intl.formatMessage({ id: 'pricing.taxCode.form.classification' })}
        description={intl.formatMessage({ id: 'pricing.taxCode.form.classification.hint' })}
        inputMode="numeric"
        name="classification"
        onValueChange={(next) => {
          set('classification', next)
        }}
        required
        value={draft.classification}
        {...(classificationError === undefined ? {} : { error: classificationError })}
      />

      <RadioGroup
        id={controlId('kind')}
        label={intl.formatMessage({ id: 'pricing.taxCode.form.kind' })}
        name="kind"
        onValueChange={(next) => {
          set('kind', next)
        }}
        options={[
          { value: 'Goods', label: intl.formatMessage({ id: 'pricing.taxCode.form.kind.Goods' }) },
          {
            value: 'Services',
            label: intl.formatMessage({ id: 'pricing.taxCode.form.kind.Services' }),
          },
        ]}
        required
        value={draft.kind}
        {...(kindError === undefined ? {} : { error: kindError })}
      />

      <RadioGroup
        id={controlId('active')}
        label={intl.formatMessage({ id: 'pricing.taxCode.form.active' })}
        name="active"
        onValueChange={(next) => {
          set('active', next)
        }}
        options={[
          { value: 'true', label: intl.formatMessage({ id: 'pricing.taxCode.form.active.true' }) },
          {
            value: 'false',
            label: intl.formatMessage({ id: 'pricing.taxCode.form.active.false' }),
          },
        ]}
        required
        value={draft.active}
        {...(activeError === undefined ? {} : { error: activeError })}
      />

      <fieldset>
        <legend>{intl.formatMessage({ id: 'pricing.taxCode.form.rates.title' })}</legend>
        <p>{intl.formatMessage({ id: 'pricing.taxCode.form.rates.hint' })}</p>

        {RATE_COMPONENTS.map((component) => (
          <TextField
            id={controlId(`rate-${component}`)}
            inputMode="decimal"
            key={component}
            label={intl.formatMessage({ id: RATE_LABEL_KEY[component] })}
            name={`rate-${component}`}
            onValueChange={(next) => {
              setRate(component, next)
            }}
            unit={percentUnit}
            value={draft.rates[component]}
            {...(rateErrors[component] === undefined ? {} : { error: rateErrors[component] })}
          />
        ))}
      </fieldset>

      <TextArea
        id={controlId('reason')}
        label={intl.formatMessage({ id: 'pricing.taxCode.form.reason' })}
        name="reason"
        onValueChange={(next) => {
          set('reason', next)
        }}
        value={draft.reason}
        {...(reasonError === undefined ? {} : { error: reasonError })}
      />

      <Button busy={busy} type="submit" variant="primary">
        {intl.formatMessage({ id: 'pricing.taxCode.form.save' })}
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        {intl.formatMessage({ id: 'admin.cancel' })}
      </Button>
    </form>
  )
}
