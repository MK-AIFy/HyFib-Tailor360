import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { Select } from '../../design-system/components/forms/Select'
import { TextArea } from '../../design-system/components/forms/TextArea'
import type { CatalogDesignGroup } from '../../catalog/types'
import type { DesignRuleFormDraft } from '../../catalog/designRuleEntry'
import type { DesignOperandDraft, DesignRuleDraft } from '../../catalog/designRuleSentence'
import { describeDesignRuleDraft } from '../../catalog/designRuleSentence'

/** The seven operand forms the rule grammar defines (`docs/prd/design-options.md` section 4). */
const OPERAND_FORMS = [
  'Always',
  'AnySelection',
  'Equals',
  'NotEquals',
  'In',
  'Includes',
  'Excludes',
]

/** The four rule types. */
const RULE_TYPES = ['Requires', 'Excludes', 'RequiresAttachment', 'Note']

function toSentenceDraft(draft: DesignRuleFormDraft): DesignRuleDraft {
  return {
    type: draft.type,
    antecedent: draft.antecedent,
    consequent: draft.hasConsequent ? draft.consequent : null,
    note: draft.note,
  }
}

interface OperandFieldsProps {
  readonly label: string
  readonly groups: readonly CatalogDesignGroup[]
  readonly value: DesignOperandDraft
  readonly onChange: (value: DesignOperandDraft) => void
  readonly controlId: (name: string) => string
  readonly idPrefix: string
}

/**
 * One side of a rule, composed from the operand grammar rather than typed as text.
 *
 * The group and option pickers are scoped to the category's own groups — never another category's —
 * which is what keeps a rule from crossing categories by construction rather than by a check this
 * form would have to duplicate from the server.
 */
function OperandFields({
  label,
  groups,
  value,
  onChange,
  controlId,
  idPrefix,
}: OperandFieldsProps) {
  const intl = useIntl()
  const needsGroup = value.form !== 'Always'
  const needsOptions = needsGroup && value.form !== 'AnySelection'
  const group = groups.find((candidate) => candidate.code === value.groupCode)

  return (
    <fieldset>
      <legend>{label}</legend>

      <Select
        id={controlId(`${idPrefix}-form`)}
        label={intl.formatMessage({ id: 'catalog.design.rule.operand.form' })}
        name={`${idPrefix}-form`}
        onValueChange={(next) => {
          onChange({ ...value, form: next })
        }}
        options={OPERAND_FORMS.map((form) => ({
          value: form,
          label: intl.formatMessage({ id: `catalog.design.rule.operand.form.${form}` }),
        }))}
        value={value.form}
      />

      {needsGroup ? (
        <Select
          emptyLabel={intl.formatMessage({ id: 'catalog.design.rule.operand.group.none' })}
          id={controlId(`${idPrefix}-group`)}
          label={intl.formatMessage({ id: 'catalog.design.rule.operand.group' })}
          name={`${idPrefix}-group`}
          onValueChange={(next) => {
            onChange({ ...value, groupCode: next === '' ? null : next, optionCodes: [] })
          }}
          options={groups.map((candidate) => ({ value: candidate.code, label: candidate.name }))}
          value={value.groupCode ?? ''}
        />
      ) : null}

      {needsOptions ? (
        <fieldset>
          <legend>{intl.formatMessage({ id: 'catalog.design.rule.operand.options' })}</legend>
          {group === undefined ? (
            <p>{intl.formatMessage({ id: 'catalog.design.rule.operand.group.pickFirst' })}</p>
          ) : (
            group.options.map((option) => (
              <Checkbox
                id={controlId(`${idPrefix}-option-${option.code}`)}
                key={option.code}
                label={option.name}
                name={`${idPrefix}-option-${option.code}`}
                onValueChange={(checked) => {
                  onChange({
                    ...value,
                    optionCodes: checked
                      ? [...value.optionCodes, option.code]
                      : value.optionCodes.filter((code) => code !== option.code),
                  })
                }}
                value={value.optionCodes.includes(option.code)}
              />
            ))
          )}
        </fieldset>
      ) : null}
    </fieldset>
  )
}

