import type { MessageKey } from '../i18n/en-IN'
import type { TemplateRule, TemplateRuleClause } from './types'

/**
 * The conditional-visibility language, as the client may build it and as it decides.
 *
 * ## Why the client evaluates it at all
 *
 * The server is the authority, and this is not a second one: it exists so the builder can show, as
 * somebody assembles a rule, what that rule would do — which is the only way to notice that a rule
 * reads correctly in English and does the opposite of what was meant. Every rule below therefore
 * mirrors `ConditionalRule` exactly, beside a citation of it, and the cases that are easy to get
 * subtly wrong are the ones with tests naming the domain's own comment.
 *
 * ## The language, and what it will never do
 *
 * One effect over a disjunction. No negation, no nesting, no arithmetic, at most six clauses. That
 * is deliberate: a rule language that can express more than the shop needs is a language an
 * administrator can write a bug in without a deployment to catch it. So the builder offers exactly
 * this and no more — a screen that offers a control the server rejects teaches an administrator that
 * the application is unreliable.
 */

/** What a rule does to the field that carries it. */
export const RULE_EFFECTS = ['ShownWhen', 'HiddenWhen'] as const

export type RuleEffect = (typeof RULE_EFFECTS)[number]

/** Where a clause reads its left-hand side from. */
export const RULE_SCOPES = ['Field', 'DesignSelection'] as const

export type RuleScope = (typeof RULE_SCOPES)[number]

/** How a clause compares. */
export const RULE_OPERATORS = ['IsAnyOf', 'Excludes'] as const

export type RuleOperator = (typeof RULE_OPERATORS)[number]

/**
 * The most clauses one rule may carry (`ConditionalRule.MaximumClauses`).
 *
 * Six is more than any seeded rule needs and few enough that the rule still reads as a sentence. A
 * rule that needs more is describing something the template should express as a separate field.
 */
export const MAXIMUM_CLAUSES = 6

/** One clause as the builder holds it: values are a list the person edits one at a time. */
export interface RuleClauseDraft {
  readonly scope: RuleScope
  readonly name: string
  readonly operator: RuleOperator
  readonly values: readonly string[]
}

/** A rule as the builder holds it, or null for a field with no rule at all. */
export interface RuleDraft {
  readonly effect: RuleEffect
  readonly anyOf: readonly RuleClauseDraft[]
}

/** A clause nobody has filled in yet. */
export function blankClause(): RuleClauseDraft {
  return { scope: 'Field', name: '', operator: 'IsAnyOf', values: [''] }
}

/** A rule nobody has filled in yet. `ShownWhen` first, because it is how the seeded rules read. */
export function blankRule(): RuleDraft {
  return { effect: 'ShownWhen', anyOf: [blankClause()] }
}

/** The stored rule as the builder holds it, or null when the field carries none. */
export function ruleToDraft(rule: TemplateRule | null): RuleDraft | null {
  if (rule === null) {
    return null
  }

  return {
    effect: asEffect(rule.effect),
    anyOf: rule.anyOf.map((clause) => ({
      scope: asScope(clause.scope),
      name: clause.name,
      operator: asOperator(clause.operator),
      values: [...clause.values],
    })),
  }
}

/**
 * The builder's rule as the wire carries it, or null when there is nothing to send.
 *
 * Empty values are dropped rather than sent: a clause with no values is refused
 * (`ClauseHasNoValues`), and a half-typed row is a person mid-edit rather than an instruction.
 */
export function draftToRule(draft: RuleDraft | null): TemplateRule | null {
  if (draft === null) {
    return null
  }

  return {
    effect: draft.effect,
    anyOf: draft.anyOf.map((clause) => ({
      scope: clause.scope,
      name: clause.name.trim(),
      operator: clause.operator,
      values: clause.values.map((value) => value.trim()).filter((value) => value !== ''),
    })),
  }
}

function asEffect(value: string): RuleEffect {
  return (RULE_EFFECTS as readonly string[]).includes(value) ? (value as RuleEffect) : 'ShownWhen'
}

function asScope(value: string): RuleScope {
  return (RULE_SCOPES as readonly string[]).includes(value) ? (value as RuleScope) : 'Field'
}

function asOperator(value: string): RuleOperator {
  return (RULE_OPERATORS as readonly string[]).includes(value) ? (value as RuleOperator) : 'IsAnyOf'
}

/** A refusal about one clause, or about the rule as a whole when `clause` is undefined. */
export interface RuleError {
  readonly messageId: MessageKey
  readonly clause?: number
  readonly values?: Readonly<Record<string, string | number>>
}

/** `FieldKey.Pattern`, for an operand naming another field of this version. */
const KEY_PATTERN = /^[a-z][a-z0-9_]*$/

