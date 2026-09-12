/**
 * Measurement-capture messages — starting a measurement, the wizard, the review and the record.
 *
 * See the top of `messages/shell.ts` for the rules every family file follows. Two bind hardest
 * here, because this is the one screen a tailor uses standing, one-handed, with a tape in the
 * other hand:
 *
 *  - **Every refusal names what to do**, never a code. "Chest / bust must be between 22 in and
 *    59 in" is a thing a person can act on with the tape in their hand; "out of bounds" is not.
 *  - **Say what confirming does to the record.** A tailor's mental model is "fix the number", and
 *    the screen has to say, before they press it, that a confirmed measurement is never edited and
 *    a correction is a new version — because that is the rule that keeps a two-year-old job card
 *    readable.
 *
 * ## Why every string below is English in the Tamil catalogue
 *
 * Measurement vocabulary in Tamil tailoring is precise and regional, and a wrong word here is a
 * wrong garment. These are held back for native-speaker review with the rest of the shop-floor
 * vocabulary, and stay behind the 95% enablement gate until then.
 */
export const measurementsEn = {
  /* The destination. ------------------------------------------------------------------------ */
  'measurements.title': 'Measurements',
  'measurements.body':
    'Measure a customer for a garment against the template the shop has published for it. A confirmed measurement is a record: it is never edited, only corrected by a new version with a reason.',
  'measurements.start': 'Capture measurement',

  /* Starting. ------------------------------------------------------------------------------- */
  'measurements.start.title': 'Start measuring',
  'measurements.start.body':
    'Choose the customer and the garment. The template published for that garment decides what is measured and in which steps.',
  'measurements.start.catalogue.loading': 'the garments this branch offers',
  'measurements.start.catalogue.empty.title': 'Nothing to measure against yet',
  'measurements.start.catalogue.empty':
    'None of the garments this branch offers points at a measurement template. An administrator publishes the catalogue with a template on each service before a customer can be measured.',
  'measurements.start.customer.label': 'Find the customer',
  'measurements.start.customer.hint':
    'Name, customer number, or the last digits of the telephone number — at least {minimum} characters.',
  'measurements.start.customer.search': 'Search',
  'measurements.start.customer.searching': 'Searching…',
  'measurements.start.customer.tooShort': 'Type at least {minimum} characters, then search.',
  'measurements.start.customer.more':
    'More customers match than are shown. Narrow the search — a customer number or the last digits of the telephone number finds one person.',
  'measurements.start.customer.results': 'Customer',
  'measurements.start.customer.none':
    'No customer matches. Check the spelling, or register the customer at the counter first.',
  'measurements.start.customer.card': '{name} · {number} · {phone}',
  'measurements.start.customer.masked': '{name} · {number} · registered at another branch',
  'measurements.start.customer.preselected':
    'The customer was chosen on the previous screen. Search to choose a different one.',
  'measurements.start.garment.label': 'Garment',
  'measurements.start.garment.option': '{service} — {category}',
  'measurements.start.garment.choose': 'Choose a garment',
  'measurements.start.incomplete': 'Choose a customer and a garment before starting.',
  'measurements.start.action': 'Start measuring',
  'measurements.start.starting': 'Starting…',
  'measurements.start.resumes':
    'If somebody on this branch has already started measuring this customer for this garment, you pick up where they left off rather than start a second set.',
  'measurements.start.offlineAction': 'Starting a measurement',

  /* The wizard. ----------------------------------------------------------------------------- */
  'measurements.wizard.loading': 'the garment being measured',
  'measurements.wizard.open': '{template} — version {version}, published {date}.',
  'measurements.wizard.reused':
    'The values were pre-filled from an earlier measurement of this customer. Check every one against the tape before confirming.',
  'measurements.wizard.steps': 'Steps',
  'measurements.wizard.step': '{index}. {name}',
  'measurements.wizard.progress': 'Step {index} of {count}',
  'measurements.wizard.review': 'Review',
  'measurements.wizard.unit.label': 'Show measurements in',
  'measurements.wizard.unit.hint':
    'Millimetres are stored whichever unit is shown. Switching converts nothing and rounds nothing.',
  'measurements.wizard.unit.inches': 'Inches',
  'measurements.wizard.unit.centimetres': 'Centimetres',
  'measurements.wizard.unit.confirm.title': 'Show the values in {unit}?',
  'measurements.wizard.unit.confirm.body':
    'Every value stays exactly as measured; only how it is shown changes. Values already entered are shown converted, not re-measured — check them against the tape if in doubt.',
  'measurements.wizard.unit.confirm.action': 'Show in {unit}',
  'measurements.wizard.unit.changed': 'Showing {unit}.',
  'measurements.wizard.hidden': '{label} is not asked for with these answers.',
  'measurements.wizard.undecidable':
    '{label} is asked for because its rule cannot be settled here: design choices only exist inside an order.',
  'measurements.wizard.diagram': 'Diagram: {alt}',
  'measurements.wizard.count.hint': 'A whole number.',
  'measurements.wizard.clear': 'Clear {label}',
  'measurements.wizard.back': 'Back',
  'measurements.wizard.next': 'Next',
  'measurements.wizard.toReview': 'Review',
  'measurements.wizard.leave': 'Save and leave',
  'measurements.wizard.saving': 'Saving…',
  'measurements.wizard.saved': 'Saved.',
  'measurements.wizard.notSaved': 'Not saved — {reason}',
  'measurements.wizard.conflict.title': 'Somebody else saved this step first',
  'measurements.wizard.conflict.body':
    'Another person on this branch saved this garment while you were measuring. Reload to see their values; what you typed on this step will need entering again.',
  'measurements.wizard.conflict.reload': 'Reload',
  'measurements.wizard.review.title': 'Review and confirm',
  'measurements.wizard.review.hint':
    'Every value, in the unit on screen. Confirming makes this the record: it cannot be edited afterwards, only corrected by a new version with a reason.',
  'measurements.wizard.review.caption': 'Values on the {step} step',
  'measurements.wizard.review.empty': 'Not entered',
  'measurements.wizard.review.notAsked': 'Not asked for',
  'measurements.wizard.confirm': 'Confirm measurements',
  'measurements.wizard.confirming': 'Confirming…',
  'measurements.wizard.confirm.title': 'Confirm these measurements?',
  'measurements.wizard.confirm.body':
    'They become the record for this customer and garment. Nothing can be edited afterwards; a correction is a new version with a reason, and this one stays readable.',
  'measurements.wizard.confirmed.title': 'Measurements confirmed',
  'measurements.wizard.confirmed.body':
    'Version {version} is now the record, taken {date}. It cannot be edited; a change from here is a new version with a reason.',
  'measurements.wizard.confirmed.another': 'Measure another customer',
  'measurements.wizard.consumed.title': 'These measurements are already confirmed',
  'measurements.wizard.consumed.body':
    'The draft became a record and is closed. Start again from the customer to take new measurements.',
  'measurements.wizard.expired.title': 'This draft has expired',
  'measurements.wizard.expired.body':
    'It was not confirmed in time and its values were discarded. Start again from the customer.',
  'measurements.wizard.notFound': 'No garment being measured matches that address.',
  'measurements.wizard.offline.save': 'Saving this step',
  'measurements.wizard.offline.confirm': 'Confirming the measurements',
  'measurements.wizard.required': '{label} is required.',
  'measurements.wizard.check.failed':
    'The measurements cannot be confirmed as they stand. Correct the following, then confirm again.',

  /* Refusals, in the shop's words. ---------------------------------------------------------- */
  'measurements.problem.consentMissing':
    'The customer has not agreed to their measurements being kept. Record the consent on the customer, then confirm.',
  'measurements.problem.draftChanged':
    'Somebody else on this branch changed this garment first. Reload before saving again.',
  'measurements.problem.draftExpired': 'This draft has expired. Start again from the customer.',
  'measurements.problem.alreadyConfirmed': 'These measurements were already confirmed.',
  'measurements.problem.templateNotPublished':
    'The template this garment is measured against is no longer published. Ask an administrator.',
  'measurements.problem.draftNotFound': 'No garment being measured matches that address.',
  'measurements.problem.validationFailed':
    'Some values cannot be confirmed as they stand. Each one is listed above.',
  'measurements.problem.branchRequired':
    'Measurements are taken at a branch, and this session is not working at one. Sign in at the branch the customer is standing in.',
} as const

