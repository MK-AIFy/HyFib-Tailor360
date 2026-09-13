import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { Select } from '../../design-system/components/forms/Select'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import type { DesignGroupDraft } from '../../catalog/designGroupEntry'

/**
 * The form for a design option group on a draft (#141).
 *
 * ## Why the code is read-only once it exists
 *
 * The same reason a category's is: `docs/prd/design-options.md` fixes a group's code once the
 * version publishes, because rules, snapshots and job cards refer to it. An editable box would
 * appear to work and silently orphan every rule that already names the group.
 *
 * ## Why the branches here, rather than "wherever the category is offered"
 *
 * A group's own branch list narrows which of the category's branches actually offer *this* group —
 * a shop with two branches might sell the same category everywhere but only stock the pleat sleeve
 * neckline at one. Empty means offered nowhere, the same rule the category and service-type forms
 * give branch lists throughout this screen.
 */
export interface DesignGroupFormProps {
  readonly draft: DesignGroupDraft
  readonly existingCode: string | null
  readonly branches: readonly { readonly branchId: string; readonly name: string }[]
  readonly busy: boolean
  readonly onChange: (draft: DesignGroupDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function DesignGroupForm(props: DesignGroupFormProps) {
  const { draft, existingCode, branches, busy, onChange, onSubmit, onCancel, controlId } = props
  const intl = useIntl()

  const set = <TKey extends keyof DesignGroupDraft>(
    key: TKey,
    value: DesignGroupDraft[TKey],
  ): void => {
    onChange({ ...draft, [key]: value })
  }

  const title =
    existingCode === null
      ? intl.formatMessage({ id: 'catalog.design.group.addTitle' })
      : intl.formatMessage({ id: 'catalog.design.group.editTitle' }, { name: draft.name })

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
              ? 'catalog.design.group.code.hint'
              : 'catalog.form.code.fixedHint',
        })}
        id={controlId('group-code')}
        label={intl.formatMessage({ id: 'catalog.form.code' })}
        name="group-code"
        {...(existingCode === null
          ? {
              onValueChange: (next: string) => {
                set('code', next)
              },
              required: true,
            }
          : { readOnly: true })}
        value={draft.code}
      />

      <TextField
        id={controlId('group-name')}
        label={intl.formatMessage({ id: 'catalog.form.name' })}
        name="group-name"
        onValueChange={(next) => {
          set('name', next)
        }}
        required
        value={draft.name}
      />

      <TextField
        id={controlId('group-nameTamil')}
        label={intl.formatMessage({ id: 'catalog.form.nameTamil' })}
        name="group-nameTamil"
        onValueChange={(next) => {
          set('nameTamil', next)
        }}
        value={draft.nameTamil}
      />

      <Select
        id={controlId('group-selectionMode')}
        label={intl.formatMessage({ id: 'catalog.design.group.selectionMode' })}
        name="group-selectionMode"
        onValueChange={(next) => {
          set('selectionMode', next)
        }}
        options={[
          {
            value: 'SingleChoice',
            label: intl.formatMessage({ id: 'catalog.design.group.selectionMode.SingleChoice' }),
          },
          {
            value: 'MultipleChoice',
            label: intl.formatMessage({ id: 'catalog.design.group.selectionMode.MultipleChoice' }),
          },
        ]}
        value={draft.selectionMode}
      />

      <Checkbox
        description={intl.formatMessage({ id: 'catalog.design.group.required.hint' })}
        id={controlId('group-required')}
        label={intl.formatMessage({ id: 'catalog.design.group.required' })}
        name="group-required"
        onValueChange={(next) => {
          set('required', next)
        }}
        value={draft.required}
      />

      <NumericStepper
        id={controlId('group-displayOrder')}
        label={intl.formatMessage({ id: 'catalog.form.displayOrder' })}
        min={0}
        name="group-displayOrder"
        onValueChange={(next) => {
          set('displayOrder', next)
        }}
        value={draft.displayOrder}
      />

      <fieldset>
        <legend>{intl.formatMessage({ id: 'catalog.form.branches' })}</legend>
        <p>{intl.formatMessage({ id: 'catalog.design.group.branches.hint' })}</p>

        {branches.map((branch) => (
          <Checkbox
            id={controlId(`group-branch-${branch.branchId}`)}
            key={branch.branchId}
            label={branch.name}
            name={`group-branch-${branch.branchId}`}
            onValueChange={(next) => {
              set(
                'branchIds',
                next
                  ? [...draft.branchIds, branch.branchId]
                  : draft.branchIds.filter((held) => held !== branch.branchId),
              )
            }}
            value={draft.branchIds.includes(branch.branchId)}
          />
        ))}
      </fieldset>

      <TextArea
        id={controlId('group-reason')}
        label={intl.formatMessage({ id: 'catalog.form.reason' })}
        name="group-reason"
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
