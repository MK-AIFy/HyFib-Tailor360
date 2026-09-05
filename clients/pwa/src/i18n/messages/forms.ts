/**
 * Form messages — labels, hints, errors and announcements owned by the forms family.
 *
 * Follows the family rules documented at the top of `messages/shell.ts`: one file, both languages,
 * ICU placeholders for anything that varies, and never a sentence built by concatenation.
 *
 * Two rules from docs/nfr/accessibility-localisation.md shape almost every string here:
 *
 *  - Section 8.2: an error is a sentence a person can act on. "Waist must be between 45.0 cm and
 *    150.0 cm", never "validation failed" and never a field key. That is why the error messages
 *    below take `{label}` and the formatted bounds rather than a code.
 *  - Section 12: no component formats a number, a date or an amount by hand. Every `{minimum}`,
 *    `{maximum}` and `{value}` below arrives already formatted by the `formatters` module in the
 *    reader's display unit, so the sentence is the only thing this catalogue owns.
 */
export const formsEn = {
  // ---------------------------------------------------------------------------------------------
  // The field shell
  // ---------------------------------------------------------------------------------------------
  /** Shown beside the label of a required field. The control also carries `aria-required`. */
  'forms.required': 'Required',
  /**
   * A visually-hidden prefix on the error text, so a screen reader reaching the message through
   * `aria-describedby` hears that it is an error rather than more help text.
   */
  'forms.error.prefix': 'Error:',
  /** The same, for the warning of a confirmation band. Never blocking, so never "Error". */
  'forms.warning.prefix': 'Check:',

  // ---------------------------------------------------------------------------------------------
  // Select
  // ---------------------------------------------------------------------------------------------
  /** The empty option. A visible label always exists as well; this is never the label. */
  'forms.select.choose': 'Choose an option',

  // ---------------------------------------------------------------------------------------------
  // Date
  // ---------------------------------------------------------------------------------------------
  'forms.date.hint': 'Day, month and year — for example {example}.',

  // ---------------------------------------------------------------------------------------------
  // Numeric stepper
  // ---------------------------------------------------------------------------------------------
  'forms.stepper.decrease': 'Decrease {label}',
  'forms.stepper.increase': 'Increase {label}',
  /** Announced after a step, because changing an input's value silently announces nothing. */
  'forms.stepper.announce': '{label}, {value}',
  'forms.stepper.atMinimum': '{label} is already at its lowest value, {value}.',
  'forms.stepper.atMaximum': '{label} is already at its highest value, {value}.',
  'forms.stepper.range': 'Between {minimum} and {maximum}.',

  // ---------------------------------------------------------------------------------------------
  // Inch fraction control
  // ---------------------------------------------------------------------------------------------
  /** The group name announced before the whole-inch box and the fraction strip (A11Y-59). */
  'forms.fraction.wholeLabel': '{label} — whole inches',
  'forms.fraction.fractionLabel': '{label} — fraction of an inch',
  /** The zero option of the fraction strip. */
  'forms.fraction.none': '0',
  /**
   * The assembled value, shown beside the control and announced when it changes, so that
   * `15 3/8 in` is heard as one value rather than as a number and an orphan fraction (A11Y-ME-04).
   */
  'forms.fraction.assembled': '{value}',

  // ---------------------------------------------------------------------------------------------
  // Measurement field
  // ---------------------------------------------------------------------------------------------
  /** The persistent hint under a bounded measurement field (A11Y-29: unit and range, in text). */
  'forms.measurement.expectedRange': 'Expected between {minimum} and {maximum}.',
  'forms.measurement.expectedMinimum': 'Expected {minimum} or more.',
  'forms.measurement.expectedMaximum': 'Expected {maximum} or less.',
  /** Hard bounds. The capture cannot be confirmed until the value is inside them. */
  'forms.measurement.outOfRange': '{label} must be between {minimum} and {maximum}.',
  'forms.measurement.tooSmall': '{label} must be {minimum} or more.',
  'forms.measurement.tooLarge': '{label} must be {maximum} or less.',
  /** An unreadable entry. The example is formatted in the unit on screen. */
  'forms.measurement.unreadable': 'Enter {label} as a number — for example {example}.',
  /**
   * The confirmation band of docs/prd/measurement-templates.md section 7, worded as that document
   * words it. A warning never blocks a save; it has to be read to do anything at all.
   */
  'forms.measurement.outsideUsual':
    'This is outside the usual range. Check the tape and the unit, then confirm.',
  'forms.measurement.acknowledge': 'I have checked the tape and the unit',
  'forms.measurement.acknowledged': 'Recorded: {label} of {value} was checked and confirmed.',

  // ---------------------------------------------------------------------------------------------
  // Error summary
  // ---------------------------------------------------------------------------------------------
  'forms.errorSummary.title':
    '{count, plural, one {There is a problem} other {There are # problems}}',
  'forms.errorSummary.instruction': 'Correct the following, then try again.',
  /** A summary entry for a field on a step other than the one on screen. */
  'forms.errorSummary.entryOnStep': '{message} — {step}',
  /** The correlation identifier a server problem detail carries, for support. Never a stack. */
  'forms.errorSummary.reference': 'If you ask for help, quote reference {reference}.',
} as const

