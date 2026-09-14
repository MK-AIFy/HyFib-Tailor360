import { useIntl } from 'react-intl'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Forbidden } from '../../components/states/Forbidden'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { DateField } from '../../design-system/components/forms/DateField'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import type { PriceListVersionDraft } from './priceListVersionDraft'

/**
 * The version-conventions form: name, first day, tax treatment, round-off rule, override threshold
 * and the branches it prices. Used here to start a draft; E09-F01-7 reuses it to change one, which is
 * why `mode` is its own prop rather than the create call being hard-wired in.
 *
 * ## Why the tax-treatment and round-off choices start with nothing selected
 *
 * Each is money-bearing, and `CreatePriceListDraft` refuses an omitted one rather than defaulting it
 * (`billing.value-required`) — OD-05's recorded default is that the round-off rule is version
 * configuration this client writes none of on its own. A pre-ticked radio would be this screen making
 * that choice for the person, silently, which is exactly what the server was built to refuse.
 *
 * ## Why the branch picker can replace the whole form
 *
 * `admin.branches` is a step-up permission this screen's own drafting key does not itself grant. A
 * single free-text identifier is a reasonable fallback for one branch (`GstRegistrationForm.tsx` does
 * exactly that), but a version prices a *set* of branches, and there is no honest way to offer a
 * multiple choice over a list this screen cannot read. So when the branch read fails, the form is not
 * shown at all — `Forbidden` says which permission is missing and hands back the way out, which is the
 * version list this draft would otherwise have started from.
 */