export const measurementsTa: Record<keyof typeof measurementsEn, string> = {
  'measurements.title': 'அளவுகள்',
  // not translated — awaiting native-speaker review
  'measurements.body':
    'Measure a customer for a garment against the template the shop has published for it. A confirmed measurement is a record: it is never edited, only corrected by a new version with a reason.',
  // not translated — awaiting native-speaker review
  'measurements.start': 'Capture measurement',
  // not translated — awaiting native-speaker review
  'measurements.start.title': 'Start measuring',
  // not translated — awaiting native-speaker review
  'measurements.start.body':
    'Choose the customer and the garment. The template published for that garment decides what is measured and in which steps.',
  // not translated — awaiting native-speaker review
  'measurements.start.catalogue.loading': 'the garments this branch offers',
  // not translated — awaiting native-speaker review
  'measurements.start.catalogue.empty.title': 'Nothing to measure against yet',
  // not translated — awaiting native-speaker review
  'measurements.start.catalogue.empty':
    'None of the garments this branch offers points at a measurement template. An administrator publishes the catalogue with a template on each service before a customer can be measured.',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.label': 'Find the customer',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.hint':
    'Name, customer number, or the last digits of the telephone number — at least {minimum} characters.',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.search': 'Search',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.searching': 'Searching…',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.tooShort': 'Type at least {minimum} characters, then search.',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.more':
    'More customers match than are shown. Narrow the search — a customer number or the last digits of the telephone number finds one person.',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.results': 'Customer',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.none':
    'No customer matches. Check the spelling, or register the customer at the counter first.',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.card': '{name} · {number} · {phone}',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.masked': '{name} · {number} · registered at another branch',
  // not translated — awaiting native-speaker review
  'measurements.start.customer.preselected':
    'The customer was chosen on the previous screen. Search to choose a different one.',
  // not translated — awaiting native-speaker review
  'measurements.start.garment.label': 'Garment',
  // not translated — awaiting native-speaker review
  'measurements.start.garment.option': '{service} — {category}',
  // not translated — awaiting native-speaker review
  'measurements.start.garment.choose': 'Choose a garment',
  // not translated — awaiting native-speaker review
  'measurements.start.incomplete': 'Choose a customer and a garment before starting.',
  // not translated — awaiting native-speaker review
  'measurements.start.action': 'Start measuring',
  // not translated — awaiting native-speaker review
  'measurements.start.starting': 'Starting…',
  // not translated — awaiting native-speaker review
  'measurements.start.resumes':
    'If somebody on this branch has already started measuring this customer for this garment, you pick up where they left off rather than start a second set.',
  // not translated — awaiting native-speaker review
  'measurements.start.offlineAction': 'Starting a measurement',
  // not translated — awaiting native-speaker review
  'measurements.wizard.loading': 'the garment being measured',
  // not translated — awaiting native-speaker review
  'measurements.wizard.open': '{template} — version {version}, published {date}.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.reused':
    'The values were pre-filled from an earlier measurement of this customer. Check every one against the tape before confirming.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.steps': 'Steps',
  // not translated — awaiting native-speaker review
  'measurements.wizard.step': '{index}. {name}',
  // not translated — awaiting native-speaker review
  'measurements.wizard.progress': 'Step {index} of {count}',
  // not translated — awaiting native-speaker review
  'measurements.wizard.review': 'Review',
  // not translated — awaiting native-speaker review
  'measurements.wizard.unit.label': 'Show measurements in',
  // not translated — awaiting native-speaker review
  'measurements.wizard.unit.hint':
    'Millimetres are stored whichever unit is shown. Switching converts nothing and rounds nothing.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.unit.inches': 'Inches',
  // not translated — awaiting native-speaker review
  'measurements.wizard.unit.centimetres': 'Centimetres',
  // not translated — awaiting native-speaker review
  'measurements.wizard.unit.confirm.title': 'Show the values in {unit}?',
  // not translated — awaiting native-speaker review
  'measurements.wizard.unit.confirm.body':
    'Every value stays exactly as measured; only how it is shown changes. Values already entered are shown converted, not re-measured — check them against the tape if in doubt.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.unit.confirm.action': 'Show in {unit}',
  // not translated — awaiting native-speaker review
  'measurements.wizard.unit.changed': 'Showing {unit}.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.hidden': '{label} is not asked for with these answers.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.undecidable':
    '{label} is asked for because its rule cannot be settled here: design choices only exist inside an order.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.diagram': 'Diagram: {alt}',
  // not translated — awaiting native-speaker review
  'measurements.wizard.count.hint': 'A whole number.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.clear': 'Clear {label}',
  // not translated — awaiting native-speaker review
  'measurements.wizard.back': 'Back',
  // not translated — awaiting native-speaker review
  'measurements.wizard.next': 'Next',
  // not translated — awaiting native-speaker review
  'measurements.wizard.toReview': 'Review',
  // not translated — awaiting native-speaker review
  'measurements.wizard.leave': 'Save and leave',
  // not translated — awaiting native-speaker review
  'measurements.wizard.saving': 'Saving…',
  // not translated — awaiting native-speaker review
  'measurements.wizard.saved': 'Saved.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.notSaved': 'Not saved — {reason}',
  // not translated — awaiting native-speaker review
  'measurements.wizard.conflict.title': 'Somebody else saved this step first',
  // not translated — awaiting native-speaker review
  'measurements.wizard.conflict.body':
    'Another person on this branch saved this garment while you were measuring. Reload to see their values; what you typed on this step will need entering again.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.conflict.reload': 'Reload',
  // not translated — awaiting native-speaker review
  'measurements.wizard.review.title': 'Review and confirm',
  // not translated — awaiting native-speaker review
  'measurements.wizard.review.hint':
    'Every value, in the unit on screen. Confirming makes this the record: it cannot be edited afterwards, only corrected by a new version with a reason.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.review.caption': 'Values on the {step} step',
  // not translated — awaiting native-speaker review
  'measurements.wizard.review.empty': 'Not entered',
  // not translated — awaiting native-speaker review
  'measurements.wizard.review.notAsked': 'Not asked for',
  // not translated — awaiting native-speaker review
  'measurements.wizard.confirm': 'Confirm measurements',
  // not translated — awaiting native-speaker review
  'measurements.wizard.confirming': 'Confirming…',
  // not translated — awaiting native-speaker review
  'measurements.wizard.confirm.title': 'Confirm these measurements?',
  // not translated — awaiting native-speaker review
  'measurements.wizard.confirm.body':
    'They become the record for this customer and garment. Nothing can be edited afterwards; a correction is a new version with a reason, and this one stays readable.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.confirmed.title': 'Measurements confirmed',
  // not translated — awaiting native-speaker review
  'measurements.wizard.confirmed.body':
    'Version {version} is now the record, taken {date}. It cannot be edited; a change from here is a new version with a reason.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.confirmed.another': 'Measure another customer',
  // not translated — awaiting native-speaker review
  'measurements.wizard.consumed.title': 'These measurements are already confirmed',
  // not translated — awaiting native-speaker review
  'measurements.wizard.consumed.body':
    'The draft became a record and is closed. Start again from the customer to take new measurements.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.expired.title': 'This draft has expired',
  // not translated — awaiting native-speaker review
  'measurements.wizard.expired.body':
    'It was not confirmed in time and its values were discarded. Start again from the customer.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.notFound': 'No garment being measured matches that address.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.offline.save': 'Saving this step',
  // not translated — awaiting native-speaker review
  'measurements.wizard.offline.confirm': 'Confirming the measurements',
  // not translated — awaiting native-speaker review
  'measurements.wizard.required': '{label} is required.',
  // not translated — awaiting native-speaker review
  'measurements.wizard.check.failed':
    'The measurements cannot be confirmed as they stand. Correct the following, then confirm again.',
  // not translated — awaiting native-speaker review
  'measurements.problem.consentMissing':
    'The customer has not agreed to their measurements being kept. Record the consent on the customer, then confirm.',
  // not translated — awaiting native-speaker review
  'measurements.problem.draftChanged':
    'Somebody else on this branch changed this garment first. Reload before saving again.',
  // not translated — awaiting native-speaker review
  'measurements.problem.draftExpired': 'This draft has expired. Start again from the customer.',
  // not translated — awaiting native-speaker review
  'measurements.problem.alreadyConfirmed': 'These measurements were already confirmed.',
  // not translated — awaiting native-speaker review
  'measurements.problem.templateNotPublished':
    'The template this garment is measured against is no longer published. Ask an administrator.',
  // not translated — awaiting native-speaker review
  'measurements.problem.draftNotFound': 'No garment being measured matches that address.',
  // not translated — awaiting native-speaker review
  'measurements.problem.validationFailed':
    'Some values cannot be confirmed as they stand. Each one is listed above.',
  // not translated — awaiting native-speaker review
  'measurements.problem.branchRequired':
    'Measurements are taken at a branch, and this session is not working at one. Sign in at the branch the customer is standing in.',
}
