import type { TemplateField, TemplateVersion } from '../admin/types'
import { groupFields } from '../admin/templateFieldOrder'
import type { FieldGroup } from '../admin/templateFieldOrder'
import { evaluateRule, ruleToDraft } from '../admin/templateRule'
import type { RuleEvaluation } from '../admin/templateRule'
import type {
  MeasurementBounds,
  MeasurementDisplayUnit,
} from '../design-system/components/forms/measurement'
import { evaluateMeasurement, isRejectedBand } from '../design-system/components/forms/measurement'
import type { FieldErrorEntry } from '../design-system/foundations/FieldProps'
import { millimetresToCentimetres, millimetresToInchFraction } from '../i18n/units'
import type { CentimetreDecimals, InchFractionStep } from '../i18n/units'
import type {
  MeasurementDraft,
  MeasurementFinding,
  MeasurementValueRequest,
  SaveMeasurementSectionRequest,
} from './types'

/**
 * The pure half of the capture wizard: what is shown, what is wrong, and what is sent.
 *
 * Kept out of the component so that each rule has a unit test with no DOM in it, and so that the
 * screen is left holding only what a screen must hold — focus, steps, and the retry keys.
 *
 * ## What the wizard holds per field
 *
 * Canonical millimetres for a measured field, a whole number for a count, an option code for a
 * choice, and whether an unusual value was acknowledged. Nothing here is in inches or centimetres:
 * the display unit is a way of *showing* a millimetre value, chosen once at the start, and switching
 * it converts nothing because there is nothing to convert.
 *
 * ## What is sent
 *
 * The number *as it will be read back in the unit on screen*, with the unit beside it, which is the
 * contract `MeasurementValueRequest` states: the server converts, once, and stores what it produced.
 * A millimetre value typed as `36 1/2 in` therefore goes back as `36.5 Inch`, not as `927.1`, and
 * the server's own conversion is the one that lands in the record.
 */

/** What the wizard holds for one field. Absent members mean "not answered". */
export interface CapturedFieldState {
  readonly millimetres?: number
  readonly choice?: string
  readonly acknowledged: boolean
}

export type CaptureState = Readonly<Record<string, CapturedFieldState>>

/** How a field is entered: measured with a tape, counted, or chosen from the template's options. */
export type CaptureKind = 'measured' | 'count' | 'choice'

export function captureKindOf(field: TemplateField): CaptureKind {
  if (field.canonicalUnit === 'None') {
    return 'choice'
  }
  return field.canonicalUnit === 'Count' ? 'count' : 'measured'
}

/** The id of the control for a field, which is what an error-summary link moves focus to. */
export function captureControlId(key: string): string {
  return `capture-${key}`
}

/** The id of a wizard step. Groups are named by the template; the review step is the wizard's own. */
export const REVIEW_STEP_ID = 'review'

export function stepIdOf(group: FieldGroup): string {
  return `group:${group.name}`
}

/** The template version's groups, in capture order. */
export function captureGroups(version: TemplateVersion): readonly FieldGroup[] {
  return groupFields(version.fields ?? [])
}

/** The bands of a measured field, in canonical millimetres, as `MeasurementField` takes them. */
export function boundsOf(field: TemplateField): MeasurementBounds {
  const minimum = Number(field.minimumMillimetres)
  const maximum = Number(field.maximumMillimetres)

  // `0 / 0` is the server's "any measurement" sentinel, not a field that refuses everything.
  if (minimum === 0 && maximum === 0) {
    return {}
  }

  return {
    minimumMillimetres: minimum,
    maximumMillimetres: maximum,
    ...(field.warnBelowMillimetres === null
      ? {}
      : { warnBelowMillimetres: Number(field.warnBelowMillimetres) }),
    ...(field.warnAboveMillimetres === null
      ? {}
      : { warnAboveMillimetres: Number(field.warnAboveMillimetres) }),
  }
}

export function fractionStepOf(field: TemplateField): InchFractionStep {
  return (Number(field.inchFraction) > 0 ? Number(field.inchFraction) : 8) as InchFractionStep
}

