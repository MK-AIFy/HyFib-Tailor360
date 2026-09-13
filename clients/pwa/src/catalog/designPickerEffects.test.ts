import { expect, it } from 'vitest'
import { evaluateDesignPickerEffects, operandHolds } from './designPickerEffects'
import {
  aDesignPickerGroup,
  aDesignPickerOption,
  aDesignPickerRule,
  anOperand,
} from './testing/fixtures'

const NECKLINE = aDesignPickerGroup({
  code: 'neckline',
  options: [aDesignPickerOption({ code: 'ROUND' }), aDesignPickerOption({ code: 'V_NECK' })],
})
const SLEEVE = aDesignPickerGroup({
  designOptionGroupId: 'group-sleeve',
  code: 'sleeve',
  options: [
    aDesignPickerOption({ code: 'SHORT' }),
    aDesignPickerOption({ code: 'PUFF' }),
    aDesignPickerOption({ code: 'SLEEVELESS' }),
  ],
})
const GROUPS = [NECKLINE, SLEEVE]

/**
 * The single-antecedent test this module runs ahead of any `…/check` round trip (#142). Every case
 * here mirrors a line of `DesignRuleEngine.SatisfiedBy`/`Holds` — the client copy has to agree with
 * the server's on what an operand means, or a card would read as offered when the next save refuses
 * it.
 */

it('reads Always as holding unconditionally, even over an empty selection set', () => {
  expect(
    operandHolds(anOperand({ form: 'Always', groupCode: null, optionCodes: [] }), new Map()),
  ).toBe(true)
})

it('reads every form but Always as false over an unset group — negations included', () => {
  const empty = new Map()
  expect(operandHolds(anOperand({ form: 'Equals', optionCodes: ['ROUND'] }), empty)).toBe(false)
  expect(operandHolds(anOperand({ form: 'NotEquals', optionCodes: ['ROUND'] }), empty)).toBe(false)
  expect(operandHolds(anOperand({ form: 'AnySelection' }), empty)).toBe(false)
  expect(operandHolds(anOperand({ form: 'Excludes', optionCodes: ['ROUND'] }), empty)).toBe(false)
})

it('reads Equals, NotEquals and In against a single-selection group', () => {
  const chosenRound = new Map([['neckline', ['ROUND']]])
  expect(operandHolds(anOperand({ form: 'Equals', optionCodes: ['ROUND'] }), chosenRound)).toBe(
    true,
  )
  expect(operandHolds(anOperand({ form: 'Equals', optionCodes: ['V_NECK'] }), chosenRound)).toBe(
    false,
  )
  expect(operandHolds(anOperand({ form: 'NotEquals', optionCodes: ['V_NECK'] }), chosenRound)).toBe(
    true,
  )
  expect(
    operandHolds(anOperand({ form: 'In', optionCodes: ['ROUND', 'V_NECK'] }), chosenRound),
  ).toBe(true)
})

it('reads Includes and Excludes against a multiple-selection group', () => {
  const chosenTwo = new Map([['trims', ['LACE', 'PIPING']]])
  expect(
    operandHolds(
      anOperand({ groupCode: 'trims', form: 'Includes', optionCodes: ['LACE'] }),
      chosenTwo,
    ),
  ).toBe(true)
  expect(
    operandHolds(
      anOperand({ groupCode: 'trims', form: 'Excludes', optionCodes: ['ZARI'] }),
      chosenTwo,
    ),
  ).toBe(true)
  expect(
    operandHolds(
      anOperand({ groupCode: 'trims', form: 'Excludes', optionCodes: ['LACE'] }),
      chosenTwo,
    ),
  ).toBe(false)
})

it('greys out only the option an excludes rule names, once its antecedent holds', () => {
  const rule = aDesignPickerRule({
    type: 'Excludes',
    antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['ROUND'] }),
    consequent: anOperand({ groupCode: 'sleeve', form: 'Equals', optionCodes: ['PUFF'] }),
  })

  const before = evaluateDesignPickerEffects(GROUPS, [rule], new Map())
  expect(before.disabledOptions.size).toBe(0)

  const after = evaluateDesignPickerEffects(GROUPS, [rule], new Map([['neckline', ['ROUND']]]))
  expect([...after.disabledOptions.keys()]).toEqual(['sleeve.PUFF'])
})

it('expands a NotEquals/Excludes consequent to the options it satisfies, not the named one', () => {
  // Mirrors the seeded gown rule DR-25: a halter neckline excludes `sleeve_style != SLEEVELESS` —
  // every sleeve but SLEEVELESS should grey out, and SLEEVELESS itself, the one thing the rule
  // actually permits, must stay enabled.
  const rule = aDesignPickerRule({
    identifier: 'DR-25',
    type: 'Excludes',
    antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['V_NECK'] }),
    consequent: anOperand({ groupCode: 'sleeve', form: 'NotEquals', optionCodes: ['SLEEVELESS'] }),
  })

  const result = evaluateDesignPickerEffects(GROUPS, [rule], new Map([['neckline', ['V_NECK']]]))

  expect([...result.disabledOptions.keys()].sort()).toEqual(['sleeve.PUFF', 'sleeve.SHORT'])
  expect(result.disabledOptions.has('sleeve.SLEEVELESS')).toBe(false)
})

it('flags a required group only while its requires rule is not yet satisfied', () => {
  const rule = aDesignPickerRule({
    type: 'Requires',
    antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['ROUND'] }),
    consequent: anOperand({ groupCode: 'sleeve', form: 'Equals', optionCodes: ['SHORT'] }),
  })

  const unsatisfied = evaluateDesignPickerEffects(
    GROUPS,
    [rule],
    new Map([['neckline', ['ROUND']]]),
  )
  expect(unsatisfied.requiredGroups.has('sleeve')).toBe(true)

  const satisfied = evaluateDesignPickerEffects(
    GROUPS,
    [rule],
    new Map([
      ['neckline', ['ROUND']],
      ['sleeve', ['SHORT']],
    ]),
  )
  expect(satisfied.requiredGroups.has('sleeve')).toBe(false)
})

it('collects a requires-attachment rule and a note rule only while their antecedent holds', () => {
  const attachment = aDesignPickerRule({
    identifier: 'DR-08',
    type: 'RequiresAttachment',
    antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['ROUND'] }),
    consequent: null,
  })
  const note = aDesignPickerRule({
    identifier: 'DR-30',
    type: 'Note',
    antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['V_NECK'] }),
    consequent: null,
    note: 'Confirm the depth with the customer before cutting.',
  })

  const result = evaluateDesignPickerEffects(
    GROUPS,
    [attachment, note],
    new Map([['neckline', ['ROUND']]]),
  )
  expect(result.attachmentReasons.map((reason) => reason.ruleIdentifier)).toEqual(['DR-08'])
  expect(result.activeNotes).toHaveLength(0)
})
