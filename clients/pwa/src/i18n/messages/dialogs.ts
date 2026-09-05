/**
 * Dialog, sheet, drawer and confirmation messages.
 *
 * The three confirmation tiers of docs/nfr/accessibility-localisation.md section 8.4 live here, and
 * the wording is not decoration. Two strings in particular are fixed by the specification rather
 * than chosen:
 *
 *  - `dialogs.irreversible` is the exact sentence the #50 blueprint requires wherever an action
 *    cannot be undone — **"Cannot be undone — a supervisor correction is needed"**. It is one
 *    string, not a template, because a person learns to recognise it and a translator has to keep
 *    it recognisable.
 *  - `dialogs.reason.description` names **what the reason will be attached to**, which is what
 *    checklist item A11Y-87 asks for: a reason box with no statement of where the reason goes is an
 *    unlabelled box inside a confirmation dialog.
 *
 * See the top of `messages/shell.ts` for the rules every family file follows.
 */
export const dialogsEn = {
  /* The frame ----------------------------------------------------------------------------- */
  'dialogs.close': 'Close',
  'dialogs.confirm': 'Confirm',
  'dialogs.cancel': 'Cancel',

  /* Consequence --------------------------------------------------------------------------- */
  'dialogs.irreversible': 'Cannot be undone — a supervisor correction is needed',

  /* Tier two: confirm with a reason --------------------------------------------------------- */
  'dialogs.reason.label': 'Reason',
  'dialogs.reason.description':
    'Recorded on the audit trail beside {action}, and shown to whoever reviews it. It cannot be changed afterwards.',
  'dialogs.reason.missing': 'Type the reason before you confirm.',

  /* Tier three: typed confirmation, desktop and tablet administration only -------------------- */
  'dialogs.typed.label': 'Type {phrase} to confirm',
  'dialogs.typed.description': 'Type the words exactly as they are shown above.',
  'dialogs.typed.mismatch': 'The words do not match. Type {phrase} exactly as it is shown.',

  /* The phone substitute for tier three: a reason and a second explicit tap ------------------- */
  'dialogs.secondTap.prompt': 'Tap Confirm once more to {action}.',
  'dialogs.secondTap.label': 'Confirm again',

  /* The three-second undo for non-sensitive field actions ------------------------------------ */
  'dialogs.undo.label': 'Undo',
  'dialogs.undo.announcement': '{action}. Undo is available for a moment.',
  'dialogs.undo.remaining': '{seconds, plural, one {# second left} other {# seconds left}}',
  'dialogs.undo.held': 'Held open while you are on it.',
} as const

export const dialogsTa: Record<keyof typeof dialogsEn, string> = {
  'dialogs.close': 'மூடு',
  'dialogs.confirm': 'உறுதிசெய்',
  'dialogs.cancel': 'ரத்து செய்',

  // not translated — awaiting native-speaker review
  'dialogs.irreversible': 'Cannot be undone — a supervisor correction is needed',

  'dialogs.reason.label': 'காரணம்',
  // not translated — awaiting native-speaker review
  'dialogs.reason.description':
    'Recorded on the audit trail beside {action}, and shown to whoever reviews it. It cannot be changed afterwards.',
  // not translated — awaiting native-speaker review
  'dialogs.reason.missing': 'Type the reason before you confirm.',

  // not translated — awaiting native-speaker review
  'dialogs.typed.label': 'Type {phrase} to confirm',
  // not translated — awaiting native-speaker review
  'dialogs.typed.description': 'Type the words exactly as they are shown above.',
  // not translated — awaiting native-speaker review
  'dialogs.typed.mismatch': 'The words do not match. Type {phrase} exactly as it is shown.',

  // not translated — awaiting native-speaker review
  'dialogs.secondTap.prompt': 'Tap Confirm once more to {action}.',
  'dialogs.secondTap.label': 'மீண்டும் உறுதிசெய்',

  'dialogs.undo.label': 'செயல்தவிர்',
  // not translated — awaiting native-speaker review
  'dialogs.undo.announcement': '{action}. Undo is available for a moment.',
  // not translated — awaiting native-speaker review
  'dialogs.undo.remaining': '{seconds, plural, one {# second left} other {# seconds left}}',
  // not translated — awaiting native-speaker review
  'dialogs.undo.held': 'Held open while you are on it.',
}
