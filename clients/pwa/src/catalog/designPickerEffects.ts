import type { CatalogDesignOperand, DesignPickerGroup, DesignPickerRule } from './types'

/**
 * What the picker's rules mean for the cards on screen, ahead of any network round trip (#142).
 *
 * ## Why this exists alongside `…/check`
 *
 * `docs/prd/design-options.md` section 4 rule 6 is explicit: "the picker's copy is a convenience";
 * the server's evaluation, from `…/check` after a save, is what decides whether a selection set is
 * confirmable, what a `requires` rule actually auto-selected, and what notes apply. This module
 * answers a narrower question that a round trip cannot answer fast enough to feel like a picker at
 * all — *before* anything is saved, which cards should already read as offered, disabled or needing a
 * photo, so that tapping a forbidden combination is prevented rather than tapped and then refused.
 *
 * It deliberately does not attempt the fixed-point resolution `DesignRuleEngine.Evaluate` runs
 * server-side (excludes narrowing requires to a fixed point, auto-selection, the required-group
 * sweep): that is genuinely stateful across rules and is exactly the part rule 6 reserves to the
 * server. This only asks, rule by rule and independently, "does this rule's antecedent hold against
 * what is chosen right now" — the same single-operand test the engine itself uses, `Holds` — and
 * reports what each rule that holds implies. It is safe to be a strict subset: everything this misses
 * still gets caught by the next save-and-check cycle.
 */

/** What is chosen, by group code. A group not present is unset — never an empty array. */
export type DesignPickerSelections = ReadonlyMap<string, readonly string[]>

/** Why one rule's effect applies: the rule and the antecedent that fired it. */
export interface DesignPickerEffectReason {
  readonly ruleIdentifier: string
  readonly antecedent: CatalogDesignOperand
  readonly consequent: CatalogDesignOperand | null
}

/** What the rules that currently hold mean for the picker's cards. */
export interface DesignPickerEffects {
  /** Options an `excludes` rule forbids alongside what is already chosen, keyed `groupCode.optionCode`. */
  readonly disabledOptions: ReadonlyMap<string, DesignPickerEffectReason>
  /** Groups a `requires` rule asks something of, keyed by the required group's code. */
  readonly requiredGroups: ReadonlyMap<string, DesignPickerEffectReason>
  /** Every `requires attachment` rule currently holding. */
  readonly attachmentReasons: readonly DesignPickerEffectReason[]
  /** Every `note` rule currently holding, in rule order. */
  readonly activeNotes: readonly DesignPickerEffectReason[]
}

function optionsOf(
  selections: DesignPickerSelections,
  groupCode: string | null,
): readonly string[] {
  if (groupCode === null) {
    return []
  }
  return selections.get(groupCode) ?? []
}

/** Whether an operand holds against a selection set. An unset group makes every form but `Always` false. */
export function operandHolds(
  operand: CatalogDesignOperand,
  selections: DesignPickerSelections,
): boolean {
  if (operand.form === 'Always') {
    return true
  }

  const held = optionsOf(selections, operand.groupCode)
  if (held.length === 0) {
    return false
  }

  switch (operand.form) {
    case 'AnySelection':
      return true
    case 'Equals':
      return held.length === 1 && held[0] === operand.optionCodes[0]
    case 'NotEquals':
      return !(held.length === 1 && held[0] === operand.optionCodes[0])
    case 'In':
      return held.some((code) => operand.optionCodes.includes(code))
    case 'Includes':
      return operand.optionCodes.length > 0 && held.includes(operand.optionCodes[0] ?? '')
    case 'Excludes':
      return operand.optionCodes.length > 0 && !held.includes(operand.optionCodes[0] ?? '')
    default:
      return false
  }
}

/** Every option code a group currently offers — the picker already narrows this to what is active. */
function offeredOptionCodes(
  groups: readonly DesignPickerGroup[],
  groupCode: string | null,
): readonly string[] {
  if (groupCode === null) {
    return []
  }
  return (
    groups.find((group) => group.code === groupCode)?.options.map((option) => option.code) ?? []
  )
}

/**
 * The concrete option codes that satisfy an operand — empty for `Always` and `AnySelection`, which
 * name none. `NotEquals` and `Excludes` name the one option they read, but the set they *satisfy* is
 * everything else the group offers, the same complement `DesignRuleOperand.SatisfyingSet` computes
 * server-side: a rule reading `sleeve_style ≠ SLEEVELESS` is satisfied by every sleeve but that one.
 */
function targetOptionCodes(
  operand: CatalogDesignOperand,
  groups: readonly DesignPickerGroup[],
): readonly string[] {
  if (operand.form === 'Always' || operand.form === 'AnySelection') {
    return []
  }
  if (operand.form === 'NotEquals' || operand.form === 'Excludes') {
    const named = operand.optionCodes[0]
    return offeredOptionCodes(groups, operand.groupCode).filter((code) => code !== named)
  }
  return operand.optionCodes
}

/** Evaluates every rule's antecedent against the current selections, independently of the others. */
export function evaluateDesignPickerEffects(
  groups: readonly DesignPickerGroup[],
  rules: readonly DesignPickerRule[],
  selections: DesignPickerSelections,
): DesignPickerEffects {
  const disabledOptions = new Map<string, DesignPickerEffectReason>()
  const requiredGroups = new Map<string, DesignPickerEffectReason>()
  const attachmentReasons: DesignPickerEffectReason[] = []
  const activeNotes: DesignPickerEffectReason[] = []

  for (const rule of rules) {
    if (!operandHolds(rule.antecedent, selections)) {
      continue
    }

    const reason: DesignPickerEffectReason = {
      ruleIdentifier: rule.identifier,
      antecedent: rule.antecedent,
      consequent: rule.consequent,
    }

    if (rule.type === 'Excludes' && rule.consequent?.groupCode !== null) {
      const targetGroup = rule.consequent?.groupCode
      for (const code of targetOptionCodes(rule.consequent ?? rule.antecedent, groups)) {
        if (targetGroup !== null && targetGroup !== undefined) {
          disabledOptions.set(`${targetGroup}.${code}`, reason)
        }
      }
    } else if (rule.type === 'Requires' && rule.consequent?.groupCode != null) {
      const group = rule.consequent.groupCode
      if (!isSatisfiedByCurrent(rule.consequent, selections)) {
        requiredGroups.set(group, reason)
      }
    } else if (rule.type === 'RequiresAttachment') {
      attachmentReasons.push(reason)
    } else if (rule.type === 'Note') {
      activeNotes.push(reason)
    }
  }

  return { disabledOptions, requiredGroups, attachmentReasons, activeNotes }
}

/** Whether a consequent is already satisfied by what is chosen — no need to flag it as required. */
function isSatisfiedByCurrent(
  consequent: CatalogDesignOperand,
  selections: DesignPickerSelections,
): boolean {
  return operandHolds(consequent, selections)
}
