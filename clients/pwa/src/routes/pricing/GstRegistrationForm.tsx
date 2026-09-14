import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { DateField } from '../../design-system/components/forms/DateField'
import { Select } from '../../design-system/components/forms/Select'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import type { GstRegistration } from '../../billing/pricingAdminTypes'
import type { GstRegistrationDraft } from './gstRegistrationDraft'

/**
 * The form for recording or amending a branch's GST registration.
 *
 * ## Why the branch is a `Select` when recording and read-only when amending
 *
 * A new registration needs a branch chosen; an amendment's branch never changes
 * (`AmendGstRegistration`'s own summary), so the form states it as a fact and still sends it, because
 * the `PUT` is whole-value and a field left out is refused, not kept.
 *
 * ## Why the branch field degrades to a plain identifier
 *
 * `listBranches()` asks for `admin.branches`, which the drafting permission this screen lives behind
 * does not itself grant. A caller who holds `billing.manage_price_lists` but not `admin.branches`
 * still has to be able to record a registration, so the field falls back to a typed identifier and
 * says why, rather than the screen refusing to work at all.
 */
export interface GstRegistrationFormProps {
  readonly draft: GstRegistrationDraft
  /** Null for a new registration; the one being amended otherwise, which fixes the branch. */
  readonly existing: GstRegistration | null
  /** The branch's name, when it could be read; undefined when `admin.branches` could not be read. */
  readonly branchName: (branchId: string) => string | undefined
  readonly branchOptions: readonly { readonly value: string; readonly label: string }[]
  readonly branchNamesAvailable: boolean
  readonly busy: boolean
  readonly gstinError?: string
  readonly stateCodeError?: string
  readonly effectiveToError?: string
  readonly onChange: (draft: GstRegistrationDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function GstRegistrationForm({
  draft,
  existing,
  branchName,
  branchOptions,
  branchNamesAvailable,
  busy,
  gstinError,
  stateCodeError,
  effectiveToError,
  onChange,
  onSubmit,
  onCancel,
  controlId,
}: GstRegistrationFormProps) {
  const intl = useIntl()

  const set = <TKey extends keyof GstRegistrationDraft>(
    key: TKey,
    value: GstRegistrationDraft[TKey],
  ): void => {
    onChange({ ...draft, [key]: value })
  }

  const title =
    existing === null
      ? intl.formatMessage({ id: 'pricing.gst.form.addTitle' })
      : intl.formatMessage({ id: 'pricing.gst.form.editTitle' }, { gstin: existing.gstin })

  return (
    <form
      aria-label={title}
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit()
      }}
    >
      <h3>{title}</h3>

      {existing === null ? (
        branchNamesAvailable ? (
          <Select
            id={controlId('branchId')}
            label={intl.formatMessage({ id: 'pricing.gst.form.branch' })}
            name="branchId"
            onValueChange={(next) => {
              set('branchId', next)
            }}
            options={branchOptions}
            required
            value={draft.branchId}
          />
        ) : (
          <TextField
            description={intl.formatMessage({ id: 'pricing.gst.form.branch.namesUnavailable' })}
            id={controlId('branchId')}
            label={intl.formatMessage({ id: 'pricing.gst.form.branch.id' })}
            name="branchId"
            onValueChange={(next) => {
              set('branchId', next)
            }}
            required
            value={draft.branchId}
          />
        )
      ) : (
        <TextField
          description={intl.formatMessage({ id: 'pricing.gst.form.branch.readOnlyHint' })}
          id={controlId('branchId')}
          label={intl.formatMessage({ id: 'pricing.gst.form.branch' })}
          name="branchId"
          readOnly
          value={branchName(existing.branchId) ?? existing.branchId}
        />
      )}

      <TextField
        description={intl.formatMessage({ id: 'pricing.gst.form.gstin.hint' })}
        id={controlId('gstin')}
        label={intl.formatMessage({ id: 'pricing.gst.form.gstin' })}
        name="gstin"
        onValueChange={(next) => {
          set('gstin', next)
        }}
        required
        value={draft.gstin}
        {...(gstinError === undefined ? {} : { error: gstinError })}
      />

      <TextField
        description={intl.formatMessage({ id: 'pricing.gst.form.stateCode.hint' })}
        id={controlId('stateCode')}
        label={intl.formatMessage({ id: 'pricing.gst.form.stateCode' })}
        name="stateCode"
        onValueChange={(next) => {
          set('stateCode', next)
        }}
        required
        value={draft.stateCode}
        {...(stateCodeError === undefined ? {} : { error: stateCodeError })}
      />

      <TextField
        id={controlId('legalName')}
        label={intl.formatMessage({ id: 'pricing.gst.form.legalName' })}
        name="legalName"
        onValueChange={(next) => {
          set('legalName', next)
        }}
        required
        value={draft.legalName}
      />

      <TextField
        id={controlId('tradeName')}
        label={intl.formatMessage({ id: 'pricing.gst.form.tradeName' })}
        name="tradeName"
        onValueChange={(next) => {
          set('tradeName', next)
        }}
        value={draft.tradeName}
      />

      <DateField
        id={controlId('effectiveFrom')}
        label={intl.formatMessage({ id: 'pricing.gst.form.effectiveFrom' })}
        name="effectiveFrom"
        onValueChange={(next) => {
          set('effectiveFrom', next)
        }}
        required
        value={draft.effectiveFrom}
      />

      <DateField
        description={intl.formatMessage({ id: 'pricing.gst.form.effectiveTo.hint' })}
        id={controlId('effectiveTo')}
        label={intl.formatMessage({ id: 'pricing.gst.form.effectiveTo' })}
        name="effectiveTo"
        onValueChange={(next) => {
          set('effectiveTo', next)
        }}
        value={draft.effectiveTo}
        {...(effectiveToError === undefined ? {} : { error: effectiveToError })}
      />

      <TextArea
        id={controlId('reason')}
        label={intl.formatMessage({ id: 'pricing.gst.form.reason' })}
        name="reason"
        onValueChange={(next) => {
          set('reason', next)
        }}
        value={draft.reason}
      />

      <Button busy={busy} type="submit" variant="primary">
        {intl.formatMessage({ id: 'pricing.gst.form.save' })}
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        {intl.formatMessage({ id: 'admin.cancel' })}
      </Button>
    </form>
  )
}
