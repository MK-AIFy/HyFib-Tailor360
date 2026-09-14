import { useIntl } from 'react-intl'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import { Button } from '../../components/primitives/Button'
import type { DiscountRule } from '../../billing/priceListTypes'
import { DISCOUNT_KINDS } from './discountRuleDraft'
import type { DiscountRuleDraft } from './discountRuleDraft'

export interface DiscountRuleFormProps {
  readonly draft: DiscountRuleDraft
  /** Null while adding; the rule being edited otherwise. */
  readonly existing: DiscountRule | null
  readonly busy: boolean
  readonly codeError?: string
  readonly descriptionError?: string
  readonly kindError?: string
  readonly maximumWithoutApprovalError?: string
  readonly maximumError?: string
  readonly activeError?: string
  readonly reasonError?: string
  readonly onChange: (draft: DiscountRuleDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

/**
 * Add or edit one discount rule of a draft price-list version: what a counter may take off a line,
 * and what nobody may exceed.
 *
 * `kind` and `active` are radio groups with no pre-selected option, for the same reason
 * `PriceListItemForm.tsx`'s own are: the server refuses either omitted, and what a rule discounts is
 * not this product's to assume. Neither bound is pre-filled, and neither is compared to the other
 * here — a discount boundary is the owner's decision with the accountant, and the ordering check
 * (`billing.discount-bounds-not-ordered`) is the server's alone.
 *
 * The unit shown beside each bound follows the chosen kind — a percentage through `formatPercent`'s
 * own symbol, an amount through `formatMoney`'s — so the same typed "10" is never read two ways.
 * Before a kind is chosen, neither bound carries a unit at all, rather than guessing one.
 */
export function DiscountRuleForm({
  draft,
  existing,
  busy,
  codeError,
  descriptionError,
  kindError,
  maximumWithoutApprovalError,
  maximumError,
  activeError,
  reasonError,
  onChange,
  onSubmit,
  onCancel,
  controlId,
}: DiscountRuleFormProps) {
  const intl = useIntl()

  const set = <TKey extends keyof DiscountRuleDraft>(
    key: TKey,
    value: DiscountRuleDraft[TKey],
  ): void => {
    onChange({ ...draft, [key]: value })
  }

  const title =
    existing === null
      ? intl.formatMessage({ id: 'pricing.discountRule.form.addTitle' })
      : intl.formatMessage({ id: 'pricing.discountRule.form.editTitle' }, { code: existing.code })

  const boundUnit =
    draft.kind === 'Percentage'
      ? {
          symbol: intl.formatMessage({ id: 'units.percent.symbol' }),
          label: intl.formatMessage({ id: 'units.percent.label' }),
        }
      : draft.kind === 'Amount'
        ? {
            symbol: intl.formatMessage({ id: 'units.rupee.symbol' }),
            label: intl.formatMessage({ id: 'units.rupee.label' }),
            position: 'leading' as const,
          }
        : undefined

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
        label={intl.formatMessage({ id: 'pricing.discountRule.form.code' })}
        description={intl.formatMessage({ id: 'pricing.discountRule.form.code.hint' })}
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
        label={intl.formatMessage({ id: 'pricing.discountRule.form.description' })}
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
        label={intl.formatMessage({ id: 'pricing.discountRule.form.kind' })}
        name="kind"
        onValueChange={(next) => {
          set('kind', next as DiscountRuleDraft['kind'])
        }}
        options={DISCOUNT_KINDS.map((kind) => ({
          value: kind,
          label: intl.formatMessage({ id: `pricing.discountRule.form.kind.${kind}` }),
        }))}
        required
        value={draft.kind}
        {...(kindError === undefined ? {} : { error: kindError })}
      />

      <TextField
        id={controlId('maximumWithoutApproval')}
        description={intl.formatMessage({
          id: 'pricing.discountRule.form.maximumWithoutApproval.hint',
        })}
        inputMode="decimal"
        label={intl.formatMessage({ id: 'pricing.discountRule.form.maximumWithoutApproval' })}
        name="maximumWithoutApproval"
        onValueChange={(next) => {
          set('maximumWithoutApproval', next)
        }}
        required
        value={draft.maximumWithoutApproval}
        {...(boundUnit === undefined ? {} : { unit: boundUnit })}
        {...(maximumWithoutApprovalError === undefined
          ? {}
          : { error: maximumWithoutApprovalError })}
      />

      <TextField
        id={controlId('maximum')}
        description={intl.formatMessage({ id: 'pricing.discountRule.form.maximum.hint' })}
        inputMode="decimal"
        label={intl.formatMessage({ id: 'pricing.discountRule.form.maximum' })}
        name="maximum"
        onValueChange={(next) => {
          set('maximum', next)
        }}
        required
        value={draft.maximum}
        {...(boundUnit === undefined ? {} : { unit: boundUnit })}
        {...(maximumError === undefined ? {} : { error: maximumError })}
      />

      <RadioGroup
        id={controlId('active')}
        label={intl.formatMessage({ id: 'pricing.discountRule.form.active' })}
        name="active"
        onValueChange={(next) => {
          set('active', next)
        }}
        options={[
          {
            value: 'true',
            label: intl.formatMessage({ id: 'pricing.discountRule.form.active.true' }),
          },
          {
            value: 'false',
            label: intl.formatMessage({ id: 'pricing.discountRule.form.active.false' }),
          },
        ]}
        required
        value={draft.active}
        {...(activeError === undefined ? {} : { error: activeError })}
      />

      <TextArea
        id={controlId('reason')}
        label={intl.formatMessage({ id: 'pricing.discountRule.form.reason' })}
        name="reason"
        onValueChange={(next) => {
          set('reason', next)
        }}
        value={draft.reason}
        {...(reasonError === undefined ? {} : { error: reasonError })}
      />

      <Button busy={busy} type="submit" variant="primary">
        {intl.formatMessage({ id: 'pricing.discountRule.form.save' })}
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        {intl.formatMessage({ id: 'admin.cancel' })}
      </Button>
    </form>
  )
}
