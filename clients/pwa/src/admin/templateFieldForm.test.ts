import { describe, expect, it } from 'vitest'
import { aTemplateField } from './testing/fixtures'
import {
  blankField,
  fieldToForm,
  hasPrecision,
  isChoice,
  toRequest,
  validateField,
} from './templateFieldForm'
import type { FieldFormState } from './templateFieldForm'

/** A form that would be accepted, so a test can change exactly one thing about it. */
function good(overrides: Partial<FieldFormState> = {}): FieldFormState {
  return {
    ...blankField('Bodice'),
    key: 'shoulder_width',
    label: 'Shoulder width',
    helpText: 'Across the back, shoulder point to shoulder point.',
    ...overrides,
  }
}

function messages(form: FieldFormState, keys: readonly string[] = [], isNew = true): string[] {
  return validateField(form, keys, isNew).map((error) => error.messageId)
}

describe('what the editor refuses before the server is asked', () => {
  it('accepts a field that satisfies every rule', () => {
    expect(validateField(good(), [], true)).toEqual([])
  })

  it('reports every problem at once rather than the first', () => {
    // A person who fixes one refusal and is then told about the next has been made to submit four
    // times to learn four things, which is the failure 3.3.1 is about.
    const found = messages(good({ key: '', label: '', helpText: '', groupName: '' }))

    expect(found).toContain('admin.field.error.keyRequired')
    expect(found).toContain('admin.field.error.labelRequired')
    expect(found).toContain('admin.field.error.helpRequired')
    expect(found).toContain('admin.field.error.groupRequired')
  })

  it.each([
    ['Shoulder_Width', 'a capital letter'],
    ['2nd_shoulder', 'a leading digit'],
    ['shoulder-width', 'a hyphen'],
    ['s', 'one character'],
    ['a'.repeat(61), 'sixty-one characters'],
  ])('refuses %s as a key — %s', (key) => {
    expect(messages(good({ key }))).toContain('admin.field.error.keyMalformed')
  })

  it('refuses a key another field in this version already uses', () => {
    expect(messages(good({ key: 'chest_bust' }), ['chest_bust'])).toContain(
      'admin.field.error.keyTaken',
    )
  })

  it('does not check the key of an existing field, because the server ignores it', () => {
    // `TemplateVersion.cs:276` drops the key on a PUT. Refusing a duplicate here would refuse the
    // field its own key, which is the one value it is guaranteed to carry.
    expect(messages(good({ key: 'chest_bust' }), ['chest_bust'], false)).toEqual([])
  })

  it('needs a length to be enterable in some unit', () => {
    // `PrecisionMissing`: no inch step and no decimal place is a field with no unit at all.
    expect(messages(good({ inchFraction: 0, centimetreDecimals: 0 }))).toContain(
      'admin.field.error.precisionMissing',
    )
  })

  it.each([2, 4, 8, 16])(
    'accepts %s as an inch step, because a tape is divided by halving',
    (denominator) => {
      expect(messages(good({ inchFraction: denominator }))).toEqual([])
    },
  )

  it.each([3, 10, 32])(
    'refuses %s as an inch step — there is no marking to read against',
    (denominator) => {
      expect(messages(good({ inchFraction: denominator }))).toContain(
        'admin.field.error.fractionNotPermitted',
      )
    },
  )

  it('does not ask a count or a choice for a precision it can never use', () => {
    expect(
      messages(good({ canonicalUnit: 'Count', inchFraction: 0, centimetreDecimals: 0 })),
    ).toEqual([])
  })

  it('needs a choice field to offer at least one choice', () => {
    // `ChoiceFieldHasNoOptions`. A choice field with no options cannot be saved at all, so the
    // editor says so rather than letting the server refuse a form the person has just filled in.
    expect(messages(good({ canonicalUnit: 'None', options: [] }))).toEqual([
      'admin.field.error.optionsRequired',
    ])
  })

  it.each([
    ['_LEADING', 'a leading underscore'],
    ['HAS SPACE', 'a space'],
    ['HAS-HYPHEN', 'a hyphen'],
    ['A'.repeat(41), 'forty-one characters'],
  ])('refuses %s as a stored value — %s', (code) => {
    const found = messages(
      good({ canonicalUnit: 'None', options: [{ code, label: 'Something', labelTamil: '' }] }),
    )
    expect(found).toContain('admin.field.error.optionCode')
  })

  it('accepts a code in lower case, because the editor upper-cases it as it is typed', () => {
    // The control normalises visibly rather than on the way out: the stored value appears in every
    // export and integration, so what the box shows and what is created have to be the same string.
    expect(
      messages(
        good({
          canonicalUnit: 'None',
          options: [{ code: 'round', label: 'Round', labelTamil: '' }],
        }),
      ),
    ).toEqual([])
  })

  it('refuses two choices that share a stored value', () => {
    const found = validateField(
      good({
        canonicalUnit: 'None',
        options: [
          { code: 'ROUND', label: 'Round', labelTamil: '' },
          { code: 'round', label: 'Round again', labelTamil: '' },
        ],
      }),
      [],
      true,
    )

    // Codes are upper-cased on the way out, so two that differ only in case are one code.
    expect(found.map((error) => error.messageId)).toContain('admin.field.error.optionDuplicate')
    expect(
      found.find((error) => error.messageId === 'admin.field.error.optionDuplicate')?.optionIndex,
    ).toBe(1)
  })

  it('needs a diagram to be described, and only once there is a diagram', () => {
    // `DiagramAltMissing`. A picture nobody can describe is a picture a screen-reader user is
    // simply not given.
    expect(messages(good({ diagramKey: 'blouse_front_v1', diagramAlt: '' }))).toContain(
      'admin.field.error.altRequired',
    )
    expect(messages(good({ diagramKey: '', diagramAlt: '' }))).toEqual([])
  })

  it('says which limit was passed, so the sentence can name it', () => {
    const found = validateField(good({ label: 'x'.repeat(121) }), [], true)
    expect(found[0]?.messageId).toBe('admin.field.error.tooLong')
    expect(found[0]?.values).toEqual({ limit: 120 })
  })
})