export const formsTa: Record<keyof typeof formsEn, string> = {
  'forms.required': 'கட்டாயம்',
  'forms.error.prefix': 'பிழை:',
  // not translated — awaiting native-speaker review
  'forms.warning.prefix': 'Check:',
  'forms.select.choose': 'ஒன்றைத் தேர்ந்தெடுக்கவும்',
  // not translated — awaiting native-speaker review
  'forms.date.hint': 'Day, month and year — for example {example}.',
  // not translated — awaiting native-speaker review
  'forms.stepper.decrease': 'Decrease {label}',
  // not translated — awaiting native-speaker review
  'forms.stepper.increase': 'Increase {label}',
  'forms.stepper.announce': '{label}, {value}',
  // not translated — awaiting native-speaker review
  'forms.stepper.atMinimum': '{label} is already at its lowest value, {value}.',
  // not translated — awaiting native-speaker review
  'forms.stepper.atMaximum': '{label} is already at its highest value, {value}.',
  // not translated — awaiting native-speaker review
  'forms.stepper.range': 'Between {minimum} and {maximum}.',
  // not translated — awaiting native-speaker review
  'forms.fraction.wholeLabel': '{label} — whole inches',
  // not translated — awaiting native-speaker review
  'forms.fraction.fractionLabel': '{label} — fraction of an inch',
  'forms.fraction.none': '0',
  'forms.fraction.assembled': '{value}',
  // not translated — awaiting native-speaker review
  'forms.measurement.expectedRange': 'Expected between {minimum} and {maximum}.',
  // not translated — awaiting native-speaker review
  'forms.measurement.expectedMinimum': 'Expected {minimum} or more.',
  // not translated — awaiting native-speaker review
  'forms.measurement.expectedMaximum': 'Expected {maximum} or less.',
  // not translated — awaiting native-speaker review
  'forms.measurement.outOfRange': '{label} must be between {minimum} and {maximum}.',
  // not translated — awaiting native-speaker review
  'forms.measurement.tooSmall': '{label} must be {minimum} or more.',
  // not translated — awaiting native-speaker review
  'forms.measurement.tooLarge': '{label} must be {maximum} or less.',
  // not translated — awaiting native-speaker review
  'forms.measurement.unreadable': 'Enter {label} as a number — for example {example}.',
  // not translated — awaiting native-speaker review
  'forms.measurement.outsideUsual':
    'This is outside the usual range. Check the tape and the unit, then confirm.',
  // not translated — awaiting native-speaker review
  'forms.measurement.acknowledge': 'I have checked the tape and the unit',
  // not translated — awaiting native-speaker review
  'forms.measurement.acknowledged': 'Recorded: {label} of {value} was checked and confirmed.',
  // not translated — awaiting native-speaker review
  'forms.errorSummary.title':
    '{count, plural, one {There is a problem} other {There are # problems}}',
  // not translated — awaiting native-speaker review
  'forms.errorSummary.instruction': 'Correct the following, then try again.',
  'forms.errorSummary.entryOnStep': '{message} — {step}',
  // not translated — awaiting native-speaker review
  'forms.errorSummary.reference': 'If you ask for help, quote reference {reference}.',
}
