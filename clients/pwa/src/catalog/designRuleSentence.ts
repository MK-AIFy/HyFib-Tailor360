import type { CatalogDesignGroup } from './types'

/**
 * The rule's sentence, composed on the client from the draft rather than read from the server
 * (#141).
 *
 * `DesignRulePayload.statement` exists only for a rule the server already holds — there is nothing
 * to ask it for while somebody is still composing one. This mirrors the same operand grammar
 * `DesignRuleOperand`'s own remarks document (`docs/prd/design-options.md` section 4), in the shop's
 * words rather than the enumeration's, so the editor can show the sentence back before the rule is
 * ever sent. It is a preview, not a second opinion: the server's own validators are still what a
 * publication check answers from, and a name this cannot resolve — a retired option, a code from
 * another category — falls back to the bare code rather than guessing.
 */

/** One side of a rule, as the editor holds it in a draft. */
export interface DesignOperandDraft {
  readonly groupCode: string | null
  readonly form: string
  readonly optionCodes: readonly string[]
}

/** A rule as the editor holds it, before it is sent. */
export interface DesignRuleDraft {
  readonly type: string
  readonly antecedent: DesignOperandDraft
  readonly consequent: DesignOperandDraft | null
  readonly note: string
}

function groupName(groups: readonly CatalogDesignGroup[], groupCode: string | null): string {
  if (groupCode === null) {
    return ''
  }
  return groups.find((group) => group.code === groupCode)?.name ?? groupCode
}

function optionNames(
  groups: readonly CatalogDesignGroup[],
  groupCode: string | null,
  codes: readonly string[],
): string {
  const group = groups.find((candidate) => candidate.code === groupCode)
  return codes
    .map((code) => group?.options.find((option) => option.code === code)?.name ?? code)
    .join(', ')
}

function operandSentence(
  groups: readonly CatalogDesignGroup[],
  operand: DesignOperandDraft,
): string {
  const group = groupName(groups, operand.groupCode)
  const options = optionNames(groups, operand.groupCode, operand.optionCodes)

  switch (operand.form) {
    case 'Always':
      return 'the customer reaches this rule'
    case 'AnySelection':
      return `${group} is set`
    case 'Equals':
      return `${group} is ${options}`
    case 'NotEquals':
      return `${group} is not ${options}`
    case 'In':
      return `${group} is one of ${options}`
    case 'Includes':
      return `${group} includes ${options}`
    case 'Excludes':
      return `${group} excludes ${options}`
    default:
      return `${group} (an unrecognised form)`
  }
}

/** The rule's sentence, in the shop's words. */
export function describeDesignRuleDraft(
  groups: readonly CatalogDesignGroup[],
  draft: DesignRuleDraft,
): string {
  const condition = operandSentence(groups, draft.antecedent)
  const consequent = draft.consequent === null ? null : operandSentence(groups, draft.consequent)

  switch (draft.type) {
    case 'Requires':
      return consequent === null
        ? `If ${condition}, something is required.`
        : `If ${condition}, then ${consequent} is required.`
    case 'Excludes':
      return consequent === null
        ? `If ${condition}, something is excluded.`
        : `If ${condition}, then ${consequent} is excluded.`
    case 'RequiresAttachment':
      return `If ${condition}, a reference photo is required.`
    case 'Note':
      return draft.note.trim() === ''
        ? `If ${condition}, a note is shown at the counter.`
        : `If ${condition}: ${draft.note.trim()}`
    default:
      return `If ${condition} (an unrecognised rule type).`
  }
}
