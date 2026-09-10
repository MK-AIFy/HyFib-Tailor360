import type { TemplateChoiceOption, TemplateField, TemplateFieldRequest } from './types'
import type { MessageKey } from '../i18n/en-IN'

/**
 * The form behind one measurement-template field, and the rules the server will apply to it.
 *
 * ## Why this is a module rather than state inside the screen
 *
 * Every rule below is the server's, restated. That is a dangerous thing to do — a restatement that
 * drifts is a screen that refuses what the server would accept, or accepts what it refuses — so the
 * restatement is kept in one place, beside the citation of the code it mirrors, and tested against
 * the same boundaries rather than through a rendered form. The screen decides what to draw; this
 * decides what is true.
 *
 * The client check exists at all because the alternative is a person filling in ten controls,
 * pressing Save, and being told by a `400` that the key had a capital letter in it. The server
 * remains the authority: nothing here is a substitute for its answer, and a refusal that arrives
 * anyway is rendered per field through `fieldErrorsFromProblemDetails`.
 */

/** What the stored value is. The three the server models, in the order the editor offers them. */
export const CANONICAL_UNITS = ['Millimetre', 'Count', 'None'] as const

export type CanonicalUnit = (typeof CANONICAL_UNITS)[number]

/**
 * The inch steps a field may declare, as denominators.
 *
 * Powers of two, because a tape is divided by halving — `FieldPrecision.PermittedInchFractions`.
 * A denominator of ten has no marking to read against, and thirty-seconds is finer than the tape.
 */
export const INCH_FRACTIONS = [2, 4, 8, 16] as const

/** The most decimal places a centimetre display may declare (`FieldPrecision`). */
export const MAXIMUM_CENTIMETRE_DECIMALS = 2

/** Lengths the server refuses beyond, from the constants on `TemplateField`. */
export const LIMITS = {
  key: 60,
  label: 120,
  group: 60,
  helpText: 400,
  diagramKey: 80,
  diagramAlt: 400,
  optionCode: 40,
} as const

/** The shortest a key may be (`FieldKey.MinimumLength`). */
export const MINIMUM_KEY_LENGTH = 2

/** `FieldKey.Pattern` — lower case, starting with a letter. */
const KEY_PATTERN = /^[a-z][a-z0-9_]*$/

/** `ChoiceOption.CodePattern` — upper case, starting with a letter or a digit. */
const OPTION_CODE_PATTERN = /^[A-Z0-9][A-Z0-9_]*$/

/**
 * One choice of a `None` field, as the form holds it.
 *
 * Options are edited here rather than in a slice of their own because a `None` field **cannot be
 * saved without at least one** — `TemplateField.CheckShape` refuses it with `ChoiceFieldHasNoOptions`
 * — so an editor that offered the unit and not the options would offer a field that can never be
 * created. No other sub-issue of #94 claims them.
 */
export interface ChoiceOptionDraft {
  readonly code: string
  readonly label: string
  readonly labelTamil: string
}

/** Everything the editor holds for one field, as strings, because that is what controls give back. */
export interface FieldFormState {
  readonly key: string
  readonly label: string
  readonly labelTamil: string
  readonly groupName: string
  readonly helpText: string
  readonly canonicalUnit: CanonicalUnit
  /** The denominator, or 0 for no inch display. */
  readonly inchFraction: number
  readonly centimetreDecimals: number
  readonly isRequired: boolean
  /**
   * The four band numbers, in **canonical millimetres**, or null for a field that declares none.
   *
   * Held canonically and converted only at the control, which is the same rule `MeasurementField`
   * follows: converting once at the boundary is what stops a value drifting by a rounding step every
   * time somebody opens the form and saves it again.
   *
   * The two hard bounds move together. They are not nullable on the wire, and the only way to say
   * "no bounds" is the `ValidationBands.None` sentinel — exactly `0 / 0 / null / null` — so a
   * minimum without a maximum is not a state the server can be told about.
   */
  readonly minimumMillimetres: number | null
  readonly maximumMillimetres: number | null
  readonly warnBelowMillimetres: number | null
  readonly warnAboveMillimetres: number | null
  readonly diagramKey: string
  readonly diagramAlt: string
  readonly options: readonly ChoiceOptionDraft[]
}

