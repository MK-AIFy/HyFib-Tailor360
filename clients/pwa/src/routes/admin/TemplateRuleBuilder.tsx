import { useIntl } from 'react-intl'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Select } from '../../design-system/components/forms/Select'
import { TextField } from '../../design-system/components/forms/TextField'
import {
  MAXIMUM_CLAUSES,
  RULE_EFFECTS,
  RULE_OPERATORS,
  RULE_SCOPES,
  blankClause,
  blankRule,
} from '../../admin/templateRule'
import type {
  RuleClauseDraft,
  RuleDraft,
  RuleEffect,
  RuleError,
  RuleOperator,
  RuleScope,
} from '../../admin/templateRule'
import type { MessageKey } from '../../i18n/en-IN'

/**
 * When a field is asked for, built rather than typed.
 *
 * ## Why the builder offers exactly the language and no more
 *
 * One effect over a disjunction: no negation, no nesting, no arithmetic, at most six clauses. The
 * domain refuses anything else, and a screen that offers a control the server rejects teaches an
 * administrator that the application is unreliable. So there is no "and", no grouping and no free
 * text where an operand belongs — and where the language *is* limited, the screen says so in words
 * rather than leaving a person to discover it by being refused.
 *
 * ## Why the design operand is typed and the field operand is not
 *
 * A field operand is chosen from this version's own keys, which the screen already has. A design
 * operand names a design option group, and **there is no catalogue of them to choose from**: the
 * option groups are #30, which does not exist — nothing in the API lists one, and the only trace of
 * them is a list of identifiers inside a Catalog contract. Offering only the field scope would
 * silently drop half the language and make an existing design rule uneditable, so the scope is
 * offered with a typed code and a hint saying exactly why there is no picker. That is the same
 * answer the field editor gives `diagramKey`, for the same reason.
 *
 * ## Why the rendered sentence is the server's
 *
 * The response carries `rule` as an English sentence and `ruleDefinition` as the structure. The
 * sentence is display-only and cannot be sent back; it is shown beside the builder so an
 * administrator can read what they have built in words, and it updates when the field is saved
 * rather than as they type, because the server writes it.
 */
export interface TemplateRuleBuilderProps {
  readonly draft: RuleDraft | null
  /** Every other field key in this version, which is what a field operand may name. */
  readonly fieldKeys: readonly string[]
  /** The sentence the server rendered for the stored rule, when there is one. */
  readonly sentence: string | null
  readonly errors: readonly RuleError[]
  readonly controlId: (name: string) => string
  readonly onChange: (draft: RuleDraft | null) => void
}

