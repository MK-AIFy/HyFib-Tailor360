import type { ControlSize } from './types'

/**
 * The one form contract.
 *
 * Every field in the design system — text, textarea, select, checkbox, radio, switch, date, the
 * numeric `FractionInput` and `NumericStepper` — implements `FieldProps`. That is what
 * docs/nfr/accessibility-localisation.md section 8.1 means by "a screen cannot accidentally ship a
 * field without a label, because the component has nowhere to put the text", and it is what the
 * form contract tests assert against.
 *
 * Four rules are enforced by the shape of this interface rather than by review:
 *
 *  1. `label` is required and is always rendered visibly. There is no `labelHidden` and no
 *     `aria-label` escape hatch, because WCAG 3.3.2 wants a persistent visible label and 2.5.3
 *     Label in Name wants the visible words to be in the accessible name so voice control works.
 *     A field with no visible label is not a field this system builds.
 *  2. There is no `placeholder`. A placeholder disappears the moment somebody types, which on a
 *     counter tablet means the unit and the format vanish exactly when they are needed. Persistent
 *     help goes in `description`.
 *  3. `error` is a sentence, not a code. "Waist must be between 45.0 cm and 150.0 cm", never
 *     "validation failed" and never a server stack trace (3.3.1, 3.3.3).
 *  4. `unit` carries both the symbol and a spoken label, because "in" read aloud is not "inches".
 *
 * The generic parameter is the value type, so `FieldProps<number>` and `FieldProps<string>` share
 * one contract while each control keeps a precise `value` and `onValueChange`.
 */

/**
 * The `inputmode` values this product uses. `tel` on a customer phone number and `decimal` on a
 * measurement are not stylistic choices: 1.3.5 Identify Input Purpose and the shop-floor rule in
 * docs/nfr/accessibility-localisation.md section 5 both name them, and `decimal` is what puts a
 * separator key on the Android keypad so a measurement can be typed one-handed.
 */
export const FIELD_INPUT_MODES = [
  'none',
  'text',
  'decimal',
  'numeric',
  'tel',
  'search',
  'email',
  'url',
] as const

export type FieldInputMode = (typeof FIELD_INPUT_MODES)[number]

/**
 * A unit adornment shown inside or beside the control.
 *
 * `symbol` is what appears on screen — `in`, `cm`, `mm`, `₹`, `%`. `label` is what assistive
 * technology reads — `inches`, `centimetres`, `rupees`. Both come from the message catalogue, never
 * from a literal in a component, because the unit *word* is glossary-owned and translated while the
 * symbol is not (docs/nfr/accessibility-localisation.md section 12.1).
 */
export interface FieldUnit {
  /** The symbol as printed: `in`, `cm`, `₹`. */
  readonly symbol: string
  /** The spoken form: `inches`, `centimetres`, `rupees`. */
  readonly label: string
  /** Currency leads, measurement units trail. Defaults to `trailing`. */
  readonly position?: 'leading' | 'trailing'
}

/**
 * The half of the contract the accessibility wiring reads.
 *
 * Split out because none of it depends on the value type: `fieldControlAttributes` can then accept
 * a `FieldProps<string>` and a `FieldProps<number>` alike, which a `FieldProps<unknown>` parameter
 * cannot do — a callback taking `unknown` is not a supertype of one taking `string`.
 */
export interface FieldAriaSource {
  /**
   * The form field name. Also the key a server problem-details error is matched on, which is why it
   * is required even on a field that is not inside a `<form>`.
   */
  readonly name: string

  /**
   * Persistent help shown under the label: the expected range, the format, where on the body a
   * measurement is taken. Wired into `aria-describedby`, so it is read with the field rather than
   * being decoration a screen reader never reaches.
   */
  readonly description?: string

  /**
   * The error, in words. Present means invalid: the component sets `aria-invalid`, points
   * `aria-describedby` at the message and marks itself for the `FormErrorSummary`.
   */
  readonly error?: string

  /**
   * A value that is allowed but unusual — the confirmation band of a measurement template, a price
   * far from the price list. Announced politely and never blocking. Distinct from `error` because a
   * warning must not make the field invalid or stop a submit.
   */
  readonly warning?: string

  /**
   * Whether a value is required. The component sets `aria-required` and marks the label; it does
   * **not** set the native `required` attribute, whose browser bubble would compete with the
   * `FormErrorSummary` and cannot be translated.
   */
  readonly required?: boolean

  /** Not editable and not focusable. Genuinely disabled controls only; never used to convey status. */
  readonly disabled?: boolean

  /** Not editable but still focusable and readable — the right state for a value under review. */
  readonly readOnly?: boolean

  /** The unit adornment. */
  readonly unit?: FieldUnit

  /** The virtual-keyboard hint. */
  readonly inputMode?: FieldInputMode

  /**
   * The HTML `autocomplete` token. Required on every customer field by WCAG 1.3.5 and by the #50
   * blueprint: `name`, `tel`, `street-address`, `postal-code`, and `one-time-code` on the
   * authentication field, which must also accept paste (3.3.8).
   */
  readonly autoComplete?: string

  /**
   * Ids of elements outside the field that also describe it — a step summary, a shared note above a
   * group. Appended after the field's own description ids.
   */
  readonly describedByIds?: readonly string[]
}

export interface FieldProps<TValue = string> extends FieldAriaSource {
  /** The visible label. Always rendered; see rule 1 above. */
  readonly label: string

  /**
   * An explicit id for the control. Omit it and the component generates one with `useFieldIds`;
   * supply it when something outside the field has to link to it — the step-aware
   * `FormErrorSummary` links to the first invalid control by id.
   */
  readonly id?: string

  /** Target size. Defaults to `standard`; shop-floor primary actions use `primary`. */
  readonly size?: ControlSize