/**
 * A new field, before anybody has typed in it.
 *
 * Eighths of an inch and one decimal centimetre is `FieldPrecision.Eighths`, which is what the
 * seeded templates use for a length; it is a starting point a person changes, not a claim about
 * what this field should be. `groupName` is inherited from the field last added so that a person
 * entering a group of six does not retype the group six times.
 */
export function blankField(groupName = ''): FieldFormState {
  return {
    key: '',
    label: '',
    labelTamil: '',
    groupName,
    helpText: '',
    canonicalUnit: 'Millimetre',
    inchFraction: 8,
    centimetreDecimals: 1,
    isRequired: true,
    minimumMillimetres: null,
    maximumMillimetres: null,
    warnBelowMillimetres: null,
    warnAboveMillimetres: null,
    diagramKey: '',
    diagramAlt: '',
    options: [],
  }
}

/** The form for a field that exists, so that editing starts from what is stored. */
export function fieldToForm(field: TemplateField): FieldFormState {
  return {
    key: field.key,
    label: field.label,
    labelTamil: field.labelTamil ?? '',
    groupName: field.groupName,
    helpText: field.helpText,
    canonicalUnit: asCanonicalUnit(field.canonicalUnit),
    inchFraction: Number(field.inchFraction),
    centimetreDecimals: Number(field.centimetreDecimals),
    isRequired: field.isRequired,
    // The sentinel reads back as two zeroes; it is "no bounds", not "a field that accepts only
    // zero", so it opens the form empty rather than with a pair of noughts somebody has to clear.
    minimumMillimetres: declaredBound(field.minimumMillimetres, field.maximumMillimetres),
    maximumMillimetres: declaredBound(field.maximumMillimetres, field.minimumMillimetres),
    warnBelowMillimetres: nullableNumber(field.warnBelowMillimetres),
    warnAboveMillimetres: nullableNumber(field.warnAboveMillimetres),
    diagramKey: field.diagramKey ?? '',
    diagramAlt: field.diagramAlt ?? '',
    // The new half of the superseded pair. `optionCodes` carries the same codes without their
    // labels, and echoing it back would erase every label on the first save.
    options: field.options.map((option) => ({
      code: option.code,
      label: option.label,
      labelTamil: option.labelTamil ?? '',
    })),
  }
}

/** One half of the hard bounds, or null when the pair is the no-bounds sentinel. */
function declaredBound(value: number | string, other: number | string): number | null {
  return Number(value) === 0 && Number(other) === 0 ? null : Number(value)
}

function nullableNumber(value: number | string | null): number | null {
  return value === null ? null : Number(value)
}

/** Falls back to `Millimetre` for a unit this build does not know, rather than rendering a blank. */
function asCanonicalUnit(value: string): CanonicalUnit {
  return (CANONICAL_UNITS as readonly string[]).includes(value)
    ? (value as CanonicalUnit)
    : 'Millimetre'
}

/**
 * Whether this unit is entered as a number a tailor reads on a tape.
 *
 * `Millimetre` is the only one. A `Count` is whole by definition, and a `None` field is a choice —
 * it has no bands, no fraction and no decimals, and the editor renders **nothing** for them rather
 * than a disabled box, because a disabled box says "not now" where the truth is "never".
 */
export function hasPrecision(unit: CanonicalUnit): boolean {
  return unit === 'Millimetre'
}

/** Whether this unit is a choice, and so needs its options listed. */
export function isChoice(unit: CanonicalUnit): boolean {
  return unit === 'None'
}

/** The four members of the quad, in the order the editor asks for them. */
export type BandKey =
  'minimumMillimetres' | 'warnBelowMillimetres' | 'warnAboveMillimetres' | 'maximumMillimetres'

/** The same four, as a list a screen can iterate and a refusal can be matched against. */
export const BAND_KEYS: readonly BandKey[] = [
  'minimumMillimetres',
  'warnBelowMillimetres',
  'warnAboveMillimetres',
  'maximumMillimetres',
]

