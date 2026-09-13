import type { DesignOperandDraft } from './designRuleSentence'

/** A rule as the editor holds it, before it is sent (#141). */
export interface DesignRuleFormDraft {
  readonly type: string
  readonly antecedent: DesignOperandDraft
  readonly hasConsequent: boolean
  readonly consequent: DesignOperandDraft
  readonly note: string
  readonly why: string
  readonly reason: string
}

function blankOperand(): DesignOperandDraft {
  return { groupCode: null, form: 'Always', optionCodes: [] }
}

/** A blank one. */
export function blankDesignRuleDraft(): DesignRuleFormDraft {
  return {
    type: 'Requires',
    antecedent: blankOperand(),
    hasConsequent: true,
    consequent: blankOperand(),
    note: '',
    why: '',
    reason: '',
  }
}