  /** The current value. */
  readonly value?: TValue

  /** Called with the new value. Named for the value, not the event, so every control agrees. */
  readonly onValueChange?: (value: TValue) => void
}

/** The ids a field generates for its own parts, so the aria wiring is computed rather than typed. */
export interface FieldElementIds {
  readonly control: string
  readonly label: string
  readonly description: string
  readonly error: string
  readonly warning: string
  readonly unit: string
}

/** Derives the part ids from one base. Pure, so a test can assert the wiring without rendering. */
export function fieldElementIds(base: string): FieldElementIds {
  return {
    control: base,
    label: `${base}-label`,
    description: `${base}-description`,
    error: `${base}-error`,
    warning: `${base}-warning`,
    unit: `${base}-unit`,
  }
}

/**
 * The `aria-describedby` value for a field, or undefined when there is nothing to describe it.
 *
 * The order is deliberate, because a screen reader reads the list in order: error first, because a
 * failed submit is what moved focus here; then the warning; then the unit, because "inches" changes
 * what the number means; then the description, which is the longest; then anything external.
 */
export function fieldDescribedBy(
  props: Pick<FieldAriaSource, 'description' | 'error' | 'warning' | 'unit' | 'describedByIds'>,
  ids: FieldElementIds,
): string | undefined {
  const parts = [
    props.error === undefined ? undefined : ids.error,
    props.warning === undefined ? undefined : ids.warning,
    props.unit === undefined ? undefined : ids.unit,
    props.description === undefined ? undefined : ids.description,
    ...(props.describedByIds ?? []),
  ].filter((part): part is string => typeof part === 'string' && part.length > 0)

  return parts.length > 0 ? parts.join(' ') : undefined
}

/**
 * The attributes a field control element receives.
 *
 * Every optional key is omitted rather than set to undefined, because the application compiles with
 * `exactOptionalPropertyTypes` and because an `aria-invalid="false"` in the DOM is noise a screen
 * reader still has to walk past.
 */
export interface FieldControlAttributes {
  readonly id: string
  readonly name: string
  readonly 'aria-describedby'?: string
  readonly 'aria-invalid'?: true
  readonly 'aria-required'?: true
  readonly inputMode?: FieldInputMode
  readonly autoComplete?: string
  readonly disabled?: true
  readonly readOnly?: true
}

/**
 * Computes the control attributes from the contract. Every field component calls this and spreads
 * the result onto its input, which is what makes the wiring identical across the system — and what
 * makes it testable once here instead of once per component.
 */
export function fieldControlAttributes(
  props: FieldAriaSource,
  ids: FieldElementIds,
): FieldControlAttributes {
  const describedBy = fieldDescribedBy(props, ids)

  return {
    id: ids.control,
    name: props.name,
    ...(describedBy === undefined ? {} : { 'aria-describedby': describedBy }),
    ...(props.error === undefined ? {} : { 'aria-invalid': true as const }),
    ...(props.required === true ? { 'aria-required': true as const } : {}),
    ...(props.inputMode === undefined ? {} : { inputMode: props.inputMode }),
    ...(props.autoComplete === undefined ? {} : { autoComplete: props.autoComplete }),
    ...(props.disabled === true ? { disabled: true as const } : {}),
    ...(props.readOnly === true ? { readOnly: true as const } : {}),
  }
}

/** Whether the field is currently invalid. One place, so no component invents its own test. */
export function isFieldInvalid(props: Pick<FieldAriaSource, 'error'>): boolean {
  return props.error !== undefined && props.error.length > 0
}

/**
 * One entry in the step-aware `FormErrorSummary`.
 *
 * `stepId` is what makes the summary step-aware: on a wizard, the summary lists errors from every
 * step and its link moves to the step before it moves focus to the field, so "Waist is required" on
 * step two is reachable from step four.
 */
export interface FieldErrorEntry {
  /** The field name, matching `FieldProps.name`. */
  readonly name: string
  /** The message, in words. */
  readonly message: string
  /** The id of the control to move focus to. */
  readonly controlId: string
  /** The wizard step the field is on, when the form has steps. */
  readonly stepId?: string
}

/**
 * The subset of an RFC 9457 problem-details response the client reads for field errors.
 *
 * The full contract is #53's; this is the shape the form layer needs, kept here so a field error
 * from the server and a field error from client validation are the same type by the time they reach
 * `FormErrorSummary`. `detail` and `title` are never rendered raw — the mapper below takes only the
 * per-field messages, and the correlation identifier is shown for support, never the exception.
 */
export interface ValidationProblemDetails {
  readonly type?: string
  readonly title?: string
  readonly status?: number
  readonly detail?: string
  /** Field name to one or more messages, as ASP.NET's validation problem details produces. */
  readonly errors?: Readonly<Record<string, readonly string[]>>
}

/**
 * Maps server field errors onto the summary entries the form already understands.
 *
 * `resolveControlId` maps a server field name — which may be dotted, `measurements.waist` — onto the
 * control id on screen; a name the screen does not have is dropped rather than shown as an
 * unreachable summary line, because a summary link that goes nowhere is worse than no link.
 */
export function fieldErrorsFromProblemDetails(
  problem: ValidationProblemDetails,
  resolveControlId: (fieldName: string) => string | undefined,
): readonly FieldErrorEntry[] {
  const errors = problem.errors
  if (errors === undefined) {
    return []
  }

  const entries: FieldErrorEntry[] = []
  for (const [name, messages] of Object.entries(errors)) {
    const controlId = resolveControlId(name)
    if (controlId === undefined) {
      continue
    }
    for (const message of messages) {
      entries.push({ name, message, controlId })
    }
  }
  return entries
}
