import { describe, expect, it } from 'vitest'
import {
  fieldControlAttributes,
  fieldDescribedBy,
  fieldElementIds,
  fieldErrorsFromProblemDetails,
  isFieldInvalid,
} from './FieldProps'
import type { FieldProps } from './FieldProps'

const ids = fieldElementIds('waist')

function field(overrides: Partial<FieldProps> = {}): FieldProps {
  return { name: 'waist', label: 'Waist', ...overrides }
}

describe('fieldElementIds', () => {
  it('derives every part from one base, so the wiring is computed rather than typed', () => {
    expect(ids).toEqual({
      control: 'waist',
      label: 'waist-label',
      description: 'waist-description',
      error: 'waist-error',
      warning: 'waist-warning',
      unit: 'waist-unit',
    })
  })
})

describe('fieldDescribedBy', () => {
  it('describes nothing when there is nothing to describe', () => {
    expect(fieldDescribedBy(field(), ids)).toBeUndefined()
  })

  it('reads the error first, because a failed submit is what moved focus here', () => {
    const described = fieldDescribedBy(
      field({
        error: 'Waist must be between 45.0 cm and 150.0 cm',
        warning: 'That is unusually large for this template',
        description: 'Measured at the natural waist',
        unit: { symbol: 'cm', label: 'centimetres' },
      }),
      ids,
    )

    expect(described).toBe('waist-error waist-warning waist-unit waist-description')
  })

  it('appends ids from outside the field after its own', () => {
    expect(fieldDescribedBy(field({ describedByIds: ['step-2-note'] }), ids)).toBe('step-2-note')
  })
})

describe('fieldControlAttributes', () => {
  it('carries only the attributes that are true, never aria-invalid="false"', () => {
    expect(fieldControlAttributes(field(), ids)).toEqual({ id: 'waist', name: 'waist' })
  })

  it('marks a required field with aria-required and not with the native attribute', () => {
    const attributes = fieldControlAttributes(field({ required: true }), ids)

    expect(attributes['aria-required']).toBe(true)
    // The native `required` attribute would raise a browser bubble that competes with the
    // FormErrorSummary and cannot be translated.
    expect(attributes).not.toHaveProperty('required')
  })

  it('marks an invalid field and points it at its message', () => {
    const attributes = fieldControlAttributes(field({ error: 'Required' }), ids)

    expect(attributes['aria-invalid']).toBe(true)
    expect(attributes['aria-describedby']).toBe('waist-error')
  })

  it('does not make a warning invalid, because a warning never blocks a submit', () => {
    const attributes = fieldControlAttributes(field({ warning: 'Unusually large' }), ids)

    expect(attributes).not.toHaveProperty('aria-invalid')
    expect(attributes['aria-describedby']).toBe('waist-warning')
  })

  it('passes the keyboard and autofill hints through', () => {
    const attributes = fieldControlAttributes(
      { name: 'phone', inputMode: 'tel', autoComplete: 'tel' },
      fieldElementIds('phone'),
    )

    expect(attributes.inputMode).toBe('tel')
    expect(attributes.autoComplete).toBe('tel')
  })
})

describe('isFieldInvalid', () => {
  it('is true only when there is an error to read', () => {
    expect(isFieldInvalid({})).toBe(false)
    expect(isFieldInvalid({ error: '' })).toBe(false)
    expect(isFieldInvalid({ error: 'Required' })).toBe(true)
  })
})

describe('fieldErrorsFromProblemDetails', () => {
  const resolve = (name: string): string | undefined =>
    name === 'waist' ? 'waist-control' : undefined

  it('maps every server message onto a summary entry', () => {
    const entries = fieldErrorsFromProblemDetails(
      { errors: { waist: ['Required', 'Out of range'] } },
      resolve,
    )

    expect(entries).toEqual([
      { name: 'waist', message: 'Required', controlId: 'waist-control' },
      { name: 'waist', message: 'Out of range', controlId: 'waist-control' },
    ])
  })

  it('drops a field the screen does not have, rather than offering a link to nowhere', () => {
    expect(
      fieldErrorsFromProblemDetails({ errors: { somethingElse: ['Required'] } }, resolve),
    ).toEqual([])
  })

  it('handles a problem-details response that carries no field errors at all', () => {
    expect(fieldErrorsFromProblemDetails({ title: 'Conflict', status: 409 }, resolve)).toEqual([])
  })
})
