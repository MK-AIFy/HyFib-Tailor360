/**
 * The value unions the forms family uses, kept in a `.ts` file rather than beside their components.
 *
 * Not an organisational preference: `react-refresh/only-export-components` warns when a `.tsx`
 * module exports something that is not a component, because a fast-refresh boundary that also
 * exports a value cannot be replaced safely. Keeping the unions here leaves every `.tsx` in this
 * family exporting components and types only, and leaves `eslint.config.js` — a file every family
 * would otherwise want to edit — alone.
 */

/**
 * The input types this system uses.
 *
 * `number` is absent on purpose. A `type="number"` control rejects the comma an Android keypad
 * offers for a decimal (`36,5`), changes its value when a wheel is scrolled over it, and is
 * announced inconsistently by screen readers. Numeric entry uses a text control with `inputMode`,
 * read by `parseDecimal`, which accepts both `,` and `.` by rule (docs/prd/measurement-templates.md
 * section 2 and docs/nfr/accessibility-localisation.md section 12.1).
 */
export const TEXT_FIELD_TYPES = ['text', 'tel', 'email', 'url', 'search', 'password'] as const

export type TextFieldType = (typeof TEXT_FIELD_TYPES)[number]

/**
 * What the virtual keyboard's action key says. `next` is what makes a measurement wizard follow the
 * tape rather than the alphabet (docs/prd/measurement-templates.md section 3).
 */
export const ENTER_KEY_HINTS = [
  'enter',
  'done',
  'go',
  'next',
  'previous',
  'search',
  'send',
] as const

export type EnterKeyHint = (typeof ENTER_KEY_HINTS)[number]

/** One option of a select or a radio group. */
export interface ChoiceOption {
  readonly value: string
  /** The visible text. Also the accessible name, so voice control works (2.5.3). */
  readonly label: string
  /** Persistent help under the option. Radio groups only; a native `<option>` cannot carry it. */
  readonly description?: string
  readonly disabled?: boolean
}

/**
 * One step of a wizard, as the error summary needs to know it.
 *
 * The summary is step-aware because a measurement capture is four steps long and a failed confirm
 * lists errors from all of them: an entry for a field on step two has to say so and has to open
 * step two before it moves focus, or the link goes nowhere (checklist item A11Y-36).
 */
export interface FormStep {
  readonly id: string
  /** What the step is called on screen — "Bodice", "Sleeve". Comes from the message catalogue. */
  readonly label: string
}
