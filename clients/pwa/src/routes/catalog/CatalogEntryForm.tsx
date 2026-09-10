import { useIntl } from 'react-intl'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { Select } from '../../design-system/components/forms/Select'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import type { CatalogEntryDraft } from '../../catalog/catalogEntry'
import type { CatalogCategory } from '../../catalog/types'
import type { MessageKey } from '../../i18n/en-IN'

/** The five links, in the order they decide what happens to an order. */
const LINKS: readonly {
  readonly key: keyof CatalogEntryDraft
  readonly label: MessageKey
}[] = [
  { key: 'measurementTemplateId', label: 'catalog.link.measurementTemplateId' },
  { key: 'workflowDefinitionId', label: 'catalog.link.workflowDefinitionId' },
  { key: 'designOptionGroupIds', label: 'catalog.link.designOptionGroupIds' },
  { key: 'priceListItemCode', label: 'catalog.link.priceListItemCode' },
  { key: 'qcChecklistTemplateId', label: 'catalog.link.qcChecklistTemplateId' },
]

/**
 * The form for a category or a service type in a draft.
 *
 * ## Why the code is read-only once it exists
 *
 * A code is what every order, invoice and report files the thing under. Changing it would orphan
 * every one of them, so the server does not accept a change to it and an editable box would appear
 * to work and change nothing — the same answer, for the same reason, that a measurement field's key
 * gets.
 *
 * ## Why the five links are typed
 *
 * Each names something in another module — a measurement template, a workflow, a set of design
 * option groups, a price-list item, a QC checklist — and only the first of those has a screen that
 * could list them. Offering a picker for one and a box for four would be less honest than a box for
 * all five and a sentence saying why, which is what this does. The screen says which links are
 * missing and what each one decides, which is the part a person cannot look up.
 *
 * ## Why "offered nowhere" is a state the form can express
 *
 * Because it is a state the server has: an empty branch list means offered nowhere, not offered
 * everywhere. Unticking every branch is therefore a real answer rather than an unfinished form, and
 * the hint says so before somebody discovers it at publication.
 */
