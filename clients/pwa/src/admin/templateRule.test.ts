import { describe, expect, it } from 'vitest'
import {
  MAXIMUM_CLAUSES,
  blankClause,
  blankRule,
  draftToRule,
  evaluateRule,
  ruleToDraft,
  validateRule,
} from './templateRule'
import type { RuleClauseDraft, RuleDraft } from './templateRule'

function clause(overrides: Partial<RuleClauseDraft> = {}): RuleClauseDraft {
  return { scope: 'Field', name: 'has_lining', operator: 'IsAnyOf', values: ['YES'], ...overrides }
}

function rule(overrides: Partial<RuleDraft> = {}): RuleDraft {
  return { effect: 'ShownWhen', anyOf: [clause()], ...overrides }
}

const codes = (draft: RuleDraft | null, ownKey = 'lining_length') =>
  validateRule(draft, ownKey).map((error) => error.messageId)

describe('building a rule', () => {
  it('sends the structure, never the sentence the response renders', () => {
    // `rule` on the response is a rendered English sentence and is not sendable; `ruleDefinition`
    // is the structure. Sending the sentence back would be a rule the server cannot parse.
    expect(draftToRule(rule())).toEqual({
      effect: 'ShownWhen',
      anyOf: [{ scope: 'Field', name: 'has_lining', operator: 'IsAnyOf', values: ['YES'] }],
    })
  })

  it('drops a value nobody has typed yet, rather than sending an empty one', () => {
    // `ClauseHasNoValues` refuses a clause with none; a half-typed row is a person mid-edit.
    const sent = draftToRule(rule({ anyOf: [clause({ values: ['YES', '  ', ''] })] }))
    expect(sent?.anyOf[0]?.values).toEqual(['YES'])
  })

  it('opens a stored rule as a rule, and a field without one as nothing at all', () => {
    expect(
      ruleToDraft({
        effect: 'HiddenWhen',
        anyOf: [
          {
            scope: 'DesignSelection',
            name: 'sleeve_style',
            operator: 'IsAnyOf',
            values: ['SLEEVELESS'],
          },
        ],
      }),
    ).toEqual({
      effect: 'HiddenWhen',
      anyOf: [
        {
          scope: 'DesignSelection',
          name: 'sleeve_style',
          operator: 'IsAnyOf',
          values: ['SLEEVELESS'],
        },
      ],
    })

    expect(ruleToDraft(null)).toBeNull()
    expect(draftToRule(null)).toBeNull()
  })

  it('falls back rather than rendering a blank for a word this build does not know', () => {
    // A server that grows a third effect or a third operator must not leave a select empty on an
    // old client, which would look like a rule nobody had finished.
    const opened = ruleToDraft({
      effect: 'SometimesWhen',
      anyOf: [{ scope: 'Weather', name: 'x', operator: 'Rhymes', values: ['A'] }],
    })

    expect(opened?.effect).toBe('ShownWhen')
    expect(opened?.anyOf[0]?.scope).toBe('Field')
    expect(opened?.anyOf[0]?.operator).toBe('IsAnyOf')
  })

  it('starts a new rule with one clause, because a rule with none can never ask for the field', () => {
    expect(blankRule().anyOf).toHaveLength(1)
    expect(blankClause().values).toEqual([''])
  })
})

describe('what the builder refuses before the server is asked', () => {
  it('accepts a well-formed rule', () => {
    expect(codes(rule())).toEqual([])
  })

  it('refuses a rule with no conditions, which can never ask for the field', () => {
    // `HidesUnconditionally`: with nothing to match, HiddenWhen always hides and ShownWhen never
    // shows. Either way the field can never be captured.
    expect(codes(rule({ anyOf: [] }))).toEqual(['admin.field.rule.error.noClauses'])
  })

  it('refuses more than six conditions', () => {
    const many = rule({ anyOf: Array.from({ length: MAXIMUM_CLAUSES + 1 }, () => clause()) })
    expect(codes(many)).toContain('admin.field.rule.error.tooManyClauses')
  })

  it('accepts exactly six, which is the bound rather than one below it', () => {
    const six = rule({ anyOf: Array.from({ length: MAXIMUM_CLAUSES }, () => clause()) })
    expect(codes(six)).toEqual([])
  })

  it('refuses a field that reads its own answer, with the sentence that explains why', () => {
    // A field whose visibility depends on its own value can never settle: it is hidden, so it has
    // no value, so it is shown, so it has one. `RuleReadsItself` — and the domain catches it
    // separately from the cycle check because a one-node cycle reads as a typo.
    expect(codes(rule({ anyOf: [clause({ name: 'lining_length' })] }))).toContain(
      'admin.field.rule.error.readsItself',
    )
  })

  it('refuses a field operand that is not a well-formed key', () => {
    expect(codes(rule({ anyOf: [clause({ name: 'Has Lining' })] }))).toContain(
      'admin.field.rule.error.operandMalformed',
    )
  })

  it('does not hold a design operand to the field-key pattern', () => {
    // A design option-group code is a different namespace with different rules, and there is no
    // catalogue of them to check against.
    expect(
      codes(rule({ anyOf: [clause({ scope: 'DesignSelection', name: 'sleeve_style' })] })),
    ).toEqual([])
  })

  it('needs every condition to compare against something', () => {
    expect(codes(rule({ anyOf: [clause({ values: [''] })] }))).toContain(
      'admin.field.rule.error.noValues',
    )
  })

  it('says which condition each refusal is about, so it can be shown beside it', () => {
    const found = validateRule(rule({ anyOf: [clause(), clause({ name: '' })] }), 'lining_length')

    expect(found).toHaveLength(1)
    expect(found[0]?.clause).toBe(1)
  })

  it('has nothing to say about a field with no rule', () => {
    expect(codes(null)).toEqual([])
  })
})