/**
 * Whether this field declares hard bounds.
 *
 * The pair moves together, because the wire cannot carry half of it: both bounds are non-nullable,
 * so a minimum on its own would have to travel beside a maximum of zero, which the server reads as
 * `minimum > maximum` and refuses.
 */
export function hasBounds(form: FieldFormState): boolean {
  return form.minimumMillimetres !== null || form.maximumMillimetres !== null
}

/**
 * The precision a unit is stored with.
 *
 * `Count` and `None` are `FieldPrecision.Whole` — exactly `(0, 0)` — and the server does **not**
 * correct a precision sent alongside them the way it corrects the bands: `PrecisionNotAllowedForUnit`
 * refuses a count with an inch step, and a choice field sent `inchFraction: 8` is accepted and
 * stored, then reads back with a step it can never use. So the editor sends the zeroes itself.
 */
function precisionFor(form: FieldFormState): { inchFraction: number; centimetreDecimals: number } {
  return hasPrecision(form.canonicalUnit)
    ? { inchFraction: form.inchFraction, centimetreDecimals: form.centimetreDecimals }
    : { inchFraction: 0, centimetreDecimals: 0 }
}

/**
 * The nineteen members, built from the form and from the field being replaced.
 *
 * ## Why `existing` is a parameter
 *
 * There is no `PATCH`. A change sends the whole field, including the members this screen does not
 * edit — the bands, which are #103's, and the display order and group ordering, which are #104's,
 * and the visibility rule, which is #95's. Those are echoed from the field as it was read, so that
 * saving a label cannot silently reset a bound somebody set on another screen.
 *
 * For a new field there is nothing to echo, so the bands are the `ValidationBands.None` sentinel —
 * exactly `0 / 0 / null / null`, which is the only way to say "no bounds", since the two hard bounds
 * are not nullable. A warning threshold sent beside that sentinel is refused as outside the bounds,
 * which is why both are null rather than zero.
 *
 * @param form What the person typed.
 * @param existing The field being replaced, or null when this is a new one.
 * @param displayOrder Where the field sits. Ordering is #104; this keeps a new field at the end.
 */
export function toRequest(
  form: FieldFormState,
  existing: TemplateField | null,
  displayOrder: number,
): TemplateFieldRequest {
  const precision = precisionFor(form)
  const choice = isChoice(form.canonicalUnit)
  const bounded = hasBounds(form)
  const diagramKey = trimmedOrNull(form.diagramKey)

  return {
    key: form.key.trim(),
    label: form.label.trim(),
    labelTamil: trimmedOrNull(form.labelTamil),
    groupName: form.groupName.trim(),
    displayOrder,
    canonicalUnit: form.canonicalUnit,
    inchFraction: precision.inchFraction,
    centimetreDecimals: precision.centimetreDecimals,
    isRequired: form.isRequired,
    // A choice field has no bands at all and the API nulls them out itself. A numeric field that
    // declares none sends the sentinel: the two hard bounds are not nullable, so `0 / 0 / null /
    // null` is the only way to say it, and a warning threshold beside that quad would be refused as
    // outside the bounds — which is why both warnings go with it rather than surviving on their own.
    minimumMillimetres: choice ? 0 : (form.minimumMillimetres ?? 0),
    maximumMillimetres: choice ? 0 : (form.maximumMillimetres ?? 0),
    warnBelowMillimetres: choice || !bounded ? null : form.warnBelowMillimetres,
    warnAboveMillimetres: choice || !bounded ? null : form.warnAboveMillimetres,
    helpText: form.helpText.trim(),
    diagramKey,
    // Alternative text is only meaningful with a diagram, and the server refuses a diagram without
    // it (`DiagramAltMissing`). Clearing the key therefore clears the text rather than leaving an
    // orphan description of a picture that is no longer referenced.
    diagramAlt: diagramKey === null ? null : trimmedOrNull(form.diagramAlt),
    // Accepted by the schema and unproducible: the Media module maps an empty endpoint group and
    // #31 does not exist, so nothing in this application can obtain one. Echoed rather than dropped
    // so that a value set by some later screen survives an edit made here.
    diagramMediaId: existing?.diagramMediaId ?? null,
    // The *definition*, never the rendered English sentence the response carries under this name.
    // The rule builder is #95; until then a rule already on the field is preserved, not erased.
    rule: existing?.ruleDefinition ?? null,
    options: choice ? form.options.map(toOption) : null,
  }
}