/**
 * Everything the server would refuse about this rule, checked before it is asked.
 *
 * Two of these are worth surfacing as guidance rather than as an error after the fact, and the issue
 * names both: a field whose rule reads its own key can never settle — it is hidden, so it has no
 * value, so it is shown, so it has one — and a rule with no clauses can never show its field at all.
 *
 * The cycle check between *different* fields stays the server's, at publish: it needs the whole
 * version's rules at once, and a cycle a person is halfway through building is not yet a mistake.
 *
 * @param draft The rule as built.
 * @param ownKey The key of the field carrying it, which no clause may name.
 */
export function validateRule(draft: RuleDraft | null, ownKey: string): readonly RuleError[] {
  if (draft === null) {
    return []
  }

  const errors: RuleError[] = []

  if (draft.anyOf.length === 0) {
    // `HidesUnconditionally`: with nothing to match, `HiddenWhen` always hides and `ShownWhen` never
    // shows. Either way the field can never be captured.
    errors.push({ messageId: 'admin.field.rule.error.noClauses' })
  }

  if (draft.anyOf.length > MAXIMUM_CLAUSES) {
    errors.push({
      messageId: 'admin.field.rule.error.tooManyClauses',
      values: { limit: MAXIMUM_CLAUSES },
    })
  }

  draft.anyOf.forEach((clause, index) => {
    const name = clause.name.trim()
    const values = clause.values.map((value) => value.trim()).filter((value) => value !== '')

    if (name === '') {
      errors.push({ messageId: 'admin.field.rule.error.operandRequired', clause: index })
    } else if (clause.scope === 'Field' && !KEY_PATTERN.test(name)) {
      errors.push({ messageId: 'admin.field.rule.error.operandMalformed', clause: index })
    } else if (clause.scope === 'Field' && name === ownKey.trim()) {
      errors.push({ messageId: 'admin.field.rule.error.readsItself', clause: index })
    }

    if (values.length === 0) {
      errors.push({ messageId: 'admin.field.rule.error.noValues', clause: index })
    }
  })

  return errors
}

/** What a rule decides, and whether it could decide at all (`RuleEvaluation`). */
export interface RuleEvaluation {
  readonly isShown: boolean
  readonly decided: boolean
}

/**
 * Whether the field is shown, given what is known.
 *
 * A faithful mirror of `ConditionalRule.Evaluate`, and the two subtleties are the whole reason this
 * exists rather than a simpler approximation:
 *
 *  - **One clause that matched settles the whole disjunction.** The clauses are joined by *or*, so
 *    no operand still unread can turn a true disjunction false. A rule is genuinely undecided only
 *    when nothing matched *and* something could not be read — otherwise a preview would call a field
 *    optional that a known, matching clause has conclusively shown.
 *  - **An undecidable rule shows the field.** Hiding on missing information drops a measurement the
 *    tailor needs; showing an unnecessary one costs a skipped field.
 *
 * @param draft The rule.
 * @param values The values captured so far, by field key.
 * @param selections The garment's design selections by option-group code — empty outside an order,
 *   which is what makes a design clause unreadable at the counter rather than false.
 */
export function evaluateRule(
  draft: RuleDraft | null,
  values: Readonly<Record<string, string>>,
  selections: Readonly<Record<string, readonly string[]>>,
): RuleEvaluation {
  if (draft === null) {
    return { isShown: true, decided: true }
  }

  if (draft.anyOf.length === 0) {
    return { isShown: draft.effect !== 'HiddenWhen', decided: true }
  }

  let decided = true
  let matched = false

  for (const clause of draft.anyOf) {
    const verdict = matchClause(clause, values, selections)

    if (verdict === null) {
      decided = false
      continue
    }

    matched ||= verdict
  }

  decided ||= matched

  if (!decided) {
    return { isShown: true, decided: false }
  }

  return { isShown: draft.effect === 'HiddenWhen' ? !matched : matched, decided: true }
}

/** True, false, or null for an operand that could not be read at all. */
function matchClause(
  clause: RuleClauseDraft,
  values: Readonly<Record<string, string>>,
  selections: Readonly<Record<string, readonly string[]>>,
): boolean | null {
  const wanted = clause.values.map((value) => value.trim()).filter((value) => value !== '')

  if (clause.scope === 'Field') {
    const value = values[clause.name]
    if (value === undefined) {
      return null
    }
    return clause.operator === 'IsAnyOf' ? wanted.includes(value) : !wanted.includes(value)
  }

  const selected = selections[clause.name]
  if (selected === undefined) {
    return null
  }
  const any = wanted.some((value) => selected.includes(value))
  return clause.operator === 'IsAnyOf' ? any : !any
}

/** Whether two clause lists describe the same rule, for deciding whether a save is needed. */
export function sameClause(left: TemplateRuleClause, right: TemplateRuleClause): boolean {
  return (
    left.scope === right.scope &&
    left.name === right.name &&
    left.operator === right.operator &&
    left.values.length === right.values.length &&
    left.values.every((value, index) => value === right.values[index])
  )
}