describe('what a rule decides', () => {
  const shown = (draft: RuleDraft | null, values = {}, selections = {}) =>
    evaluateRule(draft, values, selections)

  it('shows a field that carries no rule at all', () => {
    expect(shown(null)).toEqual({ isShown: true, decided: true })
  })

  it('shows when a ShownWhen rule matches and hides when it does not', () => {
    expect(shown(rule(), { has_lining: 'YES' })).toEqual({ isShown: true, decided: true })
    expect(shown(rule(), { has_lining: 'NO' })).toEqual({ isShown: false, decided: true })
  })

  it('hides when a HiddenWhen rule matches and shows when it does not', () => {
    const hidden = rule({ effect: 'HiddenWhen' })
    expect(shown(hidden, { has_lining: 'YES' })).toEqual({ isShown: false, decided: true })
    expect(shown(hidden, { has_lining: 'NO' })).toEqual({ isShown: true, decided: true })
  })

  it('reads Excludes as "none of these"', () => {
    const excludes = rule({ anyOf: [clause({ operator: 'Excludes', values: ['NO'] })] })
    expect(shown(excludes, { has_lining: 'YES' }).isShown).toBe(true)
    expect(shown(excludes, { has_lining: 'NO' }).isShown).toBe(false)
  })

  it('lets one matched condition settle the whole rule, even beside one it cannot read', () => {
    // The clauses are joined by OR, so no operand still unread can turn a true disjunction false.
    // Calling this undecided would make a capture wizard treat as optional a field that a known,
    // matching condition has conclusively shown. This is the server's rule since #92.
    const two = rule({
      anyOf: [
        clause(),
        clause({ scope: 'DesignSelection', name: 'sleeve_style', values: ['FULL'] }),
      ],
    })

    expect(shown(two, { has_lining: 'YES' })).toEqual({ isShown: true, decided: true })
  })

  it('is undecided only when nothing matched and something could not be read', () => {
    const two = rule({
      anyOf: [
        clause(),
        clause({ scope: 'DesignSelection', name: 'sleeve_style', values: ['FULL'] }),
      ],
    })

    expect(shown(two, { has_lining: 'NO' })).toEqual({ isShown: true, decided: false })
  })

  it('shows the field when it cannot decide, because hiding would drop a measurement', () => {
    // Hiding on missing information drops a measurement the tailor needs; showing an unnecessary
    // one costs a skipped field. `docs/prd/measurement-templates.md` section 6.
    expect(shown(rule({ effect: 'HiddenWhen' }), {})).toEqual({ isShown: true, decided: false })
  })

  it('cannot read a design condition outside an order, rather than reading it as false', () => {
    // Design selections do not exist at the counter. Treating an absent one as false would hide a
    // field that a garment's design would have shown.
    const design = rule({
      anyOf: [clause({ scope: 'DesignSelection', name: 'sleeve_style', values: ['SLEEVELESS'] })],
    })

    expect(shown(design, {}, {})).toEqual({ isShown: true, decided: false })
    expect(shown(design, {}, { sleeve_style: ['SLEEVELESS'] })).toEqual({
      isShown: true,
      decided: true,
    })
    expect(shown(design, {}, { sleeve_style: ['FULL'] })).toEqual({
      isShown: false,
      decided: true,
    })
  })

  it('matches a design condition when any one of its values was selected', () => {
    const design = rule({
      anyOf: [clause({ scope: 'DesignSelection', name: 'sleeve_style', values: ['CAP', 'FULL'] })],
    })

    expect(shown(design, {}, { sleeve_style: ['FULL', 'PIPED'] }).isShown).toBe(true)
  })

  it('decides a rule with no conditions without reading anything', () => {
    expect(shown(rule({ anyOf: [] }))).toEqual({ isShown: true, decided: true })
    expect(shown(rule({ effect: 'HiddenWhen', anyOf: [] }))).toEqual({
      isShown: false,
      decided: true,
    })
  })
})
