import { describe, expect, it } from 'vitest'
import { findingsForField, isError, parseTarget, unanchoredFindings } from './templateFindings'
import type { TemplateFinding } from './types'

function finding(overrides: Partial<TemplateFinding> = {}): TemplateFinding {
  return {
    severity: 'Error',
    code: 'measurements.field-precision-missing',
    message: 'This field has no unit it can be entered in.',
    target: 'fields[chest_bust].precision',
    ...overrides,
  }
}

describe('reading where a finding belongs', () => {
  it.each([
    ['fields[chest_bust].precision', 'chest_bust', 'precision'],
    ['fields[chest_bust].bands', 'chest_bust', 'bands'],
    ['fields[sleeve_length].rule', 'sleeve_length', 'rule'],
    ['fields[a].key', 'a', 'key'],
    ['fields[a].labelTamil', 'a', 'labelTamil'],
    ['fields[a].options', 'a', 'options'],
    ['fields[a].diagramKey', 'a', 'diagramKey'],
  ])('reads %s', (target, fieldKey, attribute) => {
    expect(parseTarget(target)).toEqual({ fieldKey, attribute })
  })

  it('reads a finding about the version as a whole', () => {
    // The one the server reports when a version has no fields at all.
    expect(parseTarget('fields')).toEqual({ fieldKey: null, attribute: null })
  })

  it('reads a field whose attribute this build has never heard of', () => {
    // A server that grows an eighth attribute must still anchor to the field, so the finding lands
    // beside the right row even when the screen cannot say which control within it.
    expect(parseTarget('fields[chest_bust].newThing')).toEqual({
      fieldKey: 'chest_bust',
      attribute: null,
    })
  })

  it('gives up honestly on a shape it cannot read at all', () => {
    expect(parseTarget('something.else')).toEqual({ fieldKey: null, attribute: null })
  })
})

describe('which findings block and which do not', () => {
  it('distinguishes an error from a warning', () => {
    expect(isError(finding())).toBe(true)
    expect(isError(finding({ severity: 'Warning' }))).toBe(false)
  })
})

describe('putting a finding beside the control that caused it', () => {
  it('collects the findings about one field, in the order the server reported them', () => {
    const all = [
      finding(),
      finding({ target: 'fields[sleeve_length].rule' }),
      finding({ target: 'fields[chest_bust].bands', code: 'measurements.bounds-out-of-order' }),
    ]

    expect(findingsForField(all, 'chest_bust').map((one) => one.code)).toEqual([
      'measurements.field-precision-missing',
      'measurements.bounds-out-of-order',
    ])
  })

  it('has nothing for a field nothing was reported about', () => {
    expect(findingsForField([finding()], 'waist')).toEqual([])
  })
})

describe('the findings a screen cannot anchor', () => {
  it('carries a version-level finding, which has no control to sit beside', () => {
    const orphan = finding({ target: 'fields', code: 'measurements.version-has-no-fields' })
    expect(unanchoredFindings([orphan], ['chest_bust'])).toEqual([orphan])
  })

  it('carries a finding about a field this screen is not showing', () => {
    // The version changed under the reader: the report was computed against a version that had the
    // field, and the one on screen does not. Dropping it would tell an administrator their version
    // is ready when the server will refuse to publish it.
    const stale = finding({ target: 'fields[gone_away].precision' })
    expect(unanchoredFindings([stale], ['chest_bust'])).toEqual([stale])
  })

  it('leaves a finding that can be anchored to the field row', () => {
    expect(unanchoredFindings([finding()], ['chest_bust'])).toEqual([])
  })
})
