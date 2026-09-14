import { useIntl } from 'react-intl'
import type { DiscountRule, PriceListItem } from '../../billing/priceListTypes'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { Select } from '../../design-system/components/forms/Select'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import type { PricingPreviewLineDraft } from './pricingPreviewDraft'

export interface PricingPreviewLineFormProps {
  readonly draft: PricingPreviewLineDraft
  /** True while editing a line already on the form; false while adding one. */
  readonly existing: boolean
  readonly items: readonly PriceListItem[]
  readonly discountRules: readonly DiscountRule[]
  readonly busy: boolean
  readonly itemCodeError?: string
  readonly quantityError?: string
  readonly discountValueError?: string
  readonly overrideRateError?: string
  readonly onChange: (draft: PricingPreviewLineDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

/**
 * One line of the pricing preview: an item, a quantity, its surcharges, an optional discount naming
 * one of the version's own rules, and an optional override rate with its reason.
 *
 * The discount and override sections appear only once a rule is chosen or the override is switched
 * on — a value nobody asked for is not shown a field for, the way `PriceListItemForm.tsx`'s own
 * conditional sections work. Neither figure is ever computed here: the value and the rate are exactly
 * what the person typed, sent to the server for the engine to judge.
 */
export function PricingPreviewLineForm({
  draft,
  existing,
  items,
  discountRules,
  busy,
  itemCodeError,
  quantityError,
  discountValueError,
  overrideRateError,
  onChange,
  onSubmit,
  onCancel,
  controlId,
}: PricingPreviewLineFormProps) {
  const intl = useIntl()

  const set = <TKey extends keyof PricingPreviewLineDraft>(
    key: TKey,
    value: PricingPreviewLineDraft[TKey],
  ): void => {
    onChange({ ...draft, [key]: value })
  }

  const title = intl.formatMessage({
    id: existing ? 'pricing.preview.line.form.editTitle' : 'pricing.preview.line.form.addTitle',
  })

  const activeItems = items.filter((item) => item.active)
  const surchargeItems = activeItems.filter(
    (item) => item.kind === 'Surcharge' && item.code !== draft.itemCode,
  )
  const activeRules = discountRules.filter((rule) => rule.active)
  const chosenRule = activeRules.find((rule) => rule.code === draft.discountRuleCode)

  const discountUnit =
    chosenRule === undefined
      ? undefined
      : chosenRule.kind === 'Percentage'
        ? {
            symbol: intl.formatMessage({ id: 'units.percent.symbol' }),
            label: intl.formatMessage({ id: 'units.percent.label' }),
          }
        : {
            symbol: intl.formatMessage({ id: 'units.rupee.symbol' }),
            label: intl.formatMessage({ id: 'units.rupee.label' }),
            position: 'leading' as const,
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

      <Select
        id={controlId('itemCode')}
        label={intl.formatMessage({ id: 'pricing.preview.line.form.itemCode' })}
        name="itemCode"
        onValueChange={(next) => {
          set('itemCode', next)
        }}
        options={activeItems.map((item) => ({
          value: item.code,
          label: `${item.code} — ${item.description}`,
        }))}
        required
        value={draft.itemCode}
        {...(itemCodeError === undefined ? {} : { error: itemCodeError })}
      />

      <TextField
        id={controlId('quantity')}
        inputMode="decimal"
        label={intl.formatMessage({ id: 'pricing.preview.line.form.quantity' })}
        name="quantity"
        onValueChange={(next) => {
          set('quantity', next)
        }}
        required
        value={draft.quantity}
        {...(quantityError === undefined ? {} : { error: quantityError })}
      />

      {surchargeItems.length === 0 ? null : (
        <fieldset>
          <legend>{intl.formatMessage({ id: 'pricing.preview.line.form.surcharges' })}</legend>
          {surchargeItems.map((item) => (
            <Checkbox
              id={controlId(`surcharge-${item.code}`)}
              key={item.code}
              label={`${item.code} — ${item.description}`}
              name={`surcharge-${item.code}`}
              onValueChange={(next) => {
                onChange({
                  ...draft,
                  surchargeItemCodes: next
                    ? [...draft.surchargeItemCodes, item.code]
                    : draft.surchargeItemCodes.filter((held) => held !== item.code),
                })
              }}
              value={draft.surchargeItemCodes.includes(item.code)}
            />
          ))}
        </fieldset>
      )}

      <Select
        description={intl.formatMessage({ id: 'pricing.preview.line.form.discountRuleCode.hint' })}
        emptyLabel={intl.formatMessage({ id: 'pricing.preview.line.form.discountRuleCode.none' })}
        id={controlId('discountRuleCode')}
        label={intl.formatMessage({ id: 'pricing.preview.line.form.discountRuleCode' })}
        name="discountRuleCode"
        onValueChange={(next) => {
          onChange({ ...draft, discountRuleCode: next, discountValue: '', discountReason: '' })
        }}
        options={activeRules.map((rule) => ({
          value: rule.code,
          label: `${rule.code} — ${rule.description}`,
        }))}
        value={draft.discountRuleCode}
      />

      {draft.discountRuleCode === '' ? null : (
        <>
          <TextField
            id={controlId('discountValue')}
            inputMode="decimal"
            label={intl.formatMessage({ id: 'pricing.preview.line.form.discountValue' })}
            name="discountValue"
            onValueChange={(next) => {
              set('discountValue', next)
            }}
            required
            value={draft.discountValue}
            {...(discountUnit === undefined ? {} : { unit: discountUnit })}
            {...(discountValueError === undefined ? {} : { error: discountValueError })}
          />
          <TextArea
            id={controlId('discountReason')}
            label={intl.formatMessage({ id: 'pricing.preview.line.form.discountReason' })}
            name="discountReason"
            onValueChange={(next) => {
              set('discountReason', next)
            }}
            value={draft.discountReason}
          />
        </>
      )}

      <Checkbox
        id={controlId('override')}
        label={intl.formatMessage({ id: 'pricing.preview.line.form.override' })}
        name="override"
        onValueChange={(next) => {
          onChange({ ...draft, override: next, overrideRate: '', overrideReason: '' })
        }}
        value={draft.override}
      />

      {!draft.override ? null : (
        <>
          <TextField
            description={intl.formatMessage({ id: 'pricing.preview.line.form.overrideRate.hint' })}
            id={controlId('overrideRate')}
            inputMode="decimal"
            label={intl.formatMessage({ id: 'pricing.preview.line.form.overrideRate' })}
            name="overrideRate"
            onValueChange={(next) => {
              set('overrideRate', next)
            }}
            required
            unit={{
              symbol: intl.formatMessage({ id: 'units.rupee.symbol' }),
              label: intl.formatMessage({ id: 'units.rupee.label' }),
              position: 'leading',
            }}
            value={draft.overrideRate}
            {...(overrideRateError === undefined ? {} : { error: overrideRateError })}
          />
          <TextArea
            id={controlId('overrideReason')}
            label={intl.formatMessage({ id: 'pricing.preview.line.form.overrideReason' })}
            name="overrideReason"
            onValueChange={(next) => {
              set('overrideReason', next)
            }}
            value={draft.overrideReason}
          />
          <Alert live="off" tone="info">
            {intl.formatMessage({ id: 'pricing.preview.line.form.override.note' })}
          </Alert>
        </>
      )}

      <Button busy={busy} type="submit" variant="primary">
        {intl.formatMessage({ id: 'pricing.preview.line.form.save' })}
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        {intl.formatMessage({ id: 'admin.cancel' })}
      </Button>
    </form>
  )
}
