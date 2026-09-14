import { useIntl } from 'react-intl'
import { Link } from 'react-router'
import { Field } from '../../design-system/components/forms/Field'
import { fieldControlAttributes } from '../../design-system/foundations/FieldProps'
import { useFieldIds } from '../../design-system/foundations/ids'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import { Button } from '../../components/primitives/Button'
import type { PriceListItem } from '../../billing/priceListTypes'
import { PRICE_ITEM_KINDS } from './priceListItemDraft'
import type { PriceListItemDraft } from './priceListItemDraft'

export interface PriceListItemFormProps {
  readonly draft: PriceListItemDraft
  /** Null while adding; the item being edited otherwise. */
  readonly existing: PriceListItem | null
  readonly busy: boolean
  /** The active codes of the published tax configuration, for the picker. Empty when none is available. */
  readonly taxCodeOptions: readonly string[]
  /** Whether a published tax configuration was found at all — distinct from it having no active codes. */
  readonly taxConfigurationAvailable: boolean
  readonly codeError?: string
  readonly descriptionError?: string
  readonly kindError?: string
  readonly baseRateError?: string
  readonly unitError?: string
  readonly taxCodeError?: string
  readonly activeError?: string
  readonly reasonError?: string
  readonly onChange: (draft: PriceListItemDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

/**
 * Add or edit one item of a draft price-list version — a service's base charge, a surcharge or a
 * material.
 *
 * `kind` and `active` are radio groups with no pre-selected option: what a line prices and whether it
 * may be charged are the administrator's to decide, and the server refuses either omitted for the
 * same reason (#41, #268). The base rate and the unit are never pre-filled, for the same reason the
 * tax configuration's own rates never are — nothing here suggests what a customer is charged.
 *
 * ## Why the tax code stays a typed field even when a picker is offered
 *
 * The item's tax code is checked when the version is *published*, not when the item is saved
 * (`PriceListPublicationCheck`), so a shop that writes its price list before it publishes a tax
 * configuration must not be blocked here. The active codes of the published tax configuration are
 * offered as suggestions through the field's own `list`, which keeps the single control both a picker
 * and a typed field rather than two controls fighting over one value.
 */
export function PriceListItemForm({
  draft,
  existing,
  busy,
  taxCodeOptions,
  taxConfigurationAvailable,
  codeError,
  descriptionError,
  kindError,
  baseRateError,
  unitError,
  taxCodeError,
  activeError,
  reasonError,
  onChange,
  onSubmit,
  onCancel,
  controlId,
}: PriceListItemFormProps) {
  const intl = useIntl()

  const set = <TKey extends keyof PriceListItemDraft>(
    key: TKey,
    value: PriceListItemDraft[TKey],
  ): void => {
    onChange({ ...draft, [key]: value })
  }

  const title =
    existing === null
      ? intl.formatMessage({ id: 'pricing.priceListItem.form.addTitle' })
      : intl.formatMessage({ id: 'pricing.priceListItem.form.editTitle' }, { code: existing.code })

  const taxCodeFieldId = controlId('taxCode')
  const taxCodeIds = useFieldIds(taxCodeFieldId)
  const taxCodeDatalistId = `${taxCodeIds.control}-options`
  const taxCodeControl = fieldControlAttributes(
    {
      name: 'taxCode',
      required: true,
      ...(taxCodeError === undefined ? {} : { error: taxCodeError }),
    },
    taxCodeIds,
  )

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
        label={intl.formatMessage({ id: 'pricing.priceListItem.form.code' })}
        description={intl.formatMessage({ id: 'pricing.priceListItem.form.code.hint' })}
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
        label={intl.formatMessage({ id: 'pricing.priceListItem.form.description' })}
        name="description"
        onValueChange={(next) => {
          set('description', next)
        }}
        required
        value={draft.description}
        {...(descriptionError === undefined ? {} : { error: descriptionError })}
      />

      <RadioGroup
        id={controlId('kind')}
        label={intl.formatMessage({ id: 'pricing.priceListItem.form.kind' })}
        name="kind"
        onValueChange={(next) => {
          set('kind', next as PriceListItemDraft['kind'])
        }}
        options={PRICE_ITEM_KINDS.map((kind) => ({
          value: kind,
          label: intl.formatMessage({ id: `pricing.priceListItem.form.kind.${kind}` }),
        }))}
        required
        value={draft.kind}
        {...(kindError === undefined ? {} : { error: kindError })}
      />

      <TextField
        id={controlId('baseRate')}
        inputMode="decimal"
        label={intl.formatMessage({ id: 'pricing.priceListItem.form.baseRate' })}
        name="baseRate"
        onValueChange={(next) => {
          set('baseRate', next)
        }}
        required
        unit={{
          symbol: intl.formatMessage({ id: 'units.rupee.symbol' }),
          label: intl.formatMessage({ id: 'units.rupee.label' }),
          position: 'leading',
        }}
        value={draft.baseRate}
        {...(baseRateError === undefined ? {} : { error: baseRateError })}
      />

      <TextField
        id={controlId('unit')}
        description={intl.formatMessage({ id: 'pricing.priceListItem.form.unit.hint' })}
        label={intl.formatMessage({ id: 'pricing.priceListItem.form.unit' })}
        name="unit"
        onValueChange={(next) => {
          set('unit', next)
        }}
        required
        value={draft.unit}
        {...(unitError === undefined ? {} : { error: unitError })}
      />

      <Field
        description={
          taxConfigurationAvailable
            ? intl.formatMessage({ id: 'pricing.priceListItem.form.taxCode.hint' })
            : intl.formatMessage({ id: 'pricing.priceListItem.form.taxCode.noneHint' })
        }
        error={taxCodeError}
        ids={taxCodeIds}
        label={intl.formatMessage({ id: 'pricing.priceListItem.form.taxCode' })}
        required
      >
        <input
          {...taxCodeControl}
          className="field__control"
          list={taxConfigurationAvailable ? taxCodeDatalistId : undefined}
          onChange={(event) => {
            set('taxCode', event.target.value)
          }}
          type="text"
          value={draft.taxCode}
        />
      </Field>
      {taxConfigurationAvailable ? (
        <datalist id={taxCodeDatalistId}>
          {taxCodeOptions.map((code) => (
            <option key={code} value={code} />
          ))}
        </datalist>
      ) : (
        <p className="field__description">
          <Link to="/admin/tax-configuration">
            {intl.formatMessage({ id: 'pricing.priceListItem.form.taxCode.noneHint.link' })}
          </Link>
        </p>
      )}

      <RadioGroup
        id={controlId('active')}
        label={intl.formatMessage({ id: 'pricing.priceListItem.form.active' })}
        name="active"
        onValueChange={(next) => {
          set('active', next)
        }}
        options={[
          {
            value: 'true',
            label: intl.formatMessage({ id: 'pricing.priceListItem.form.active.true' }),
          },
          {
            value: 'false',
            label: intl.formatMessage({ id: 'pricing.priceListItem.form.active.false' }),
          },
        ]}
        required
        value={draft.active}
        {...(activeError === undefined ? {} : { error: activeError })}
      />

      <TextArea
        id={controlId('reason')}
        label={intl.formatMessage({ id: 'pricing.priceListItem.form.reason' })}
        name="reason"
        onValueChange={(next) => {
          set('reason', next)
        }}
        value={draft.reason}
        {...(reasonError === undefined ? {} : { error: reasonError })}
      />

      <Button busy={busy} type="submit" variant="primary">
        {intl.formatMessage({ id: 'pricing.priceListItem.form.save' })}
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        {intl.formatMessage({ id: 'admin.cancel' })}
      </Button>
    </form>
  )
}