export interface CatalogEntryFormProps {
  readonly draft: CatalogEntryDraft
  readonly kind: 'category' | 'serviceType'
  /** Null when this is a new one; the existing code otherwise, which fixes the code control. */
  readonly existingCode: string | null
  /** Every category, for the parent control. Excludes the one being edited and its descendants. */
  readonly parents: readonly CatalogCategory[]
  /** Every branch this shop has, as identifier and name. */
  readonly branches: readonly { readonly branchId: string; readonly name: string }[]
  /** The branches this entry offers that its parent does not, which publication will refuse. */
  readonly branchesOutside: readonly string[]
  readonly busy: boolean
  readonly onChange: (draft: CatalogEntryDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function CatalogEntryForm(props: CatalogEntryFormProps) {
  const {
    draft,
    kind,
    existingCode,
    parents,
    branches,
    branchesOutside,
    busy,
    onChange,
    onSubmit,
    onCancel,
    controlId,
  } = props
  const intl = useIntl()

  const set = <TKey extends keyof CatalogEntryDraft>(
    key: TKey,
    value: CatalogEntryDraft[TKey],
  ): void => {
    onChange({ ...draft, [key]: value })
  }

  const title =
    existingCode === null
      ? intl.formatMessage({
          id: kind === 'category' ? 'catalog.category.addTitle' : 'catalog.serviceType.addTitle',
        })
      : intl.formatMessage(
          {
            id:
              kind === 'category' ? 'catalog.category.editTitle' : 'catalog.serviceType.editTitle',
          },
          { name: draft.name },
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
        description={intl.formatMessage({
          id: existingCode === null ? 'catalog.form.code.hint' : 'catalog.form.code.fixedHint',
        })}
        id={controlId('code')}
        label={intl.formatMessage({ id: 'catalog.form.code' })}
        name="code"
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
        id={controlId('name')}
        label={intl.formatMessage({ id: 'catalog.form.name' })}
        name="name"
        onValueChange={(next) => {
          set('name', next)
        }}
        required
        value={draft.name}
      />

      <TextField
        id={controlId('nameTamil')}
        label={intl.formatMessage({ id: 'catalog.form.nameTamil' })}
        name="nameTamil"
        onValueChange={(next) => {
          set('nameTamil', next)
        }}
        value={draft.nameTamil}
      />

      <TextArea
        id={controlId('description')}
        label={intl.formatMessage({ id: 'catalog.form.description' })}
        name="description"
        onValueChange={(next) => {
          set('description', next)
        }}
        value={draft.description}
      />

      <NumericStepper
        id={controlId('displayOrder')}
        label={intl.formatMessage({ id: 'catalog.form.displayOrder' })}
        min={0}
        name="displayOrder"
        onValueChange={(next) => {
          set('displayOrder', next)
        }}
        value={draft.displayOrder}
      />

      {kind === 'category' ? (
        <Select
          emptyLabel={intl.formatMessage({ id: 'catalog.form.parent.none' })}
          id={controlId('parentCategoryId')}
          label={intl.formatMessage({ id: 'catalog.form.parent' })}
          name="parentCategoryId"
          onValueChange={(next) => {
            set('parentCategoryId', next === '' ? null : next)
          }}
          options={parents.map((category) => ({
            value: category.categoryId,
            label: `${category.name} (${category.code})`,
          }))}
          value={draft.parentCategoryId ?? ''}
        />
      ) : null}

      <fieldset>
        <legend>{intl.formatMessage({ id: 'catalog.form.branches' })}</legend>
        <p>{intl.formatMessage({ id: 'catalog.form.branches.hint' })}</p>

        {branches.map((branch) => (
          <Checkbox
            id={controlId(`branch-${branch.branchId}`)}
            key={branch.branchId}
            label={branch.name}
            name={`branch-${branch.branchId}`}
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

        {branchesOutside.length === 0 ? null : (
          <Alert live="polite" tone="warning">
            {intl.formatMessage(
              { id: 'catalog.branches.outside' },
              { count: branchesOutside.length },
            )}
          </Alert>
        )}
      </fieldset>

      {kind === 'category' ? (
        <TextField
          id={controlId('featureFlagKey')}
          label={intl.formatMessage({ id: 'catalog.form.featureFlag' })}
          name="featureFlagKey"
          onValueChange={(next) => {
            set('featureFlagKey', next)
          }}
          value={draft.featureFlagKey}
        />
      ) : (
        <>
          <fieldset>
            <legend>{intl.formatMessage({ id: 'catalog.form.links' })}</legend>
            <p>{intl.formatMessage({ id: 'catalog.form.links.hint' })}</p>

            {LINKS.map((link) => (
              <TextField
                description={intl.formatMessage({ id: link.label })}
                id={controlId(String(link.key))}
                key={String(link.key)}
                label={intl.formatMessage({ id: link.label })}
                name={String(link.key)}
                onValueChange={(next) => {
                  set(link.key, next as never)
                }}
                value={String(draft[link.key])}
              />
            ))}

            <Checkbox
              description={intl.formatMessage({ id: 'catalog.allowIncomplete' })}
              id={controlId('allowIncomplete')}
              label={intl.formatMessage({ id: 'catalog.allowIncomplete' })}
              name="allowIncomplete"
              onValueChange={(next) => {
                set('allowIncomplete', next)
              }}
              value={draft.allowIncomplete}
            />
          </fieldset>

          <NumericStepper
            id={controlId('expectedDurationDays')}
            label={intl.formatMessage({ id: 'catalog.form.duration' })}
            min={0}
            name="expectedDurationDays"
            onValueChange={(next) => {
              set('expectedDurationDays', next)
            }}
            value={draft.expectedDurationDays}
          />

          <TextArea
            id={controlId('intakeWarning')}
            label={intl.formatMessage({ id: 'catalog.form.intakeWarning' })}
            name="intakeWarning"
            onValueChange={(next) => {
              set('intakeWarning', next)
            }}
            value={draft.intakeWarning}
          />
        </>
      )}

      <TextArea
        id={controlId('reason')}
        label={intl.formatMessage({ id: 'catalog.form.reason' })}
        name="reason"
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