export function TemplateRuleBuilder(props: TemplateRuleBuilderProps) {
  const { draft, fieldKeys, sentence, errors, controlId, onChange } = props
  const intl = useIntl()

  const errorProp = (message: string | undefined): { error?: string } =>
    message === undefined ? {} : { error: message }

  const say = (error: RuleError): string =>
    intl.formatMessage({ id: error.messageId }, error.values)

  const forClause = (index: number, ...ids: readonly MessageKey[]): string | undefined => {
    const found = errors.find((error) => error.clause === index && ids.includes(error.messageId))
    return found === undefined ? undefined : say(found)
  }

  const whole = errors.filter((error) => error.clause === undefined)

  const setClause = (index: number, next: RuleClauseDraft): void => {
    if (draft === null) {
      return
    }
    onChange({
      ...draft,
      anyOf: draft.anyOf.map((clause, at) => (at === index ? next : clause)),
    })
  }

  return (
    <fieldset>
      <legend>{intl.formatMessage({ id: 'admin.field.rule' })}</legend>
      <p>{intl.formatMessage({ id: 'admin.field.rule.hint' })}</p>

      {draft === null ? (
        <>
          <p>{intl.formatMessage({ id: 'admin.field.rule.none' })}</p>
          <Button
            onClick={() => {
              onChange(blankRule())
            }}
            type="button"
            variant="secondary"
          >
            {intl.formatMessage({ id: 'admin.field.rule.add' })}
          </Button>
        </>
      ) : (
        <>
          {whole.map((error) => (
            <Alert key={error.messageId} live="polite" tone="warning">
              {say(error)}
            </Alert>
          ))}

          <Select
            emptyLabel={null}
            id={controlId('ruleEffect')}
            label={intl.formatMessage({ id: 'admin.field.rule.effect' })}
            name="ruleEffect"
            onValueChange={(next) => {
              onChange({ ...draft, effect: next as RuleEffect })
            }}
            options={RULE_EFFECTS.map((effect) => ({
              value: effect,
              label: intl.formatMessage({
                id: `admin.field.rule.effect.${effect}` as MessageKey,
              }),
            }))}
            required
            value={draft.effect}
          />

          <p>{intl.formatMessage({ id: 'admin.field.rule.clauses' })}</p>
          <p>{intl.formatMessage({ id: 'admin.field.rule.noNesting' })}</p>

          {draft.anyOf.map((clause, index) => {
            const position = index + 1

            return (
              <fieldset key={`clause-${String(index)}`}>
                <legend>
                  {intl.formatMessage({ id: 'admin.field.rule.clause' }, { position })}
                </legend>

                <Select
                  emptyLabel={null}
                  id={controlId(`clause-${String(index)}-scope`)}
                  label={intl.formatMessage({ id: 'admin.field.rule.clause.scope' }, { position })}
                  name={`clause-${String(index)}-scope`}
                  onValueChange={(next) => {
                    // The operand belongs to the scope: a field key is not a design option-group
                    // code, so carrying it across would leave a clause naming something that cannot
                    // exist in the namespace it now points at.
                    setClause(index, { ...clause, scope: next as RuleScope, name: '' })
                  }}
                  options={RULE_SCOPES.map((scope) => ({
                    value: scope,
                    label: intl.formatMessage({
                      id: `admin.field.rule.clause.scope.${scope}` as MessageKey,
                    }),
                  }))}
                  required
                  value={clause.scope}
                />

                {clause.scope === 'Field' ? (
                  <Select
                    id={controlId(`clause-${String(index)}-name`)}
                    label={intl.formatMessage(
                      { id: 'admin.field.rule.clause.field' },
                      { position },
                    )}
                    name={`clause-${String(index)}-name`}
                    onValueChange={(next) => {
                      setClause(index, { ...clause, name: next })
                    }}
                    options={fieldKeys.map((key) => ({ value: key, label: key }))}
                    required
                    value={clause.name}
                    {...errorProp(
                      forClause(
                        index,
                        'admin.field.rule.error.operandRequired',
                        'admin.field.rule.error.operandMalformed',
                        'admin.field.rule.error.readsItself',
                      ),
                    )}
                  />
                ) : (
                  <TextField
                    description={intl.formatMessage({
                      id: 'admin.field.rule.clause.design.hint',
                    })}
                    id={controlId(`clause-${String(index)}-name`)}
                    label={intl.formatMessage(
                      { id: 'admin.field.rule.clause.design' },
                      { position },
                    )}
                    name={`clause-${String(index)}-name`}
                    onValueChange={(next) => {
                      setClause(index, { ...clause, name: next })
                    }}
                    required
                    value={clause.name}
                    {...errorProp(forClause(index, 'admin.field.rule.error.operandRequired'))}
                  />
                )}

                <Select
                  emptyLabel={null}
                  id={controlId(`clause-${String(index)}-operator`)}
                  label={intl.formatMessage(
                    { id: 'admin.field.rule.clause.operator' },
                    { position },
                  )}
                  name={`clause-${String(index)}-operator`}
                  onValueChange={(next) => {
                    setClause(index, { ...clause, operator: next as RuleOperator })
                  }}
                  options={RULE_OPERATORS.map((operator) => ({
                    value: operator,
                    label: intl.formatMessage({
                      id: `admin.field.rule.clause.operator.${operator}` as MessageKey,
                    }),
                  }))}
                  required
                  value={clause.operator}
                />

                {clause.values.map((value, valueIndex) => (
                  <TextField
                    key={`value-${String(valueIndex)}`}
                    id={controlId(`clause-${String(index)}-value-${String(valueIndex)}`)}
                    label={intl.formatMessage(
                      { id: 'admin.field.rule.clause.value' },
                      { position, index: valueIndex + 1 },
                    )}
                    name={`clause-${String(index)}-value-${String(valueIndex)}`}
                    onValueChange={(next) => {
                      setClause(index, {
                        ...clause,
                        values: clause.values.map((current, at) =>
                          at === valueIndex ? next : current,
                        ),
                      })
                    }}
                    value={value}
                    {...(valueIndex === 0
                      ? errorProp(forClause(index, 'admin.field.rule.error.noValues'))
                      : {})}
                  />
                ))}

                <Button
                  onClick={() => {
                    setClause(index, { ...clause, values: [...clause.values, ''] })
                  }}
                  type="button"
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'admin.field.rule.clause.value.add' }, { position })}
                </Button>

                {clause.values.length > 1 ? (
                  <Button
                    onClick={() => {
                      setClause(index, { ...clause, values: clause.values.slice(0, -1) })
                    }}
                    type="button"
                    variant="secondary"
                  >
                    {intl.formatMessage(
                      { id: 'admin.field.rule.clause.value.remove' },
                      { position, index: clause.values.length },
                    )}
                  </Button>
                ) : null}

                {draft.anyOf.length > 1 ? (
                  <Button
                    onClick={() => {
                      onChange({
                        ...draft,
                        anyOf: draft.anyOf.filter((_unused, at) => at !== index),
                      })
                    }}
                    type="button"
                    variant="secondary"
                  >
                    {intl.formatMessage({ id: 'admin.field.rule.clause.remove' }, { position })}
                  </Button>
                ) : null}
              </fieldset>
            )
          })}

          {draft.anyOf.length >= MAXIMUM_CLAUSES ? (
            <Alert live="polite" tone="info">
              {intl.formatMessage({ id: 'admin.field.rule.atLimit' })}
            </Alert>
          ) : (
            <Button
              onClick={() => {
                onChange({ ...draft, anyOf: [...draft.anyOf, blankClause()] })
              }}
              type="button"
              variant="secondary"
            >
              {intl.formatMessage({ id: 'admin.field.rule.clause.add' })}
            </Button>
          )}

          <h4>{intl.formatMessage({ id: 'admin.field.rule.sentence' })}</h4>
          <p>{sentence ?? intl.formatMessage({ id: 'admin.field.rule.sentence.pending' })}</p>

          <Button
            onClick={() => {
              onChange(null)
            }}
            type="button"
            variant="secondary"
          >
            {intl.formatMessage({ id: 'admin.field.rule.remove' })}
          </Button>
        </>
      )}
    </fieldset>
  )
}