function toOption(option: ChoiceOptionDraft, index: number): TemplateChoiceOption {
  return {
    code: option.code.trim().toUpperCase(),
    label: option.label.trim(),
    labelTamil: trimmedOrNull(option.labelTamil),
    displayOrder: index,
  }
}

function trimmedOrNull(value: string): string | null {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

/** A refusal this screen can state before the round trip, keyed by the control it belongs to. */
export interface FieldFormError {
  readonly field: keyof FieldFormState | 'options'
  /** The catalogue key of the sentence. A `MessageKey`, so a typo is a compile error. */
  readonly messageId: MessageKey
  readonly values?: Readonly<Record<string, string | number>>
  /** Which option the refusal is about, for the ones that are. */
  readonly optionIndex?: number
}

/**
 * Everything the server would refuse, checked before it is asked.
 *
 * Every rule here cites the code it mirrors. The list is returned whole rather than stopping at the
 * first refusal, because a person who fixes one problem and is then told about the next has been
 * made to submit a form four times to learn four things — which is the failure 3.3.1 is about.
 *
 * @param form The form state.
 * @param existingKeys Every other field's key in this version, so a duplicate is caught here.
 * @param isNew Whether the key is being set. It is ignored on a change and so is not checked then.
 */
export function validateField(
  form: FieldFormState,
  existingKeys: readonly string[],
  isNew: boolean,
): readonly FieldFormError[] {
  const errors: FieldFormError[] = []
  const key = form.key.trim()

  if (isNew) {
    if (key === '') {
      errors.push({ field: 'key', messageId: 'admin.field.error.keyRequired' })
    } else if (
      key.length < MINIMUM_KEY_LENGTH ||
      key.length > LIMITS.key ||
      !KEY_PATTERN.test(key)
    ) {
      errors.push({ field: 'key', messageId: 'admin.field.error.keyMalformed' })
    } else if (existingKeys.includes(key)) {
      errors.push({ field: 'key', messageId: 'admin.field.error.keyTaken' })
    }
  }

  pushRequired(errors, 'label', form.label, 'admin.field.error.labelRequired')
  pushTooLong(errors, 'label', form.label, LIMITS.label)
  pushRequired(errors, 'groupName', form.groupName, 'admin.field.error.groupRequired')
  pushTooLong(errors, 'groupName', form.groupName, LIMITS.group)
  // Required for every field: it is the only place the difference between a body measurement and a
  // finished-garment measurement is written down, and confusing the two is the commonest cause of a
  // re-make (`TemplateField.Check`).
  pushRequired(errors, 'helpText', form.helpText, 'admin.field.error.helpRequired')
  pushTooLong(errors, 'helpText', form.helpText, LIMITS.helpText)
  pushTooLong(errors, 'diagramKey', form.diagramKey, LIMITS.diagramKey)
  pushTooLong(errors, 'diagramAlt', form.diagramAlt, LIMITS.diagramAlt)

  // A diagram nobody can describe is a diagram a screen-reader user is simply not given
  // (`DiagramAltMissing`). The key is what makes the text required, not the other way round.
  if (form.diagramKey.trim() !== '' && form.diagramAlt.trim() === '') {
    errors.push({ field: 'diagramAlt', messageId: 'admin.field.error.altRequired' })
  }

  if (hasPrecision(form.canonicalUnit)) {
    // `PrecisionMissing`: a millimetre field with neither an inch step nor a decimal place has no
    // unit anybody could enter it in.
    if (form.inchFraction === 0 && form.centimetreDecimals === 0) {
      errors.push({ field: 'inchFraction', messageId: 'admin.field.error.precisionMissing' })
    } else if (
      form.inchFraction !== 0 &&
      !(INCH_FRACTIONS as readonly number[]).includes(form.inchFraction)
    ) {
      errors.push({ field: 'inchFraction', messageId: 'admin.field.error.fractionNotPermitted' })
    }
  }

  if (isChoice(form.canonicalUnit)) {
    errors.push(...validateOptions(form.options))
  } else {
    errors.push(...validateBands(form))
  }

  return errors
}

/**
 * The band rules, restated from `ValidationBands.Validate`.
 *
 * Every one of them exists because a template that breaks it is a template a reviewer *believed*
 * had a check and does not — a warning band outside the hard bounds is never reached, because the
 * value is refused before anybody is asked to confirm it. That is worth saying before the round
 * trip rather than after, since a person has just typed four numbers.
 */
function validateBands(form: FieldFormState): readonly FieldFormError[] {
  const errors: FieldFormError[] = []
  const { minimumMillimetres: min, maximumMillimetres: max } = form
  const { warnBelowMillimetres: low, warnAboveMillimetres: high } = form

  if (!hasBounds(form)) {
    // No bounds is a legitimate state, and so is a field with no warning band. A threshold without
    // bounds is not: it travels beside the `0 / 0` sentinel and is refused as outside it.
    if (low !== null || high !== null) {
      errors.push({
        field: 'warnBelowMillimetres',
        messageId: 'admin.field.error.warningWithoutBounds',
      })
    }
    return errors
  }

  if (min === null) {
    errors.push({ field: 'minimumMillimetres', messageId: 'admin.field.error.boundsIncomplete' })
  }
  if (max === null) {
    errors.push({ field: 'maximumMillimetres', messageId: 'admin.field.error.boundsIncomplete' })
  }
  if (min === null || max === null) {
    return errors
  }

  if (min > max) {
    errors.push({ field: 'minimumMillimetres', messageId: 'admin.field.error.boundsOutOfOrder' })
    return errors
  }

  if (low !== null && (low < min || low > max)) {
    errors.push({
      field: 'warnBelowMillimetres',
      messageId: 'admin.field.error.warningOutsideBounds',
    })
  }
  if (high !== null && (high < min || high > max)) {
    errors.push({
      field: 'warnAboveMillimetres',
      messageId: 'admin.field.error.warningOutsideBounds',
    })
  }
  if (low !== null && high !== null && low > high) {
    errors.push({
      field: 'warnAboveMillimetres',
      messageId: 'admin.field.error.warningBandOutOfOrder',
    })
  }

  return errors
}

function validateOptions(options: readonly ChoiceOptionDraft[]): readonly FieldFormError[] {
  if (options.length === 0) {
    return [{ field: 'options', messageId: 'admin.field.error.optionsRequired' }]
  }

  const errors: FieldFormError[] = []
  const seen = new Set<string>()

  options.forEach((option, index) => {
    const code = option.code.trim().toUpperCase()

    if (code === '' || code.length > LIMITS.optionCode || !OPTION_CODE_PATTERN.test(code)) {
      errors.push({
        field: 'options',
        messageId: 'admin.field.error.optionCode',
        optionIndex: index,
      })
    } else if (seen.has(code)) {
      errors.push({
        field: 'options',
        messageId: 'admin.field.error.optionDuplicate',
        optionIndex: index,
      })
    }

    seen.add(code)

    if (option.label.trim() === '') {
      errors.push({
        field: 'options',
        messageId: 'admin.field.error.optionLabel',
        optionIndex: index,
      })
    }
  })

  return errors
}

function pushRequired(
  errors: FieldFormError[],
  field: keyof FieldFormState,
  value: string,
  messageId: MessageKey,
): void {
  if (value.trim() === '') {
    errors.push({ field, messageId })
  }
}

function pushTooLong(
  errors: FieldFormError[],
  field: keyof FieldFormState,
  value: string,
  limit: number,
): void {
  if (value.trim().length > limit) {
    errors.push({ field, messageId: 'admin.field.error.tooLong', values: { limit } })
  }
}