export function centimetreDecimalsOf(field: TemplateField): CentimetreDecimals {
  const decimals = Number(field.centimetreDecimals)
  return (decimals > 0 ? decimals : 1) as CentimetreDecimals
}

/** The unit the wizard opens in, as the version declares it. Anything unrecognised opens in inches. */
export function defaultDisplayUnitOf(version: TemplateVersion): MeasurementDisplayUnit {
  return version.defaultDisplayUnit === 'Centimetre' ? 'cm' : 'in'
}

/**
 * The values a visibility rule reads, by field key.
 *
 * A choice contributes its option code, which is what a rule names; a measured or counted field
 * contributes the number, so a clause on it can at least be settled as "answered". A field with no
 * answer is absent, which is what makes a rule on it undecidable rather than false.
 */
export function ruleValuesOf(state: CaptureState): Readonly<Record<string, string>> {
  const values: Record<string, string> = {}

  for (const [key, held] of Object.entries(state)) {
    if (held.choice !== undefined) {
      values[key] = held.choice
    } else if (held.millimetres !== undefined) {
      values[key] = String(held.millimetres)
    }
  }

  return values
}

/**
 * Whether a field is asked for with the answers so far.
 *
 * Design selections are empty: a measurement at the counter is outside an order, which is exactly
 * the case a rule on a design choice cannot be settled in — and an unsettled rule **shows** the
 * field, because hiding on missing information drops a measurement the tailor needs.
 */
export function visibilityOf(field: TemplateField, state: CaptureState): RuleEvaluation {
  return evaluateRule(ruleToDraft(field.ruleDefinition), ruleValuesOf(state), {})
}

/** The state a draft read from the server unpacks into. */
export function stateOf(draft: MeasurementDraft): CaptureState {
  const state: Record<string, CapturedFieldState> = {}

  for (const value of draft.values) {
    if (value.choice !== null) {
      state[value.key] = { choice: value.choice, acknowledged: value.acknowledged }
    } else if (value.millimetres !== null) {
      state[value.key] = {
        millimetres: Number(value.millimetres),
        acknowledged: value.acknowledged,
      }
    }
  }

  return state
}

/**
 * A millimetre value as it reads in the unit on screen, for the request.
 *
 * Inches are the whole number plus the fraction the control showed, so `927.1 mm` at eighths goes
 * back as `36.5`; centimetres are rounded to the field's own decimals; a count is the whole number
 * it is. The server converts again and stores what *it* produced.
 */
export function enteredFor(
  field: TemplateField,
  millimetres: number,
  unit: MeasurementDisplayUnit,
): { readonly entered: number; readonly unit: string } {
  if (captureKindOf(field) === 'count') {
    return { entered: Math.round(millimetres), unit: 'Count' }
  }

  if (unit === 'cm') {
    return {
      entered: millimetresToCentimetres(millimetres, centimetreDecimalsOf(field)),
      unit: 'Centimetre',
    }
  }

  const fraction = millimetresToInchFraction(millimetres, fractionStepOf(field))
  const magnitude = fraction.whole + fraction.numerator / fraction.denominator

  return { entered: fraction.negative ? -magnitude : magnitude, unit: 'Inch' }
}

/**
 * The request that saves one step.
 *
 * Every answered, shown field of the group, and nothing else: the server replaces the step with what
 * it is sent, so a field the person cleared is sent by not being sent, and a field a rule has hidden
 * is dropped rather than saved with a value nobody can see.
 */
