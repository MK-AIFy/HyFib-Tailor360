import { describe, expect, it } from 'vitest'
import { aTemplateField } from '../admin/testing/fixtures'
import {
  captureGroups,
  enteredFor,
  findingErrorsOf,
  localErrorsOf,
  sectionRequestOf,
  stateOf,
} from './capture'
import { aMeasurementDraft, aPublishedCaptureVersion } from './testing/fixtures'

const VERSION = aPublishedCaptureVersion()
const GROUPS = captureGroups(VERSION)
const BODICE = GROUPS[0]!
const CHEST = aTemplateField()

const MESSAGES = {
  required: (label: string) => `${label} is required.`,
  outOfRange: (label: string, minimum: string, maximum: string) =>
    `${label} must be between ${minimum} and ${maximum}.`,
}

const formatBound = (_field: typeof CHEST, millimetres: number) => `${String(millimetres)} mm`

describe('enteredFor', () => {
  it('sends an inch value as the whole number plus the fraction the control showed', () => {
    // 36 1/2 in at eighths. The server converts it back and stores what it produced.
    expect(enteredFor(CHEST, 927.1, 'in')).toEqual({ entered: 36.5, unit: 'Inch' })
  })

  it('sends centimetres at the field’s own precision', () => {
    expect(enteredFor(CHEST, 927.1, 'cm')).toEqual({ entered: 92.7, unit: 'Centimetre' })
  })

  it('sends a count as the whole number it is, whatever unit is on screen', () => {
    const pleats = aTemplateField({ key: 'pleats', canonicalUnit: 'Count' })
    expect(enteredFor(pleats, 6, 'cm')).toEqual({ entered: 6, unit: 'Count' })
  })
})

describe('sectionRequestOf', () => {
  it('replaces the step with every answered, shown field and nothing else', () => {
    const request = sectionRequestOf(
      BODICE,
      {
        chest_bust: { millimetres: 927.1, acknowledged: true },
        closure: { choice: 'front_hooks', acknowledged: false },
        sleeve_length: { millimetres: 500, acknowledged: false },
      },
      'in',
    )

    expect(request.groupName).toBe('Bodice')
    expect(request.values).toEqual([
      { key: 'chest_bust', entered: 36.5, unit: 'Inch', choice: null, acknowledged: true },
      { key: 'closure', entered: null, unit: null, choice: 'front_hooks', acknowledged: false },
    ])
  })

  it('omits a field the person cleared, which is how a value is cleared on the server', () => {
    const request = sectionRequestOf(BODICE, { closure: { choice: '', acknowledged: false } }, 'in')
    expect(request.values).toEqual([])
  })

  it('drops a field a rule has hidden rather than saving a value nobody can see', () => {
    const lining = aTemplateField({
      templateFieldId: 'f-lining',
      key: 'lining_length',
      label: 'Lining length',
      groupName: 'Bodice',
      displayOrder: 2,
      ruleDefinition: {
        effect: 'ShownWhen',
        anyOf: [{ scope: 'Field', name: 'closure', operator: 'IsAnyOf', values: ['back_hooks'] }],
      },
    })
    const group = { name: 'Bodice', fields: [...BODICE.fields, lining] }

    const request = sectionRequestOf(
      group,
      {
        closure: { choice: 'front_hooks', acknowledged: false },
        lining_length: { millimetres: 300, acknowledged: false },
      },
      'cm',
    )

    expect(request.values.map((value) => value.key)).toEqual(['closure'])
  })
})

describe('localErrorsOf', () => {
  it('names a required field left empty, on the step it belongs to', () => {
    const errors = localErrorsOf(GROUPS, {}, formatBound, MESSAGES)

    expect(errors).toEqual([
      {
        name: 'chest_bust',
        message: 'Chest / bust is required.',
        controlId: 'capture-chest_bust',
        stepId: 'group:Bodice',
      },
      {
        name: 'closure',
        message: 'Closure is required.',
        controlId: 'capture-closure',
        stepId: 'group:Bodice',
      },
    ])
  })

  it('refuses a value outside the hard bounds and quotes them through the caller’s formatter', () => {
    const errors = localErrorsOf(
      GROUPS,
      {
        chest_bust: { millimetres: 3000, acknowledged: false },
        closure: { choice: 'back_hooks', acknowledged: false },
      },
      formatBound,
      MESSAGES,
    )

    expect(errors).toEqual([
      {
        name: 'chest_bust',
        message: 'Chest / bust must be between 100 mm and 2000 mm.',
        controlId: 'capture-chest_bust',
        stepId: 'group:Bodice',
      },
    ])
  })

  it('never blocks on the confirmation band: a warning is not an error', () => {
    const errors = localErrorsOf(
      GROUPS,
      {
        chest_bust: { millimetres: 150, acknowledged: false },
        closure: { choice: 'back_hooks', acknowledged: false },
      },
      formatBound,
      MESSAGES,
    )

    expect(errors).toEqual([])
  })

  it('does not demand a required field a rule has hidden', () => {
    const hidden = aTemplateField({
      templateFieldId: 'f-hidden',
      key: 'hidden_required',
      label: 'Hidden',
      groupName: 'Bodice',
      displayOrder: 3,
      isRequired: true,
      ruleDefinition: {
        effect: 'HiddenWhen',
        anyOf: [{ scope: 'Field', name: 'closure', operator: 'IsAnyOf', values: ['front_hooks'] }],
      },
    })

    const errors = localErrorsOf(
      [{ name: 'Bodice', fields: [hidden] }],
      { closure: { choice: 'front_hooks', acknowledged: false } },
      formatBound,
      MESSAGES,
    )

    expect(errors).toEqual([])
  })
})

describe('findingErrorsOf', () => {
  it('carries a server finding to its field and step, in the server’s own words', () => {
    const errors = findingErrorsOf(
      [
        {
          code: 'measurements.value-needs-acknowledgement',
          field: 'sleeve_length',
          message: 'Sleeve length is unusual and has not been checked.',
        },
      ],
      GROUPS,
    )

    expect(errors).toEqual([
      {
        name: 'sleeve_length',
        message: 'Sleeve length is unusual and has not been checked.',
        controlId: 'capture-sleeve_length',
        stepId: 'group:Sleeve',
      },
    ])
  })

  it('lists a finding about the draft as a whole under the review step rather than losing it', () => {
    const errors = findingErrorsOf(
      [{ code: 'measurements.consent-missing', field: null, message: 'No consent.' }],
      GROUPS,
    )

    expect(errors.map((entry) => entry.stepId)).toEqual(['review'])
  })

  it('drops a finding naming a field the version does not have', () => {
    const errors = findingErrorsOf(
      [{ code: 'measurements.unknown-field', field: 'ghost', message: 'Nothing.' }],
      GROUPS,
    )

    expect(errors).toEqual([])
  })
})

describe('stateOf', () => {
  it('unpacks a draft into millimetres, choices and acknowledgements by key', () => {
    const state = stateOf(
      aMeasurementDraft({
        values: [
          {
            key: 'chest_bust',
            millimetres: '927.1',
            enteredUnit: 'Inch',
            choice: null,
            acknowledged: true,
          },
          {
            key: 'closure',
            millimetres: null,
            enteredUnit: 'Inch',
            choice: 'back_hooks',
            acknowledged: false,
          },
        ],
      }),
    )

    expect(state).toEqual({
      chest_bust: { millimetres: 927.1, acknowledged: true },
      closure: { choice: 'back_hooks', acknowledged: false },
    })
  })
})