describe('which controls a unit admits', () => {
  it('gives a length a precision and a choice a list, and never both', () => {
    expect(hasPrecision('Millimetre')).toBe(true)
    expect(hasPrecision('Count')).toBe(false)
    expect(hasPrecision('None')).toBe(false)
    expect(isChoice('None')).toBe(true)
    expect(isChoice('Millimetre')).toBe(false)
  })
})

describe('the nineteen members that go out', () => {
  it('sends every member the schema requires', () => {
    // The request type is pinned against the generated schema in `api/contract.ts`; this asserts
    // the object actually built carries all of them, which the type alone cannot.
    expect(Object.keys(toRequest(good(), null, 0)).sort()).toEqual(
      [
        'canonicalUnit',
        'centimetreDecimals',
        'diagramAlt',
        'diagramKey',
        'diagramMediaId',
        'displayOrder',
        'groupName',
        'helpText',
        'inchFraction',
        'isRequired',
        'key',
        'label',
        'labelTamil',
        'maximumMillimetres',
        'minimumMillimetres',
        'options',
        'rule',
        'warnAboveMillimetres',
        'warnBelowMillimetres',
      ].sort(),
    )
  })

  it('sends the no-bounds sentinel for a new field, and never a threshold beside it', () => {
    // `ValidationBands.None` is exactly 0 / 0 / null / null. The two hard bounds are not nullable,
    // so that quad is the only way to say "no bounds", and a warning threshold sent beside it is
    // refused as outside them.
    const request = toRequest(good(), null, 0)

    expect(request.minimumMillimetres).toBe(0)
    expect(request.maximumMillimetres).toBe(0)
    expect(request.warnBelowMillimetres).toBeNull()
    expect(request.warnAboveMillimetres).toBeNull()
  })

  it('leaves the bands of an existing field exactly as they were', () => {
    // The bands are #103's screen. Saving a label here must not reset a bound somebody set there.
    const existing = aTemplateField()
    const request = toRequest(fieldToForm(existing), existing, 0)

    expect(request.minimumMillimetres).toBe(100)
    expect(request.maximumMillimetres).toBe(2000)
    expect(request.warnBelowMillimetres).toBe(200)
    expect(request.warnAboveMillimetres).toBe(1800)
  })

  it('sends a whole precision for a count, because the server will not correct it', () => {
    // `PrecisionNotAllowedForUnit` refuses a count carrying an inch step, so the editor zeroes the
    // pair itself rather than sending whatever the length controls last held.
    const request = toRequest(
      good({ canonicalUnit: 'Count', inchFraction: 8, centimetreDecimals: 1 }),
      null,
      0,
    )

    expect(request.inchFraction).toBe(0)
    expect(request.centimetreDecimals).toBe(0)
  })

  it('sends a choice field no bands and no precision, and its options in order', () => {
    // Unlike the bands, which the API nulls out itself, a choice field sent `inchFraction: 8` is
    // accepted and stored, and then reads back with a step it can never use.
    const request = toRequest(
      good({
        canonicalUnit: 'None',
        inchFraction: 16,
        centimetreDecimals: 2,
        options: [
          { code: 'round', label: 'Round', labelTamil: 'வட்டம்' },
          { code: 'SQUARE', label: 'Square', labelTamil: '' },
        ],
      }),
      null,
      0,
    )

    expect(request.inchFraction).toBe(0)
    expect(request.centimetreDecimals).toBe(0)
    expect(request.minimumMillimetres).toBe(0)
    expect(request.warnBelowMillimetres).toBeNull()
    expect(request.options).toEqual([
      { code: 'ROUND', label: 'Round', labelTamil: 'வட்டம்', displayOrder: 0 },
      { code: 'SQUARE', label: 'Square', labelTamil: null, displayOrder: 1 },
    ])
  })

  it('sends no options at all on a numeric field', () => {
    // `NumericFieldHasOptions` refuses one that carries them.
    expect(toRequest(good({ canonicalUnit: 'Millimetre' }), null, 0).options).toBeNull()
  })

  it('clears the description when the diagram it described is removed', () => {
    const request = toRequest(
      good({ diagramKey: '  ', diagramAlt: 'A picture that is gone.' }),
      null,
      0,
    )

    expect(request.diagramKey).toBeNull()
    expect(request.diagramAlt).toBeNull()
  })

  it('preserves the rule and the uploaded diagram this screen cannot edit', () => {
    // The rule builder is #95 and the media library is #31. Neither exists, and dropping what is
    // already on the field would silently unsay it.
    const existing = aTemplateField({
      diagramMediaId: '0199bb00-0000-7000-8000-0000000000c1',
      ruleDefinition: {
        effect: 'ShownWhen',
        anyOf: [{ scope: 'Field', name: 'has_lining', operator: 'IsAnyOf', values: ['YES'] }],
      },
    })
    const request = toRequest(fieldToForm(existing), existing, 0)

    expect(request.diagramMediaId).toBe('0199bb00-0000-7000-8000-0000000000c1')
    expect(request.rule).toEqual(existing.ruleDefinition)
  })

  it('reads the new half of each superseded pair, never the rendered sentence', () => {
    // `rule` on the response is an English sentence and is not sendable; `optionCodes` carries the
    // codes without their labels, so echoing it would erase every label on the first save.
    const existing = aTemplateField({
      canonicalUnit: 'None',
      rule: 'Shown when the lining is chosen.',
      ruleDefinition: null,
      optionCodes: ['ROUND', 'SQUARE'],
      options: [
        { code: 'ROUND', label: 'Round', labelTamil: null, displayOrder: 0 },
        { code: 'SQUARE', label: 'Square', labelTamil: null, displayOrder: 1 },
      ],
    })
    const request = toRequest(fieldToForm(existing), existing, 0)

    expect(request.rule).toBeNull()
    expect(request.options).toEqual([
      { code: 'ROUND', label: 'Round', labelTamil: null, displayOrder: 0 },
      { code: 'SQUARE', label: 'Square', labelTamil: null, displayOrder: 1 },
    ])
  })

  it('trims what a person typed, so a trailing space is not part of a key', () => {
    const request = toRequest(good({ key: ' shoulder_width ', label: ' Shoulder width ' }), null, 3)

    expect(request.key).toBe('shoulder_width')
    expect(request.label).toBe('Shoulder width')
    expect(request.displayOrder).toBe(3)
  })

  it('turns an empty optional into null rather than an empty string', () => {
    expect(toRequest(good({ labelTamil: '   ' }), null, 0).labelTamil).toBeNull()
  })
})

describe('starting from a field that exists', () => {
  it('opens on what is stored, so an edit begins where the field is', () => {
    const form = fieldToForm(aTemplateField())

    expect(form.key).toBe('chest_bust')
    expect(form.canonicalUnit).toBe('Millimetre')
    expect(form.inchFraction).toBe(8)
    expect(form.diagramKey).toBe('blouse_front_v1')
  })

  it('falls back to a length for a unit this build has never heard of', () => {
    // A server that grows a fourth unit must not render a blank select on an old client.
    expect(fieldToForm(aTemplateField({ canonicalUnit: 'Furlong' })).canonicalUnit).toBe(
      'Millimetre',
    )
  })

  it('inherits the group of the field before it, so a step is typed once', () => {
    expect(blankField('Bodice').groupName).toBe('Bodice')
    expect(blankField().groupName).toBe('')
  })
})