export function sectionRequestOf(
  group: FieldGroup,
  state: CaptureState,
  unit: MeasurementDisplayUnit,
): SaveMeasurementSectionRequest {
  const values: MeasurementValueRequest[] = []

  for (const field of group.fields) {
    const held = state[field.key]
    if (held === undefined || !visibilityOf(field, state).isShown) {
      continue
    }

    if (captureKindOf(field) === 'choice') {
      if (held.choice !== undefined && held.choice !== '') {
        values.push({
          key: field.key,
          entered: null,
          unit: null,
          choice: held.choice,
          acknowledged: held.acknowledged,
        })
      }
      continue
    }

    if (held.millimetres === undefined) {
      continue
    }

    const { entered, unit: enteredUnit } = enteredFor(field, held.millimetres, unit)
    values.push({
      key: field.key,
      entered,
      unit: enteredUnit,
      choice: null,
      acknowledged: held.acknowledged,
    })
  }

  return { groupName: group.name, values }
}

/** How the wizard renders a bound for a message, supplied by the screen so the unit is the one on screen. */
export type BoundFormatter = (field: TemplateField, millimetres: number) => string

/** The words for the two things the client can tell before asking the server. */
export interface CaptureMessages {
  readonly required: (label: string) => string
  readonly outOfRange: (label: string, minimum: string, maximum: string) => string
}

/**
 * Everything the client can already tell is wrong with a step, or with the whole draft.
 *
 * Only the two rules a person holding a tape needs to hear before the round trip: a required field
 * left empty, and a value outside the hard bounds. The confirmation band is not an error — it is a
 * warning with an acknowledgement, and the server decides whether the acknowledgement was given.
 * Everything else — consent, the template still being published, a value off its step — is the
 * server's to say, and `findingErrorsOf` carries its answer into the same summary.
 */
export function localErrorsOf(
  groups: readonly FieldGroup[],
  state: CaptureState,
  formatBound: BoundFormatter,
  messages: CaptureMessages,
): readonly FieldErrorEntry[] {
  const errors: FieldErrorEntry[] = []

  for (const group of groups) {
    for (const field of group.fields) {
      if (!visibilityOf(field, state).isShown) {
        continue
      }

      const held = state[field.key]
      const kind = captureKindOf(field)
      const answered =
        kind === 'choice'
          ? held?.choice !== undefined && held.choice !== ''
          : held?.millimetres !== undefined

      if (!answered) {
        if (field.isRequired) {
          errors.push({
            name: field.key,
            message: messages.required(field.label),
            controlId: captureControlId(field.key),
            stepId: stepIdOf(group),
          })
        }
        continue
      }

      if (kind !== 'measured' || held?.millimetres === undefined) {
        continue
      }

      const bounds = boundsOf(field)
      if (
        bounds.minimumMillimetres !== undefined &&
        bounds.maximumMillimetres !== undefined &&
        isRejectedBand(evaluateMeasurement(held.millimetres, bounds))
      ) {
        errors.push({
          name: field.key,
          message: messages.outOfRange(
            field.label,
            formatBound(field, bounds.minimumMillimetres),
            formatBound(field, bounds.maximumMillimetres),
          ),
          controlId: captureControlId(field.key),
          stepId: stepIdOf(group),
        })
      }
    }
  }

  return errors
}

/**
 * The server's findings, in the shape the summary already understands.
 *
 * A finding names a field and never a value, and the message is the server's own words — the shop's
 * words, not a code. A finding about the draft as a whole (no field) is carried under the review step
 * so it is listed rather than lost, and a finding naming a field the version does not have is
 * dropped: a summary link that goes nowhere is worse than no link.
 */
export function findingErrorsOf(
  findings: readonly MeasurementFinding[],
  groups: readonly FieldGroup[],
): readonly FieldErrorEntry[] {
  const entries: FieldErrorEntry[] = []

  for (const finding of findings) {
    if (finding.field === null) {
      entries.push({
        name: finding.code,
        message: finding.message,
        controlId: 'capture-review',
        stepId: REVIEW_STEP_ID,
      })
      continue
    }

    const group = groups.find((candidate) =>
      candidate.fields.some((field) => field.key === finding.field),
    )
    if (group === undefined) {
      continue
    }

    entries.push({
      name: finding.field,
      message: finding.message,
      controlId: captureControlId(finding.field),
      stepId: stepIdOf(group),
    })
  }

  return entries
}