/**
 * The rule editor: composes a requires, excludes, requires-attachment or note rule from the operand
 * grammar, and shows the rule's own sentence back before it is saved (#141).
 *
 * ## Why the sentence is composed here rather than asked of the server
 *
 * There is no rule to ask about yet — this is what somebody is still deciding to send. The sentence
 * is a preview of intent, not a claim about what the catalogue will do: the server's own publication
 * checks are still the authority on whether the rule the operands describe actually holds together
 * with the rest of the category (`catalog.check`), and a save this form cannot complete is refused in
 * the server's own words, shown the same way every other refusal on this screen is.
 */
export interface DesignRuleFormProps {
  readonly draft: DesignRuleFormDraft
  readonly groups: readonly CatalogDesignGroup[]
  readonly busy: boolean
  readonly onChange: (draft: DesignRuleFormDraft) => void
  readonly onSubmit: () => void
  readonly onCancel: () => void
  readonly controlId: (name: string) => string
}

export function DesignRuleForm(props: DesignRuleFormProps) {
  const { draft, groups, busy, onChange, onSubmit, onCancel, controlId } = props
  const intl = useIntl()

  const title = intl.formatMessage({ id: 'catalog.design.rule.formTitle' })
  const sentence = describeDesignRuleDraft(groups, toSentenceDraft(draft))

  return (
    <form
      aria-label={title}
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit()
      }}
    >
      <h4>{title}</h4>

      <Select
        id={controlId('rule-type')}
        label={intl.formatMessage({ id: 'catalog.design.rule.type' })}
        name="rule-type"
        onValueChange={(next) => {
          onChange({ ...draft, type: next })
        }}
        options={RULE_TYPES.map((type) => ({
          value: type,
          label: intl.formatMessage({ id: `catalog.design.rule.type.${type}` }),
        }))}
        value={draft.type}
      />

      <OperandFields
        controlId={controlId}
        groups={groups}
        idPrefix="antecedent"
        label={intl.formatMessage({ id: 'catalog.design.rule.antecedent' })}
        onChange={(next) => {
          onChange({ ...draft, antecedent: next })
        }}
        value={draft.antecedent}
      />

      <Checkbox
        description={intl.formatMessage({ id: 'catalog.design.rule.hasConsequent.hint' })}
        id={controlId('rule-hasConsequent')}
        label={intl.formatMessage({ id: 'catalog.design.rule.hasConsequent' })}
        name="rule-hasConsequent"
        onValueChange={(next) => {
          onChange({ ...draft, hasConsequent: next })
        }}
        value={draft.hasConsequent}
      />

      {draft.hasConsequent ? (
        <OperandFields
          controlId={controlId}
          groups={groups}
          idPrefix="consequent"
          label={intl.formatMessage({ id: 'catalog.design.rule.consequent' })}
          onChange={(next) => {
            onChange({ ...draft, consequent: next })
          }}
          value={draft.consequent}
        />
      ) : null}

      <TextArea
        id={controlId('rule-note')}
        label={intl.formatMessage({ id: 'catalog.design.rule.note' })}
        name="rule-note"
        onValueChange={(next) => {
          onChange({ ...draft, note: next })
        }}
        value={draft.note}
      />

      <TextArea
        description={intl.formatMessage({ id: 'catalog.design.rule.why.hint' })}
        id={controlId('rule-why')}
        label={intl.formatMessage({ id: 'catalog.design.rule.why' })}
        name="rule-why"
        onValueChange={(next) => {
          onChange({ ...draft, why: next })
        }}
        value={draft.why}
      />

      <p aria-live="polite">
        {intl.formatMessage({ id: 'catalog.design.rule.sentence' })} <strong>{sentence}</strong>
      </p>

      <TextArea
        id={controlId('rule-reason')}
        label={intl.formatMessage({ id: 'catalog.form.reason' })}
        name="rule-reason"
        onValueChange={(next) => {
          onChange({ ...draft, reason: next })
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