export interface PriceListVersionFormProps {
  readonly draft: PriceListVersionDraft
  readonly mode: 'start'
  /** The version number this draft was cloned from, for the informational note. Null for an empty start. */
  readonly cloneFromVersionNumber: number | string | null
  readonly branchOptions: readonly { readonly value: string; readonly label: string }[]
  readonly branchNamesAvailable: boolean
  readonly busy: boolean
  readonly nameError?: string
  readonly effectiveFromError?: string
  readonly taxInclusiveError?: string
  readonly roundOffError?: string
  readonly overrideThresholdPercentError?: string
  readonly branchIdsError?: string
  readonly onChange: (draft: PriceListVersionDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function PriceListVersionForm({
  draft,
  cloneFromVersionNumber,
  branchOptions,
  branchNamesAvailable,
  busy,
  nameError,
  effectiveFromError,
  taxInclusiveError,
  roundOffError,
  overrideThresholdPercentError,
  branchIdsError,
  onChange,
  onSubmit,
  onCancel,
  controlId,
}: PriceListVersionFormProps) {
  const intl = useIntl()

  const set = <TKey extends keyof PriceListVersionDraft>(
    key: TKey,
    value: PriceListVersionDraft[TKey],
  ): void => {
    onChange({ ...draft, [key]: value })
  }

  if (!branchNamesAvailable) {
    return (
      <Forbidden
        action={intl.formatMessage({
          id: 'pricing.priceList.version.form.branches.forbiddenAction',
        })}
        allowedRoles={['Owner', 'Admin']}
        onBack={onCancel}
      />
    )
  }

  const title =
    cloneFromVersionNumber === null
      ? intl.formatMessage({ id: 'pricing.priceList.version.form.startTitle' })
      : intl.formatMessage(
          { id: 'pricing.priceList.version.form.cloneTitle' },
          { versionNumber: cloneFromVersionNumber },
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

      {cloneFromVersionNumber === null ? null : (
        <p>
          {intl.formatMessage(
            { id: 'pricing.priceList.version.form.clonedFrom' },
            { versionNumber: cloneFromVersionNumber },
          )}
        </p>
      )}

      <TextField
        id={controlId('name')}
        label={intl.formatMessage({ id: 'pricing.priceList.version.form.name' })}
        name="name"
        onValueChange={(next) => {
          set('name', next)
        }}
        required
        value={draft.name}
        {...(nameError === undefined ? {} : { error: nameError })}
      />

      <TextArea
        id={controlId('notes')}
        label={intl.formatMessage({ id: 'pricing.priceList.version.form.notes' })}
        name="notes"
        onValueChange={(next) => {
          set('notes', next)
        }}
        value={draft.notes}
      />

      <DateField
        id={controlId('effectiveFrom')}
        label={intl.formatMessage({ id: 'pricing.priceList.version.form.effectiveFrom' })}
        name="effectiveFrom"
        onValueChange={(next) => {
          set('effectiveFrom', next)
        }}
        required
        value={draft.effectiveFrom}
        {...(effectiveFromError === undefined ? {} : { error: effectiveFromError })}
      />

      <RadioGroup
        id={controlId('taxInclusive')}
        label={intl.formatMessage({ id: 'pricing.priceList.version.form.tax' })}
        name="taxInclusive"
        onValueChange={(next) => {
          set('taxInclusive', next as PriceListVersionDraft['taxInclusive'])
        }}
        options={[
          {
            value: 'true',
            label: intl.formatMessage({ id: 'pricing.priceList.version.form.tax.inclusive' }),
          },
          {
            value: 'false',
            label: intl.formatMessage({ id: 'pricing.priceList.version.form.tax.exclusive' }),
          },
        ]}
        required
        value={draft.taxInclusive}
        {...(taxInclusiveError === undefined ? {} : { error: taxInclusiveError })}
      />

      <RadioGroup
        id={controlId('roundOff')}
        label={intl.formatMessage({ id: 'pricing.priceList.version.form.roundOff' })}
        name="roundOff"
        onValueChange={(next) => {
          set('roundOff', next)
        }}
        options={[
          {
            value: 'None',
            label: intl.formatMessage({ id: 'pricing.priceList.version.form.roundOff.none' }),
          },
          {
            value: 'NearestRupee',
            label: intl.formatMessage({
              id: 'pricing.priceList.version.form.roundOff.nearestRupee',
            }),
          },
        ]}
        required
        value={draft.roundOff}
        {...(roundOffError === undefined ? {} : { error: roundOffError })}
      />

      <TextField
        description={intl.formatMessage({ id: 'pricing.priceList.version.form.threshold.hint' })}
        id={controlId('overrideThresholdPercent')}
        label={intl.formatMessage({ id: 'pricing.priceList.version.form.threshold' })}
        name="overrideThresholdPercent"
        onValueChange={(next) => {
          set('overrideThresholdPercent', next)
        }}
        required
        unit={{
          symbol: intl.formatMessage({ id: 'units.percent.symbol' }),
          label: intl.formatMessage({ id: 'units.percent.label' }),
        }}
        value={draft.overrideThresholdPercent}
        {...(overrideThresholdPercentError === undefined
          ? {}
          : { error: overrideThresholdPercentError })}
      />

      <fieldset>
        <legend>{intl.formatMessage({ id: 'pricing.priceList.version.form.branches' })}</legend>
        <p>{intl.formatMessage({ id: 'pricing.priceList.version.form.branches.hint' })}</p>

        {branchOptions.map((branch) => (
          <Checkbox
            id={controlId(`branch-${branch.value}`)}
            key={branch.value}
            label={branch.label}
            name={`branch-${branch.value}`}
            onValueChange={(next) => {
              // One `onChange` call, not two `set()` calls: each `set()` reads this render's
              // `draft` closure, so a second call would overwrite the first's branchIds change
              // with the pre-click value while only `branchIdsTouched` took effect.
              onChange({
                ...draft,
                branchIds: next
                  ? [...draft.branchIds, branch.value]
                  : draft.branchIds.filter((held) => held !== branch.value),
                branchIdsTouched: true,
              })
            }}
            value={draft.branchIds.includes(branch.value)}
          />
        ))}

        {branchIdsError === undefined ? null : (
          <Alert live="polite" tone="danger">
            {branchIdsError}
          </Alert>
        )}
      </fieldset>

      <TextArea
        id={controlId('reason')}
        label={intl.formatMessage({ id: 'pricing.priceList.version.form.reason' })}
        name="reason"
        onValueChange={(next) => {
          set('reason', next)
        }}
        value={draft.reason}
      />

      <Button busy={busy} type="submit" variant="primary">
        {intl.formatMessage({ id: 'pricing.priceList.version.form.save' })}
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        {intl.formatMessage({ id: 'admin.cancel' })}
      </Button>
    </form>
  )
}
